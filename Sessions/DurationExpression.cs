using System;
using System.Globalization;

namespace ErgTrainer.Sessions;

// Seconds or a product of whole numbers, for example 60*5.
public static class DurationExpression
{
    public static bool TryParse(string? text, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        long product = 1;
        foreach (string part in text.Split('*'))
        {
            if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int factor) ||
                factor < 1 || factor > 86400 || product > 86400 / factor)
                return false;
            product *= factor;
        }
        seconds = (int)product;
        return true;
    }
}
