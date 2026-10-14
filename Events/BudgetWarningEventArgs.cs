namespace BudgetTracker.Events;

/// <summary>
/// Event data used when the remaining budget reaches a warning level.
/// </summary>
public class BudgetWarningEventArgs : EventArgs
{
    public decimal RemainingAmount { get; }
    public decimal RemainingPercent { get; }
    public bool IsExhausted { get; }

    public BudgetWarningEventArgs(decimal remainingAmount, decimal remainingPercent, bool isExhausted)
    {
        RemainingAmount = remainingAmount;
        RemainingPercent = remainingPercent;
        IsExhausted = isExhausted;
    }
}
