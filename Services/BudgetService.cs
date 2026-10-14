using BudgetTracker.Events;
using BudgetTracker.Models;

namespace BudgetTracker.Services;

/// <summary>
/// Contains all budget-related business logic. Available funds are the budget (start amount plus
/// top-ups) plus all income; expenses reduce what is left. So budget, income and account balance
/// always stay in sync: Remaining = TotalAmount + Income - Expenses.
/// </summary>
public class BudgetService
{
    public const decimal BonusTopUpAmount = 7_777_777m;

    private readonly BudgetStorageService _storage;
    private readonly TransactionService _transactions;
    private Budget _budget;

    public event EventHandler<BudgetWarningEventArgs>? BudgetWarning;

    public BudgetService(BudgetStorageService storage, TransactionService transactions, Budget budget)
    {
        _storage = storage;
        _transactions = transactions;
        _budget = budget;
        _transactions.TransactionAdded += HandleTransactionAdded;
    }

    public decimal TotalAmount => _budget.TotalAmount;
    public decimal WarningThresholdPercent => _budget.WarningThresholdPercent;
    public decimal IncomeAmount => _transactions.GetAllTransactions()
        .Where(t => t.Type == TransactionType.Income)
        .Sum(t => t.Amount);
    public decimal SpentAmount => _transactions.GetAllTransactions()
        .Where(t => t.Type == TransactionType.Expense)
        .Sum(t => t.Amount);
    /// <summary>Budget (start + top-ups) plus all income = everything that can be spent.</summary>
    public decimal AvailableFunds => TotalAmount + IncomeAmount;
    public decimal RemainingAmount => AvailableFunds - SpentAmount;
    public decimal RemainingPercent => AvailableFunds == 0 ? 0 : RemainingAmount / AvailableFunds * 100m;

    public void AddFunds(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Top-up amount must be positive.");
        _budget = _budget with { TotalAmount = _budget.TotalAmount + amount };
        _storage.Save(_budget);
    }

    public void AddBonusFunds() => AddFunds(BonusTopUpAmount);

    public void ChangeWarningThreshold(decimal percent)
    {
        if (percent <= 0 || percent >= 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Warning threshold must be between 0 and 100 percent.");
        _budget = _budget with { WarningThresholdPercent = percent };
        _storage.Save(_budget);
    }

    private void HandleTransactionAdded(object? sender, TransactionAddedEventArgs e)
    {
        if (e.Transaction.Type != TransactionType.Expense) return;

        decimal percent = RemainingPercent;
        if (RemainingAmount <= 0 || percent <= WarningThresholdPercent)
        {
            BudgetWarning?.Invoke(this, new BudgetWarningEventArgs(
                RemainingAmount,
                percent,
                RemainingAmount <= 0));
        }
    }
}
