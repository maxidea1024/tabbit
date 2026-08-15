using Tabbit.Rules;
using Tabbit.Validation;

// A table rule reaching for a store, which is refused. The folder is what makes
// `--skip-runtime-validation` mean anything: a rule holding a connection from outside
// `rules/runtime/` fails on a machine with no access however much was skipped.
//
// The cast is what makes this a test of the guard rather than of the compiler. Written plainly,
// `context.Db(...)` does not compile at all - `ITableContext` has no such method, which is the
// first line of defence and the one an author meets while typing. This is the second: the host
// object does implement the runtime contract, so a rule determined enough to cast to it reaches a
// method that refuses by stage.

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        var live = ((IRuntimeContext)context).Db("Live").Set<int>("SELECT id FROM live_products");

        context.Info($"This should never be reached. {live.Count}");
    }
}
