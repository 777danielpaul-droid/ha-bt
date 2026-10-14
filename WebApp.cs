using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BudgetTracker.Events;
using BudgetTracker.Models;
using BudgetTracker.Services;

namespace BudgetTracker;

/// <summary>
/// Thin HTTP layer on top of the existing services. It contains no business logic of its own:
/// every endpoint calls TransactionService / BudgetService, exactly like the console menu does.
/// </summary>
internal static class WebApp
{
    private const string DataDirectory = "data";
    private const string LogDirectory = "logs";

    public static void Run(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);

        var builder = WebApplication.CreateBuilder(args);
        if (builder.Configuration["urls"] is null)
            builder.WebHost.UseUrls("http://localhost:5080"); // local only
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        var state = new AppState();

        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (Exception ex) when (!ctx.Response.HasStarted)
            {
                bool expected = ex is ArgumentException or InvalidDataException
                    or InvalidOperationException or IOException;
                ctx.Response.StatusCode = expected ? 400 : 500;
                string msg = expected ? Clean(ex.Message) : "Unexpected server error.";
                await ctx.Response.WriteAsJsonAsync(new { error = msg });
            }
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();

        // ---- State -------------------------------------------------------------------------
        app.MapGet("/api/state", () => { lock (state.Gate) return Results.Ok(state.Snapshot()); });

        app.MapPost("/api/setup", (AmountRequest req) =>
        {
            lock (state.Gate)
            {
                state.EnsureBudget();
                if (state.Budget is not null)
                    return Results.Conflict(new { error = "Budget is already set up." });

                decimal amount = ValidAmount(req.Amount);
                var budget = new Budget(amount);
                state.BudgetStorage.Save(budget);
                state.CreateBudgetService(budget);
                return Results.Ok(state.Snapshot());
            }
        });

        // ---- Transactions ------------------------------------------------------------------
        app.MapGet("/api/transactions", (string? from, string? to, string? type) =>
        {
            lock (state.Gate)
            {
                IEnumerable<Transaction> items;
                if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
                {
                    items = state.Transactions.GetAllTransactions();
                }
                else
                {
                    DateOnly start = string.IsNullOrWhiteSpace(from) ? DateOnly.MinValue : ParseDate(from);
                    DateOnly end = string.IsNullOrWhiteSpace(to) ? DateOnly.MaxValue : ParseDate(to);
                    items = state.Transactions.GetTransactions(start, end);
                }

                if (Enum.TryParse(type, ignoreCase: true, out TransactionType filter) &&
                    Enum.IsDefined(filter))
                    items = items.Where(t => t.Type == filter);

                var list = items.OrderByDescending(t => t.Timestamp).ToList();
                decimal income = list.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
                decimal expenses = list.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

                return Results.Ok(new
                {
                    items = list,
                    count = list.Count,
                    totalIncome = income,
                    totalExpenses = expenses,
                    balance = income - expenses
                });
            }
        });

        app.MapPost("/api/transactions", (AddTransactionRequest req) =>
        {
            lock (state.Gate)
            {
                state.EnsureBudget();
                if (state.BudgetService is null)
                    return Results.BadRequest(new { error = "Please set up your budget first." });

                if (!Enum.TryParse(req.Type, ignoreCase: true, out TransactionType type) ||
                    !Enum.IsDefined(type))
                    return Results.BadRequest(new { error = "Type must be Income or Expense." });

                string description = (req.Description ?? "").Trim();
                if (description.Length == 0) description = "(no description)";
                if (description.Length > StorageService.MaxDescriptionLength || description.Any(char.IsControl))
                    return Results.BadRequest(new
                    {
                        error = $"Description must be at most {StorageService.MaxDescriptionLength} characters and contain no control characters."
                    });

                decimal amount = ValidAmount(req.Amount);

                state.LastWarning = null;
                Transaction tx = state.Transactions.AddTransaction(type, description, amount);
                BudgetWarningEventArgs? w = state.LastWarning; // set by the BudgetWarning event

                return Results.Ok(new
                {
                    transaction = tx,
                    warning = w is null ? null : new
                    {
                        isExhausted = w.IsExhausted,
                        remainingAmount = w.RemainingAmount,
                        remainingPercent = w.RemainingPercent
                    },
                    state = state.Snapshot()
                });
            }
        });

        app.MapDelete("/api/transactions/{id}", (string id) =>
        {
            lock (state.Gate)
            {
                if (!Guid.TryParse(id, out Guid guid))
                    return Results.BadRequest(new { error = "Invalid Id format." });
                if (!state.Transactions.RemoveTransaction(guid))
                    return Results.NotFound(new { error = $"No transaction found with Id: {guid}" });
                return Results.Ok(new { state = state.Snapshot() });
            }
        });

        // ---- Budget settings ---------------------------------------------------------------
        app.MapPost("/api/budget/topup", (AmountRequest req) =>
        {
            lock (state.Gate)
            {
                BudgetService svc = state.RequireBudget();
                decimal amount = ValidAmount(req.Amount);
                svc.AddFunds(amount);
                return Results.Ok(new { added = amount, state = state.Snapshot() });
            }
        });

        app.MapPost("/api/budget/bonus", () =>
        {
            lock (state.Gate)
            {
                BudgetService svc = state.RequireBudget();
                svc.AddBonusFunds();
                return Results.Ok(new { added = BudgetService.BonusTopUpAmount, state = state.Snapshot() });
            }
        });

        app.MapPut("/api/budget/threshold", (PercentRequest req) =>
        {
            lock (state.Gate)
            {
                BudgetService svc = state.RequireBudget();
                svc.ChangeWarningThreshold(req.Percent);
                return Results.Ok(state.Snapshot());
            }
        });

        app.Lifetime.ApplicationStarted.Register(() =>
            Console.WriteLine($"Budget Tracker running -> {string.Join(", ", app.Urls)}"));
        app.Run();
    }

    private static decimal ValidAmount(decimal amount)
    {
        if (amount <= 0 || amount > StorageService.MaxTransactionAmount)
            throw new ArgumentException("Please enter a positive amount within the supported limit.");
        return amount;
    }

    private static DateOnly ParseDate(string value)
    {
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly d))
            return d;
        throw new ArgumentException("Invalid date format. Please use yyyy-MM-dd.");
    }

    private static string Clean(string message)
    {
        message = Regex.Replace(message, @"\s*\(Parameter '[^']*'\)", "");
        message = Regex.Replace(message, @"\s*\(Actual value was [^)]*\)", "");
        return new string(message.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
    }

    private sealed record AmountRequest(decimal Amount);
    private sealed record PercentRequest(decimal Percent);
    private sealed record AddTransactionRequest(string? Type, string? Description, decimal Amount);

    /// <summary>Wires up the same service graph as the console Program and holds it for the web process.</summary>
    private sealed class AppState
    {
        public readonly object Gate = new();
        public StorageService Storage { get; }
        public TransactionService Transactions { get; }
        public BudgetStorageService BudgetStorage { get; }
        public BudgetService? BudgetService { get; private set; }
        public BudgetWarningEventArgs? LastWarning { get; set; }
        private readonly LoggerService _logger;

        public AppState()
        {
            Storage = new StorageService(DataDirectory);
            Transactions = new TransactionService(Storage);
            _logger = new LoggerService(Transactions, LogDirectory);
            BudgetStorage = new BudgetStorageService(DataDirectory);
            EnsureBudget();
        }

        public Budget? Budget => BudgetService is null ? null : new Budget(BudgetService.TotalAmount, BudgetService.WarningThresholdPercent);

        public void EnsureBudget()
        {
            if (BudgetService is not null) return;
            Budget? saved = BudgetStorage.Load();
            if (saved is not null) CreateBudgetService(saved);
        }

        public void CreateBudgetService(Budget budget)
        {
            BudgetService = new BudgetService(BudgetStorage, Transactions, budget);
            BudgetService.BudgetWarning += (_, e) => LastWarning = e;
        }

        public BudgetService RequireBudget()
        {
            EnsureBudget();
            return BudgetService ?? throw new InvalidOperationException("Please set up your budget first.");
        }

        public object Snapshot()
        {
            EnsureBudget();
            if (BudgetService is null)
                return new { needsSetup = true, bonusAmount = BudgetService.BonusTopUpAmount };

            BudgetService b = BudgetService;
            decimal income = b.IncomeAmount;
            decimal spent = b.SpentAmount;
            decimal funds = b.TotalAmount + income;
            decimal remaining = funds - spent;
            decimal percent = funds == 0 ? 0 : remaining / funds * 100m;

            return new
            {
                needsSetup = false,
                bonusAmount = BudgetService.BonusTopUpAmount,
                budget = new
                {
                    total = b.TotalAmount,
                    income,
                    available = funds,
                    spent,
                    remaining,
                    remainingPercent = percent,
                    warningThresholdPercent = b.WarningThresholdPercent,
                    isLow = remaining > 0 && percent <= b.WarningThresholdPercent,
                    isExhausted = remaining <= 0
                },
                // Everything that came in (budget + income) minus everything spent = remaining budget.
                totals = new { income = funds, expenses = spent, balance = remaining }
            };
        }
    }
}
