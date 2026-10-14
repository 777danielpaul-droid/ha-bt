using BudgetTracker.Models;

namespace BudgetTracker.Events;

/// <summary>
/// Event arguments raised when a transaction is added. Exposes the added Transaction.
/// </summary>
public class TransactionAddedEventArgs : EventArgs
{
    public Transaction Transaction { get; }

    public TransactionAddedEventArgs(Transaction transaction)
    {
        Transaction = transaction;
    }
}
