using Tabbit.Rules;
using Tabbit.Validation;

// The reason a rule folder belongs in this directory at all.
//
// This table's columns are named `class`, `delete`, `operator`, `namespace`, `constructor`,
// `function` and `int`, and the point of the side-by-side tree is showing how each language
// gets out of the way of its own keywords. A rule file is the fourteenth view of that: what a
// project actually types to read those columns. Every one of them is reached below, and this
// file compiles - which is the claim being made.

internal static class TemplateRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"Template rules ran over {context.Tables.Template.Records.Count} row(s).");

        foreach (var row in context.Tables.Template.Records)
        {
            // Every text column of this table is named after a keyword. Named together here
            // rather than one check each, because the question is the same for all of them.
            var text = new[]
            {
                (nameof(row.Class), row.Class),
                (nameof(row.Operator), row.Operator),
                (nameof(row.Namespace), row.Namespace),
                (nameof(row.Constructor), row.Constructor),
                (nameof(row.Function), row.Function),
            };

            foreach (var (column, value) in text)
            {
                if (!Naming.IsFilled(value))
                    context.Error(row, column, $"`{column}` is part of this row's identity and is empty.");
            }

            // `Int` is an int column called `int`, and `Delete` is a bool called `delete`.
            if (row.Int <= 0)
                context.Error(row, nameof(row.Int), "A weight should be greater than zero.");

            if (row.Delete && row.Int > 0)
                context.Info(row, nameof(row.Delete), "Marked for deletion while still carrying a weight.");
        }

        // The secondary index the sheet declared, by the name it declared it under. Asked here
        // because a rule that looks a row up is the ordinary use of one.
        if (context.Tables.Template.FindByOperator("plus") is null)
            context.Warn("The `plus` row is gone, so anything keyed off it has nothing to find.");
    }
}
