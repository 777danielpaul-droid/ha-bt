using BudgetTracker.Events;

namespace BudgetTracker.Services;

/// <summary>
/// Logger service: subscribes to TransactionService.TransactionAdded and
/// appends one line per transaction to logs/transactions.log.
/// </summary>
public class LoggerService : IDisposable
{
    private readonly string _logFilePath;
    private readonly TransactionService _transactionService;
    private bool _disposed;

    public LoggerService(TransactionService transactionService, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _transactionService = transactionService;
        _logFilePath = Path.Combine(logDirectory, "transactions.log");
        _transactionService.TransactionAdded += OnTransactionAdded;
    }

    private void OnTransactionAdded(object? sender, TransactionAddedEventArgs e)
    {
        try
        {
            string description = SanitizeForSingleLine(e.Transaction.Description);
            string line = $"[{e.Transaction.Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                          $"{(e.Transaction.Type == Models.TransactionType.Income ? "+" : "-")} " +
                          $"{e.Transaction.Amount:C} - {description} " +
                          $"(Id: {e.Transaction.Id})";

            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to write to log file: {SanitizeForSingleLine(ex.Message)}");
        }
    }

    private static string SanitizeForSingleLine(string value)
    {
        return new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transactionService.TransactionAdded -= OnTransactionAdded;
            _disposed = true;
        }
    }
}
