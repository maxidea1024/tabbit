using Tabbit.Rules;
using Tabbit.Validation;

// A global rule: for what a table rule cannot ask, because the question is not about one
// table. `Schema` enumerates, which is what a convention needs - a typed property exists only
// for a name somebody wrote, so "every table" cannot be asked of `Tables` at all.

internal static class ConventionsRules
{
    public static void Validate(IGlobalContext context)
    {
        context.Info(
            $"Global rules ran over {context.Schema.Tables.Count} table(s) and "
            + $"{context.Schema.Tables.Sum(table => table.Fields.Count)} column(s).");

        foreach (var table in context.Schema.Tables)
        {
            // Not required, and worth noticing: a table nobody described is a table whose
            // columns are the only documentation of it.
            if (string.IsNullOrWhiteSpace(table.Comment))
                context.Warn(table, $"Table `{table.Name}` has no description.");
        }

        // A cross-table invariant, which is the other half of what this folder is for: an empty
        // table is not an error in itself, but it is never what somebody meant.
        var withoutRows = context.Schema.Tables.Where(table => table.RowCount == 0).ToList();

        if (withoutRows.Count > 0)
            context.Warn($"{withoutRows.Count} table(s) have no rows: {string.Join(", ", withoutRows)}.");
    }
}
