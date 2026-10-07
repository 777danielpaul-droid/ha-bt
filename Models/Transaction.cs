namespace BudgetTracker.Models;

/// <summary>
/// A single financial transaction. Amount is always positive; Type determines
/// whether it adds to (Income) or subtracts from (Expense) the balance.
/// </summary>
public record Transaction(
    Guid Id,
    DateTimeOffset Timestamp,
    TransactionType Type,
    string Description,
    decimal Amount)
{
    public void Validate()
    {
        if (Id == Guid.Empty)
            throw new ArgumentException("Id must not be empty.", nameof(Id));

        if (Type is not TransactionType.Income and not TransactionType.Expense)
            throw new ArgumentException("Type must be Income or Expense.", nameof(Type));

        if (string.IsNullOrWhiteSpace(Description) || Description.Length > StorageServicesMaxDescription)
            throw new ArgumentException("Description must be between 1 and 500 characters.", nameof(Description));

        if (Description.Any(char.IsControl))
            throw new ArgumentException("Description must not contain control characters.", nameof(Description));

        if (Amount <= 0 || Amount > 999_999_999_99m)
            throw new ArgumentException("Amount must be greater than zero and within the supported limit.", nameof(Amount));
    }

    private const int StorageServicesMaxDescription = 500;
}
