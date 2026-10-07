using System.Globalization;
using BudgetTracker.Events;
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
        // FR009: Ensure data/ and logs/ folders exist
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);

        var storage = new StorageService(DataDirectory);
        var transactionService = new TransactionService(storage);
        var logger = new LoggerService(transactionService, LogDirectory);

        Console.WriteLine("=== Budget Tracker ===");
        Console.WriteLine("Welcome! Manage your income and expenses.");
        Console.WriteLine();

        bool exit = false;
        while (!exit)
        {
            PrintMenu();

            Console.Write("\nSelect an option: ");
            string choice = ReadRequiredLine();

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
                        exit = true;
                        Console.WriteLine("Goodbye!");
                        break;
                    default:
                        Console.WriteLine("Invalid option. Please choose 1-4.");
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
        Console.WriteLine("│ 4. Exit                     │");
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
            string typeInput = ReadRequiredLine();

            if (Enum.TryParse<TransactionType>(typeInput, ignoreCase: true, out type))
            {
                break;
            }
            Console.WriteLine("Invalid type. Please enter 'Income' or 'Expense'.");
        }

        // Description
        Console.Write("Enter description: ");
        string description = ReadRequiredLine().Trim();
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
            string amountInput = ReadRequiredLine();

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
        string idInput = ReadRequiredLine();

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
            string input = ReadRequiredLine();

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
        string input = ReadRequiredLine();

        if (Enum.TryParse<TransactionType>(input.Trim(), ignoreCase: true, out TransactionType type))
        {
            return type;
        }

        return null;
    }
    private static string ReadRequiredLine()
    {
        string? input = Console.ReadLine();
        if (input is null)
        {
            throw new EndOfStreamException("Input stream ended.");
        }

        return input;
    }

    private static string SanitizeForTerminal(string value)
    {
        return new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }

}
