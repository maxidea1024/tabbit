using Tabbit.Rules;
using Tabbit.Validation;

// A rule that reports against a row, so the suite can check both halves of what an error
// costs: the run stops with no output written, and the report points at the cell the value
// came from rather than at the rule that objected.

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        foreach (var row in Tables.Item.Records)
            context.Error(row, nameof(row.Price), "This fixture rule always fails, which is what it is for.");
    }
}
