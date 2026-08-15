using Tabbit.Rules;
using Tabbit.Validation;

// A global rule: for what a table rule cannot ask, because the question is not about one
// table. `Schema` enumerates, which is what a convention needs - a typed property only exists
// for a name somebody wrote, so "every table" cannot be asked of `Tables` at all.

internal static class ConventionsRules
{
    public static void Validate(IGlobalContext context)
    {
        context.Info($"Global rules ran over {context.Schema.Tables.Count} table(s).");

        foreach (var table in context.Schema.Tables)
        {
            // A description is not required, but a table without one is worth noticing.
            if (string.IsNullOrWhiteSpace(table.Comment))
                context.Warn(table, $"Table `{table.Name}` has no description.");

            foreach (var field in table.Fields)
            {
                // A naming convention: a column whose name says it points at a table has to.
                if (field.Name.EndsWith("Id") && !field.IsIndex && field.References is null)
                    context.Info(field, $"`{field}` is named like a reference but is a plain {field.TypeName}.");
            }
        }

        // A cross-table invariant, which is the other half of what this folder is for.
        var withoutRows = context.Schema.Tables.Where(table => table.RowCount == 0).ToList();

        if (withoutRows.Count > 0)
            context.Warn($"{withoutRows.Count} table(s) have no rows: {string.Join(", ", withoutRows)}.");
    }
}
