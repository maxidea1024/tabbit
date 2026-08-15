using Tabbit.Validation;

// A pre rule reading the data. It runs before a sheet is opened, so there is no accessor to
// read - the name does not resolve, and the report says so in those terms rather than leaving
// an author with `Tables does not exist in the current context`.

internal static class EarlyRules
{
    public static void Validate(IPreContext context)
    {
        context.Info($"This should never be reached. {Tables.Item.Records.Count}");
    }
}
