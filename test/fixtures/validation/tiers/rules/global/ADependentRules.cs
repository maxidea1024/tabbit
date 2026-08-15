using Tabbit.Rules;
using Tabbit.Validation;

// The rule that must not run. It sorts first by name, so if the tiers were not honoured this is
// what would report - which is what makes the pair a test of the tiers rather than of the order
// they were already collected in.

[RulePriority(10)]
internal static class ADependentRules
{
    public static void Validate(IGlobalContext context)
    {
        context.Error("DEPENDENT-RAN: this rule should not have run.");
    }
}
