using Tabbit.Rules;
using Tabbit.Validation;

// The rule the others assume. It sorts last by name and runs first anyway, because its tier is
// lower - and because it reports, nothing after it runs at all.

[RulePriority(-10)]
internal static class ZFoundationRules
{
    public static void Validate(IGlobalContext context)
    {
        context.Error("FOUNDATION-FAILED: the invariant every later rule assumes does not hold.");
    }
}
