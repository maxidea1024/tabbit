using Tabbit.Rules;
using Tabbit.Validation;

// A rule that reports far more than anyone can read, which is what the cap is for. One wrong
// rule over a large table is tens of thousands of identical lines, and past the first hundred
// nothing is learned.

internal static class LocalizationRules
{
    public static void Validate(ITableContext context)
    {
        for (int at = 0; at < 150; at++)
            context.Error($"Report {at}, of which most should be counted rather than printed.");
    }
}
