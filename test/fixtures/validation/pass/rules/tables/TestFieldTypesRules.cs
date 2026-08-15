using Tabbit.Rules;
using Tabbit.Validation;

// A rule reading things that are not in the sheets: a folder of files, and a JSON document
// beside them. The core does not know what a `.png` is or what `policy.json` means - the paths
// come from the recipe's own options, and this file decides what to do with them.

internal static class TestFieldTypesRules
{
    public static void Validate(ITableContext context)
    {
        var icons = context.Files(context.Option("AssetRoot"), "*.png");
        var banned = context.Json(context.Option("AssetRoot") + "/policy.json")["banned"].Select(name => (string)name).ToHashSet();

        context.Info($"Scanned {icons.Count} icon(s) and {banned.Count} banned name(s).");

        if (!icons.Has("Sword_Icon"))
            context.Error("The fixture's own icon is missing, so the file map is not being read.");

        foreach (string name in banned)
        {
            if (icons.Has(name))
                context.Error($"`{name}` is on the banned list but is in the asset folder.");
        }
    }
}
