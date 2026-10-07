using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetTracker.Models;

namespace BudgetTracker.Services;

/// <summary>
/// Handles all JSON file I/O for transactions. Each day's transactions are stored
/// in data/yyyy-MM-dd.json as a JSON array.
/// </summary>
public class StorageService
{
    public const int MaxTransactionsPerDay = 10_000;
    public const long MaxDataFileBytes = 5 * 1024 * 1024;
    public const int MaxDescriptionLength = 500;
    public const decimal MaxTransactionAmount = 999_999_999_99m;

    private readonly string _dataDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public StorageService(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        Directory.CreateDirectory(_dataDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
        };
    }

    public string DataDirectory => _dataDirectory;

    public void SaveTransaction(Transaction transaction)
    {
        ValidateTransaction(transaction);

        string fileName = $"{transaction.Timestamp:yyyy-MM-dd}.json";
        string filePath = Path.Combine(_dataDirectory, fileName);
        string lockPath = filePath + ".lock";

        using FileStream lockStream = AcquireFileLock(lockPath);
        List<Transaction> transactions = LoadDayFile(filePath);

        if (transactions.Count >= MaxTransactionsPerDay)
        {
            throw new InvalidOperationException("The daily transaction limit has been reached.");
        }

        transactions.Add(transaction);
        WriteAtomically(filePath, transactions);
    }

    private List<Transaction> LoadDayFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<Transaction>();
        }

        FileInfo info = new(filePath);
        if (info.Length > MaxDataFileBytes)
        {
            throw new InvalidDataException($"The file '{filePath}' exceeds the maximum allowed size.");
        }

        string json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<Transaction>();
        }

        try
        {
            List<Transaction>? list = JsonSerializer.Deserialize<List<Transaction>>(json, _jsonOptions);
            list ??= new List<Transaction>();

            if (list.Count > MaxTransactionsPerDay)
            {
                throw new InvalidDataException($"The file '{filePath}' contains too many transactions.");
            }

            DateOnly fileDate = GetFileDate(filePath);
            foreach (Transaction transaction in list)
            {
                ValidateTransaction(transaction);
                if (DateOnly.FromDateTime(transaction.Timestamp.DateTime) != fileDate)
                {
                    throw new InvalidDataException(
                        $"Transaction {transaction.Id} has a timestamp outside the file's date.");
                }
            }

            return list;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The file '{filePath}' contains invalid JSON and could not be read.", ex);
        }
    }

    private static DateOnly GetFileDate(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly date))
        {
            throw new InvalidDataException($"Invalid data file name: '{Path.GetFileName(filePath)}'.");
        }

        return date;
    }

    private static void ValidateTransaction(Transaction transaction)
    {
        if (transaction.Id == Guid.Empty)
        {
            throw new InvalidDataException("Transaction Id must be a non-empty GUID.");
        }

        if (transaction.Type is not TransactionType.Income and not TransactionType.Expense)
        {
            throw new InvalidDataException($"Transaction {transaction.Id} has an invalid type.");
        }

        if (string.IsNullOrWhiteSpace(transaction.Description))
        {
            throw new InvalidDataException($"Transaction {transaction.Id} has an empty description.");
        }

        if (transaction.Description.Length > MaxDescriptionLength)
        {
            throw new InvalidDataException($"Transaction {transaction.Id} has an oversized description.");
        }

        if (ContainsControlCharacters(transaction.Description))
        {
            throw new InvalidDataException($"Transaction {transaction.Id} contains control characters.");
        }

        if (transaction.Amount <= 0 || transaction.Amount > MaxTransactionAmount)
        {
            throw new InvalidDataException($"Transaction {transaction.Id} has an invalid amount.");
        }
    }

    private static bool ContainsControlCharacters(string value)
    {
        return value.Any(char.IsControl);
    }

    private static FileStream AcquireFileLock(string lockPath)
    {
        const int maxAttempts = 50;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(50);
            }
        }

        throw new IOException($"Could not acquire the data file lock '{lockPath}'.");
    }

    private void WriteAtomically(string filePath, List<Transaction> transactions)
    {
        string json = JsonSerializer.Serialize(transactions, _jsonOptions);
        string tempPath = filePath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public IEnumerable<Transaction> LoadTransactionsForDateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException("End date must be on or after start date.");
        }

        List<Transaction> all = new();
        foreach (string file in Directory.GetFiles(_dataDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly day) && day >= start && day <= end)
            {
                all.AddRange(LoadDayFile(file));
            }
        }

        return all;
    }

    public IEnumerable<Transaction> LoadAllTransactions()
    {
        List<Transaction> all = new();
        foreach (string file in Directory.GetFiles(_dataDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                all.AddRange(LoadDayFile(file));
            }
        }

        return all;
    }

    public bool RemoveTransaction(Guid id)
    {
        bool removed = false;

        foreach (string file in Directory.GetFiles(_dataDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                continue;
            }

            string lockPath = file + ".lock";
            using FileStream lockStream = AcquireFileLock(lockPath);
            List<Transaction> transactions = LoadDayFile(file);
            int countBefore = transactions.Count;
            transactions.RemoveAll(t => t.Id == id);

            if (transactions.Count < countBefore)
            {
                removed = true;
                WriteAtomically(file, transactions);
            }
        }

        return removed;
    }
}
