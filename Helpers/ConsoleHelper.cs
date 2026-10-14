using System.Globalization;

namespace BudgetTracker.Helpers;

/// <summary>
/// Reusable console input helpers keep validation code out of Program.cs.
/// </summary>
public static class ConsoleHelper
{
    public static string ReadRequiredLine()
    {
        string? input = Console.ReadLine();
        return input ?? throw new EndOfStreamException("Input stream ended.");
    }

    public static decimal ReadPositiveDecimal(string prompt, decimal maxValue = decimal.MaxValue)
    {
        while (true)
        {
            Console.Write(prompt);
            string input = ReadRequiredLine();
            if (decimal.TryParse(input.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value) &&
                value > 0 && value <= maxValue)
            {
                return value;
            }
            Console.WriteLine("Invalid amount. Please enter a positive number.");
        }
    }
}
