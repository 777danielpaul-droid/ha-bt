using System.Globalization;
using BudgetTracker.Events;
using BudgetTracker.Helpers;
using BudgetTracker.Models;
using BudgetTracker.Services;

namespace BudgetTracker;

/// <summary>
/// Main program — handles directory bootstrap, the interactive menu loop,
/// and all user interaction for the Budget Tracker.
/// </summary>
internal class Program
{
    private const string DataDirectory = "data";
    private const string LogDirectory = "logs";

    private static void Main(string[] args)
    {
        // Default: web UI (http://localhost:5080). Pass --console to use the original terminal menu.
        if (!args.Contains("--console"))
        {
            WebApp.Run(args);
            return;
        }

        // FR009: Ensure data/ and logs/ folders exist
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);

        var storage = new StorageService(DataDirectory);
        var transactionService = new TransactionService(storage);
        var logger = new LoggerService(transactionService, LogDirectory);
        var budgetStorage = new BudgetStorageService(DataDirectory);
        Budget budget = LoadOrCreateBudget(budgetStorage);
        var budgetService = new BudgetService(budgetStorage, transactionService, budget);
        budgetService.BudgetWarning += OnBudgetWarning;

        Console.WriteLine("=== Budget Tracker ===");
        Console.WriteLine("Welcome! Manage your income and expenses.");
        Console.WriteLine();

        bool exit = false;
        while (!exit)
        {
            PrintBudgetOverview(budgetService);
            PrintMenu();

            Console.Write("\nSelect an option: ");
            string choice = ConsoleHelper.ReadRequiredLine();

            try
            {
                switch (choice.Trim())
                {
                    case "1":
                        AddTransaction(transactionService);
                        break;
                    case "2":
                        RemoveTransaction(transactionService);
                        break;
                    case "3":
                        GenerateReport(transactionService);
                        break;
                    case "4":
                        BudgetSettings(budgetService);
                        break;
                    case "5":
                        exit = true;
                        Console.WriteLine("Goodbye!");
                        break;
                    default:
                        Console.WriteLine("Invalid option. Please choose 1-5.");
                        break;
                }
            }
            catch (EndOfStreamException)
            {
                exit = true;
                Console.WriteLine("Input stream ended. Exiting.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {SanitizeForTerminal(ex.Message)}");
            }

            if (!exit && !Console.IsInputRedirected)
            {
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey(true);
                Console.WriteLine();
            }
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine("┌─────────────────────────────┐");
        Console.WriteLine("│          MAIN MENU          │");
        Console.WriteLine("├─────────────────────────────┤");
        Console.WriteLine("│ 1. Add Transaction          │");
        Console.WriteLine("│ 2. Remove Transaction       │");
        Console.WriteLine("│ 3. Generate Report          │");
        Console.WriteLine("│ 4. Budget Settings          │");
        Console.WriteLine("│ 5. Exit                     │");
        Console.WriteLine("└─────────────────────────────┘");
    }

    /// <summary>
    /// FR003: Prompt for type, description, and amount.
    /// Uses Enum.TryParse (ignoring case) and decimal.TryParse, asking again on invalid input.
    /// </summary>
    private static void AddTransaction(TransactionService service)
    {
        Console.WriteLine("\n--- Add Transaction ---");

        // Parse transaction type with Enum.TryParse, ignoring case
        TransactionType type;
        while (true)
        {
            Console.Write("Enter type (Income/Expense): ");
            string typeInput = ConsoleHelper.ReadRequiredLine();

            if (Enum.TryParse<TransactionType>(typeInput, ignoreCase: true, out type))
            {
                break;
            }
            Console.WriteLine("Invalid type. Please enter 'Income' or 'Expense'.");
        }

        // Description
        Console.Write("Enter description: ");
        string description = ConsoleHelper.ReadRequiredLine().Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            description = "(no description)";
        }
        if (description.Length > StorageService.MaxDescriptionLength || description.Any(char.IsControl))
        {
            Console.WriteLine($"Description must be at most {StorageService.MaxDescriptionLength} characters and contain no control characters.");
            return;
        }

        // Parse amount with decimal.TryParse, asking again on invalid input
        decimal amount;
        while (true)
        {
            Console.Write("Enter amount: ");
            string amountInput = ConsoleHelper.ReadRequiredLine();

            if (decimal.TryParse(amountInput.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount) &&
                amount > 0 && amount <= StorageService.MaxTransactionAmount)
            {
                break;
            }
            Console.WriteLine("Invalid amount. Please enter a positive number.");
        }

        Transaction transaction = service.AddTransaction(type, description, amount);

        Console.WriteLine($"\nTransaction added!");
        Console.WriteLine($"  Id:          {transaction.Id}");
        Console.WriteLine($"  Timestamp:   {transaction.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Type:        {transaction.Type}");
        Console.WriteLine($"  Description: {transaction.Description}");
        Console.WriteLine($"  Amount:      {transaction.Amount:C}");
    }

    /// <summary>
    /// FR005: Ask for an Id (Guid.TryParse), search all files, remove matching.
    /// </summary>
    private static void RemoveTransaction(TransactionService service)
    {
        Console.WriteLine("\n--- Remove Transaction ---");

        Console.Write("Enter transaction Id (GUID): ");
        string idInput = ConsoleHelper.ReadRequiredLine();

        if (!Guid.TryParse(idInput, out Guid id))
        {
            Console.WriteLine("Invalid Id format. Please enter a valid GUID.");
            return;
        }

        bool removed = service.RemoveTransaction(id);

        if (removed)
        {
            Console.WriteLine($"Transaction {id} was found and removed.");
        }
        else
        {
            Console.WriteLine($"No transaction found with Id: {id}");
        }
    }

    /// <summary>
    /// FR006: Ask for start and end date (yyyy-MM-dd), load day files in range,
    /// flatten with SelectMany, print transactions and totals.
    /// </summary>
    private static void GenerateReport(TransactionService service)
    {
        Console.WriteLine("\n--- Generate Report ---");

        DateOnly start = PromptForDate("Enter start date (yyyy-MM-dd): ");
        DateOnly end = PromptForDate("Enter end date (yyyy-MM-dd): ");

        if (end < start)
        {
            Console.WriteLine("End date must be on or after the start date.");
            return;
        }

        // Optionally filter by type
        TransactionType? filterType = PromptForTypeFilter();

        IEnumerable<Transaction> transactions = service.GetTransactions(start, end);

        if (filterType.HasValue)
        {
            transactions = transactions.Where(t => t.Type == filterType.Value);
        }

        // Materialize for counting and iterating
        List<Transaction> list = transactions.ToList();

        if (list.Count == 0)
        {
            Console.WriteLine("\nNo transactions found in the selected period.");
            return;
        }

        Console.WriteLine($"\n--- Report ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}) ---\n");

        Console.WriteLine($"{"Id",-36} {"Date",-12} {"Type",-8} {"Amount",-10} Description");
        Console.WriteLine(new string('-', 80));

        foreach (Transaction t in list)
        {
            string sign = t.Type == TransactionType.Income ? "+" : "-";
            string safeDescription = SanitizeForTerminal(t.Description);
            Console.WriteLine($"{t.Id,-36} {t.Timestamp:yyyy-MM-dd} {t.Type,-8} {sign}{t.Amount,-9} {safeDescription}");
        }

        Console.WriteLine(new string('-', 80));

        decimal totalIncome = list.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        decimal totalExpenses = list.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        decimal balance = totalIncome - totalExpenses;

        Console.WriteLine($"\nTotal Income:    {totalIncome:C}");
        Console.WriteLine($"Total Expenses:  {totalExpenses:C}");
        Console.WriteLine($"Balance:         {balance:C}");
    }

    /// <summary>
    /// Prompts the user for a date in yyyy-MM-dd format, asking again on invalid input.
    /// </summary>
    private static DateOnly PromptForDate(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string input = ConsoleHelper.ReadRequiredLine();

            if (DateOnly.TryParseExact(
                    input.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly date))
            {
                return date;
            }

            Console.WriteLine("Invalid date format. Please use yyyy-MM-dd (e.g. 2026-03-14).");
        }
    }

    /// <summary>
    /// Optionally filters the report by transaction type.
    /// Returns null if no filter (show all).
    /// </summary>
    private static TransactionType? PromptForTypeFilter()
    {
        Console.Write("\nFilter by type? (Income/Expense/None): ");
        string input = ConsoleHelper.ReadRequiredLine();

        if (Enum.TryParse<TransactionType>(input.Trim(), ignoreCase: true, out TransactionType type))
        {
            return type;
        }

        return null;
    }
    private static Budget LoadOrCreateBudget(BudgetStorageService storage)
    {
        Budget? saved = storage.Load();
        if (saved is not null) return saved;

        Console.WriteLine("=== First Start: Budget Setup ===");
        decimal amount = ConsoleHelper.ReadPositiveDecimal("Enter your starting budget: ", StorageService.MaxTransactionAmount);
        Budget budget = new(amount);
        storage.Save(budget);
        Console.WriteLine($"Budget saved: {budget.TotalAmount:C}\n");
        return budget;
    }

    private static void PrintBudgetOverview(BudgetService budget)
    {
        Console.WriteLine();
        Console.WriteLine("--- Budget Overview ---");
        Console.WriteLine($"Budget:     {budget.TotalAmount:C}");
        Console.WriteLine($"Income:     {budget.IncomeAmount:C}");
        Console.WriteLine($"Spent:      {budget.SpentAmount:C}");
        Console.WriteLine($"Remaining:  {budget.RemainingAmount:C} ({budget.RemainingPercent:F1}%)");

        if (budget.RemainingAmount <= 0)
            Console.WriteLine("[!] Your budget is exhausted. You can add funds in Budget Settings.");
        else if (budget.RemainingPercent <= budget.WarningThresholdPercent)
            Console.WriteLine("[!] Your budget is running low.");
        Console.WriteLine();
    }

    private static void BudgetSettings(BudgetService budget)
    {
        bool back = false;
        while (!back)
        {
            Console.WriteLine("\n--- Budget Settings ---");
            Console.WriteLine($"Current budget: {budget.TotalAmount:C}");
            Console.WriteLine($"Remaining:      {budget.RemainingAmount:C}");
            Console.WriteLine($"Warning at:     {budget.WarningThresholdPercent:F0}% remaining");
            Console.WriteLine();
            Console.WriteLine("1. Add custom amount");
            Console.WriteLine($"2. Bonus top-up (+{BudgetService.BonusTopUpAmount:N0})");
            Console.WriteLine("3. Change warning threshold");
            Console.WriteLine("4. Back");
            Console.Write("\nSelect an option: ");

            switch (ConsoleHelper.ReadRequiredLine().Trim())
            {
                case "1":
                    decimal amount = ConsoleHelper.ReadPositiveDecimal("Amount to add: ", StorageService.MaxTransactionAmount);
                    budget.AddFunds(amount);
                    Console.WriteLine($"Added {amount:C}. New budget: {budget.TotalAmount:C}");
                    break;
                case "2":
                    budget.AddBonusFunds();
                    Console.WriteLine($"BONUS! +{BudgetService.BonusTopUpAmount:N0} added to your budget.");
                    Console.WriteLine($"New budget: {budget.TotalAmount:C}");
                    break;
                case "3":
                    decimal threshold = ConsoleHelper.ReadPositiveDecimal("Warn when remaining budget reaches this percentage (1-99): ", 99m);
                    budget.ChangeWarningThreshold(threshold);
                    Console.WriteLine($"Warning threshold changed to {threshold:F0}%.");
                    break;
                case "4":
                    back = true;
                    break;
                default:
                    Console.WriteLine("Invalid option. Please choose 1-4.");
                    break;
            }
        }
    }

    private static void OnBudgetWarning(object? sender, BudgetWarningEventArgs e)
    {
        Console.WriteLine();
        if (e.IsExhausted)
            Console.WriteLine("[BUDGET WARNING] Your budget is exhausted!");
        else
            Console.WriteLine($"[BUDGET WARNING] Only {e.RemainingPercent:F1}% of your budget is left ({e.RemainingAmount:C}).");
        Console.WriteLine("Open Budget Settings from the main menu to add funds.");
    }

    private static string SanitizeForTerminal(string value)
    {
        return new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }

}
