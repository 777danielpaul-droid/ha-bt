namespace BudgetTracker.Models;

/// <summary>
/// Stores the user's spending budget and the percentage at which a warning is shown.
/// Expenses are calculated from the transaction files, so they do not need to be duplicated here.
/// </summary>
public record Budget(decimal TotalAmount, decimal WarningThresholdPercent = 20m);
