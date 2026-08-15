using Tabbit.Rules;
using Tabbit.Validation;

// Rules for the `ItemCategory` table. The file name is what binds them to it, so a
// renamed table wants this file renamed too - the run refuses a name no table has,
// rather than letting the checks stop happening quietly.
//
// This one reads through `context.Tables` rather than the static `Tables`, which is the
// other way of reaching the same data - the instance this run loaded rather than the
// global one. The sibling rules use the static form, so the two are exercised together.

internal static class ItemCategoryRules
{
    public static void Validate(ITableContext context)
    {
        int rows = 0;

        foreach (var row in context.Tables.ItemCategory.Records)
        {
            rows++;

            // if (row.Something < 0)
            //     context.Error(row, nameof(row.Something), "Something cannot be negative.");
        }

        // The two roots must answer with the same data. Reported rather than asserted so the
        // check names the cell-free rule it came from if it ever stops holding.
        if (rows != Tables.ItemCategory.Records.Count)
            context.Error("`context.Tables` and the static `Tables` disagree on row count.");

        context.Info($"ItemCategory rules read {rows} row(s) through context.Tables.");
    }
}
