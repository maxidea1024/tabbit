using System;

namespace Tabbit.Validation;

/// <summary>
/// Which tier a rule belongs to, and therefore what it runs before.
/// </summary>
/// <remarks>
/// **This is a barrier rather than an ordering.** Rule files are already collected in a settled
/// order - by name - so putting one before another was never the missing part. What was missing is
/// "if this one failed, do not run the rest": a rule that checks an invariant everything else
/// assumes, failing, and fifty rules after it reporting the consequences rather than the cause.
///
/// Rules sharing a value are one tier and run as they always did. A tier that reported an error is
/// where the stage stops, and what did not run is counted in the report - a gate that is skipped
/// has to say so, or a run that skipped it reads exactly like a run that passed it.
///
/// Lower runs first. A rule with no attribute is in the default tier, so a folder that says nothing
/// behaves as it did before: one tier, and no barrier in it.
///
/// **Sequential stages only** - `pre`, `global` and `runtime`. Table rules run at the same time as
/// each other and each is about one table, so there is no order among them to speak of; the
/// attribute on one is refused rather than ignored, because ignoring it silently would leave the
/// author believing it took.
///
/// spec/rule-priority.md.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RulePriorityAttribute : Attribute
{
    /// <summary>The tier this rule is in. Lower runs first.</summary>
    public int Tier { get; }

    /// <param name="tier">Lower runs first. Rules sharing a value are one tier.</param>
    public RulePriorityAttribute(int tier) => Tier = tier;
}
