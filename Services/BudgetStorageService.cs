using System.Text.Json;
using BudgetTracker.Models;

namespace BudgetTracker.Services;

/// <summary>
/// Handles persistence of the budget settings in data/budget.json.
/// </summary>
public class BudgetStorageService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public BudgetStorageService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "budget.json");
    }

    public bool Exists() => File.Exists(_filePath);

    public Budget? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            string json = File.ReadAllText(_filePath);
            Budget? budget = JsonSerializer.Deserialize<Budget>(json, _jsonOptions);
            if (budget is null || budget.TotalAmount <= 0 ||
                budget.WarningThresholdPercent <= 0 || budget.WarningThresholdPercent >= 100)
            {
                throw new InvalidDataException("The saved budget settings are invalid.");
            }

            return budget;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The budget file contains invalid JSON.", ex);
        }
    }

    public void Save(Budget budget)
    {
        if (budget.TotalAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(budget), "Budget must be greater than zero.");
        if (budget.WarningThresholdPercent <= 0 || budget.WarningThresholdPercent >= 100)
            throw new ArgumentOutOfRangeException(nameof(budget), "Warning threshold must be between 0 and 100 percent.");

        string json = JsonSerializer.Serialize(budget, _jsonOptions);
        string tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
