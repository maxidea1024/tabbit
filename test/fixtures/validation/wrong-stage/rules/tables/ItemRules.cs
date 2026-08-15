using Tabbit.Rules;
using Tabbit.Validation;

// A table rule reaching for a store. `ITableContext` has no `Db`, so this does not compile -
// and the report says which folder does have it.

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        var live = context.Db("Live").Set<int>("SELECT id FROM live_products");

        context.Info($"This should never be reached. {live.Count}");
    }
}
