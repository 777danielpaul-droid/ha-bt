using BudgetTracker.Events;
using BudgetTracker.Models;

namespace BudgetTracker.Services;

/// <summary>
/// Core transaction service. Receives a StorageService for persistence and
/// raises TransactionAdded events whenever a transaction is added.
/// </summary>
public class TransactionService
{
    private readonly StorageService _storage;

    /// <summary>
    /// Raised every time a transaction is successfully added.
    /// </summary>
    public event EventHandler<TransactionAddedEventArgs>? TransactionAdded;

    public TransactionService(StorageService storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Adds a transaction: sets Id and Timestamp, validates Amount,
    /// delegates persistence to StorageService, then raises TransactionAdded.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when Amount is zero or negative.</exception>
    public Transaction AddTransaction(TransactionType type, string description, decimal amount)
    {
        Transaction transaction = new(
            Id: Guid.NewGuid(),
            Timestamp: DateTimeOffset.Now,
            Type: type,
            Description: description,
            Amount: amount);

        // Amount is always positive; Type decides income vs expense.
        transaction.Validate();

        _storage.SaveTransaction(transaction);

        OnTransactionAdded(new TransactionAddedEventArgs(transaction));

        return transaction;
    }

    /// <summary>
    /// Removes a transaction by Id. Returns true if found, false otherwise.
    /// </summary>
    public bool RemoveTransaction(Guid id)
    {
        return _storage.RemoveTransaction(id);
    }

    /// <summary>
    /// Loads transactions for the given date range.
    /// </summary>
    public IEnumerable<Transaction> GetTransactions(DateOnly start, DateOnly end)
    {
        return _storage.LoadTransactionsForDateRange(start, end);
    }

    /// <summary>
    /// Loads all transactions across all day files.
    /// </summary>
    public IEnumerable<Transaction> GetAllTransactions()
    {
        return _storage.LoadAllTransactions();
    }

    /// <summary>
    /// Raises the TransactionAdded event.
    /// </summary>
    protected virtual void OnTransactionAdded(TransactionAddedEventArgs e)
    {
        TransactionAdded?.Invoke(this, e);
    }
}
