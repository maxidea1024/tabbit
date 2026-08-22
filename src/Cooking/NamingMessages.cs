using Tabbit.Messages;

namespace Tabbit.Cooking;

/// <summary>
/// The reports the naming checks write, and the phrases they are built from.
/// </summary>
/// <remarks>
/// **The one family in this tool whose sentences are composed rather than written.** A naming
/// report names the kind of thing, what is wrong with it, what follows from that, and how many
/// cells the fix touches - and each of those has two to four forms. Written out as whole
/// sentences the spelling-conflict report alone would be sixteen entries differing by a noun.
///
/// So the sentence is one entry and the parts are entries of their own, filled in as nested
/// messages. Each part is still a phrase somebody can translate on sight - "One field name",
/// "3 places" - rather than a fragment whose place in the sentence they have to infer. What
/// this costs is that a translator has to keep the parts fitting the whole; what it buys is
/// that they only do it once per part.
/// </remarks>
[TabbitMessages("cook")]
public static class NamingMessages
{
    /// <summary>A name spelled other than the way the recipe declares for its kind.</summary>
    public const string SpellingViolation = "cook.naming-spelling-violation";

    /// <summary>As <see cref="SpellingViolation"/>, where one level of a nested name is at fault.</summary>
    public const string SpellingViolationInLevel = "cook.naming-spelling-violation-in-level";

    /// <summary>A name holding two or more underscores in a row.</summary>
    public const string ConsecutiveUnderscores = "cook.naming-consecutive-underscores";

    /// <summary>As <see cref="ConsecutiveUnderscores"/>, in one level of a nested name.</summary>
    public const string ConsecutiveUnderscoresInLevel = "cook.naming-consecutive-underscores-in-level";

    /// <summary>One name the sheets write more than one way.</summary>
    public const string SpellingConflict = "cook.naming-spelling-conflict";

    /// <summary>What a name is, when it stands alone.</summary>
    public const string SaidAlone = "cook.naming-said-alone";

    /// <summary>What a name is, when it belongs to something.</summary>
    public const string SaidOfOwner = "cook.naming-said-of-owner";

    /// <summary>The subject of a conflict report: an entity name.</summary>
    public const string SubjectEntity = "cook.naming-subject-entity";

    /// <summary>The subject of a conflict report: a field name.</summary>
    public const string SubjectField = "cook.naming-subject-field";

    /// <summary>The subject of a conflict report: an enum label.</summary>
    public const string SubjectLabel = "cook.naming-subject-label";

    /// <summary>The subject of a conflict report: a constant name.</summary>
    public const string SubjectConstant = "cook.naming-subject-constant";

    /// <summary>What follows when the spellings reach the generated code separately.</summary>
    public const string ConsequenceSplits = "cook.naming-consequence-splits";

    /// <summary>What follows when they do not.</summary>
    public const string ConsequenceSame = "cook.naming-consequence-same";

    /// <summary>How much work the fix is, for one cell.</summary>
    public const string PlacesOne = "cook.naming-places-one";

    /// <summary>How much work the fix is, for more than one.</summary>
    public const string PlacesMany = "cook.naming-places-many";

    /// <summary>An asset column whose value names no file in the configured folders.</summary>
    public const string AssetFileMissing = "cook.asset-file-missing";

    /// <summary>As <see cref="AssetFileMissing"/>, where the column declares a kind.</summary>
    public const string AssetFileMissingForKind = "cook.asset-file-missing-for-kind";

    /// <summary>A known-problem entry whose count no longer matches what was found.</summary>
    public const string KnownProblemCountGrew = "cook.known-problem-count-grew";

    /// <summary>As <see cref="KnownProblemCountGrew"/>, where fewer were found.</summary>
    public const string KnownProblemCountShrank = "cook.known-problem-count-shrank";

    /// <summary>A report a recipe has written down, with the reason beside it.</summary>
    public const string KnownProblemNoted = "cook.known-problem-noted";

    /// <summary>A known-problem entry missing its place or its reason.</summary>
    public const string KnownProblemEntryIncomplete = "cook.known-problem-entry-incomplete";

    /// <summary>A known-problem entry that matched nothing.</summary>
    public const string KnownProblemMatchedNothing = "cook.known-problem-matched-nothing";
}
