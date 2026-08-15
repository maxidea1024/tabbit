using Tabbit.Rules;
using Tabbit.Validation;

// A runtime rule: it reads a store outside the sheets, which is what this folder is for and
// what `--skip-runtime-validation` can leave out.
//
// The query goes through the read-only gateway, so the rule says what it wants rather than how
// to connect. A store that cannot answer fails the validation - an unanswered check is not a
// passed one.

internal static class LiveRules
{
    public static void Validate(IRuntimeContext context)
    {
        var live = context.Db("Live").Set<int>("SELECT id FROM live_products");

        context.Info($"Compared against {live.Count} live product(s).");

        foreach (var row in Tables.Item.Records)
        {
            if (!live.Contains(row.Index))
                context.Warn(row, nameof(row.Index), "This item is not listed in the live product table.");
        }

        // Redis is read the same way, by the name the recipe gave it.
        context.Info($"The cache {(context.Redis("Cache").Exists("tabbit:absent") ? "holds" : "does not hold")} the probe key.");
    }
}
