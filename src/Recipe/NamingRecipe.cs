using System.Collections.Generic;

namespace Tabbit.Recipe;

/// <summary>
/// Which spelling the names in the sheets have to follow, and what to do about the ones
/// that do not.
/// </summary>
/// <remarks>
/// The core holds the checking and the recipe holds the rule. What counts as the right
/// spelling differs per project and cannot be derived from anything the tool can see, so
/// nothing here has a default that asserts one: a kind nobody declares is not checked.
///
/// What does run without being asked for is the pair of checks that need no rule to be
/// meaningful - two spellings of one name, and the underscore runs no output can show.
/// Those need no declaration because they are not about which convention is right. They
/// are about the same name being written more than one way, which is a mistake under
/// every convention.
///
/// Names are global to the model rather than to a source, which is why this is here and
/// not on a source entry: the same name is written in several workbooks by several
/// people, and that is exactly the case worth reporting.
///
/// spec/targets/naming-conventions.md.
/// </remarks>
public class NamingRecipe
{
    /// <summary>
    /// Spelling required of table, enum and constant-set names. Blank leaves them
    /// unchecked.
    /// </summary>
    /// <remarks>
    /// Takes `pascal`, `camel`, `snake` or `upper-snake`. One setting for the three
    /// because they share a namespace in the generated code and in a reader's head; a
    /// project that spells its tables and its enums differently is describing a
    /// distinction the generated code does not have.
    /// </remarks>
    public string Entity { get; set; } = "";

    /// <summary>
    /// Spelling required of field names. Blank leaves them unchecked.
    /// </summary>
    /// <remarks>
    /// A nested name is judged one level at a time, so `Slot1.Id` is two answers rather
    /// than one, and the `*` that marks a secondary index is not part of the name being
    /// judged.
    /// </remarks>
    public string Field { get; set; } = "";

    /// <summary>Spelling required of enum labels. Blank leaves them unchecked.</summary>
    public string Label { get; set; } = "";

    /// <summary>Spelling required of constant names. Blank leaves them unchecked.</summary>
    /// <remarks>
    /// Separate from <see cref="Label"/> because `upper-snake` is a common answer for one
    /// and an unusual one for the other.
    /// </remarks>
    public string Constant { get; set; } = "";

    /// <summary>
    /// What to do about a name that does not follow the spelling declared for its kind:
    /// `error` or `warn`.
    /// </summary>
    /// <remarks>
    /// `error` by default, because declaring a convention is the act of asking for it to
    /// hold. A project that wants to see the scale of the gap before it holds anyone to it
    /// sets `warn` for a while - or keeps `error` and lists what it has today under
    /// <see cref="Exempt"/>, which is the same thing except that it also stops new ones.
    ///
    /// There is no `ignore`: that is what leaving the kind blank means.
    /// </remarks>
    public string OnViolation { get; set; } = "error";

    /// <summary>
    /// What to do when one name is written more than one way across the model: `warn`,
    /// `error` or `ignore`.
    /// </summary>
    /// <remarks>
    /// `warn` by default, and it runs whether or not a convention is declared. A
    /// conversion that used to succeed still succeeds, which is why this is not `error`;
    /// it is not `ignore` because in a language without declarations, reading a field by
    /// the wrong one of two spellings is not an error but an absent value, and the symptom
    /// is a feature that quietly does nothing.
    ///
    /// What this sets is the weight of a conflict whose spellings reach the generated code
    /// as different names, which is the one that costs a consumer something. A conflict
    /// whose spellings all normalize to one name is reported a level lower - the sheets
    /// still disagree, and the next spelling of that name may not be so harmless, but
    /// nothing downstream carries the difference and no build should stop for it.
    /// </remarks>
    public string OnSpellingConflict { get; set; } = "warn";

    /// <summary>
    /// What to do about a name with two or more underscores in a row: `warn`, `error` or
    /// `ignore`.
    /// </summary>
    /// <remarks>
    /// The case rules use an interior underscore as a word boundary and keep no count of
    /// them, so `a_b`, `a__b` and `a___b` all reach the generated code as one name. The
    /// difference is a distinction the sheet draws and nothing downstream can carry, which
    /// makes it either a typo or an intent that was never delivered.
    ///
    /// Leading and trailing underscores are not this: they survive into the generated code,
    /// so `_name` and `__name` are two names rather than two spellings of one.
    /// </remarks>
    public string OnConsecutiveUnderscores { get; set; } = "warn";

    /// <summary>
    /// Names to leave out of every check here, spelled exactly as the sheet spells them.
    /// </summary>
    /// <remarks>
    /// How a model with a history adopts a convention. Declaring one on a project that has
    /// been written for years reports what is already there, and there can be hundreds of
    /// them: made errors, the convention cannot be adopted at all, and made warnings, a new
    /// violation arrives among the old ones and is never seen.
    ///
    /// So the existing ones go in here and the setting goes to `error`. From that moment a
    /// new name has to follow the convention, which is the half of the problem worth
    /// stopping first, and the listed ones are renamed a family at a time and struck off as
    /// they go.
    ///
    /// The list is meant to shrink. Adding a name to it is admitting a new violation, and
    /// nothing in the tool can tell that apart from recording an old one - which is why it
    /// is a plain list a reviewer can read, and not a pattern.
    /// </remarks>
    public List<string> Exempt { get; set; } = new List<string>();
}
