namespace BudgetTracker.Models;

/// <summary>
/// The type of a transaction, stored as text in JSON (e.g. "Income", "Expense").
/// </summary>
public enum TransactionType
{
    Income,
    Expense
}
