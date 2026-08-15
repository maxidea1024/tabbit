using Tabbit.Rules;
using Tabbit.Validation;

// A table rule: the file name is the table it is about, and a name no table has is refused
// rather than a rule that quietly stops running.
//
// `Tables` is the accessor generated from these sheets - the same type a consuming project's
// own code uses - so `Kind` is the generated `Keyword` enum here and a misspelling is a
// compile error rather than a rule that passes.

internal static class PackageRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"Package rules ran over {context.Tables.Package.Records.Count} row(s).");

        foreach (var row in context.Tables.Package.Records)
        {
            if (!Naming.IsFilled(row.Label))
                context.Error(row, nameof(row.Label), "Every keyword needs a label to show.");

            // Compared against a label rather than a number, because the column arrives as the
            // generated enum. `None` is the one this tool inserts for the unset case, so a row
            // carrying it says the sheet left the cell empty.
            if (row.Kind == Keyword.None)
                context.Error(row, nameof(row.Kind), "A row should name which keyword it is about.");
        }
    }
}
