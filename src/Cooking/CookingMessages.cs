using Tabbit.Messages;

namespace Tabbit.Cooking;

/// <summary>
/// The reports cooking writes, named.
/// </summary>
/// <remarks>
/// `cook` because that is the step of the run these come from - the same names
/// <see cref="LogCategory"/> uses, so a report and the log lines around it agree about where
/// in a run they happened.
///
/// The names here say what was wrong, not what the sentence says. A message can be reworded
/// without the id moving, which is the point of having ids at all.
/// </remarks>
[TabbitMessages("cook")]
public static class CookingMessages
{
    /// <summary>A role's brackets were opened and nothing was named in them.</summary>
    public const string RoleGroupEmpty = "cook.role-group-empty";

    /// <summary>A second name in the brackets of a role that takes one name.</summary>
    public const string RoleSpaceNotText = "cook.role-space-not-text";

    /// <summary>A trailing comma in a role's brackets with no namespace after it.</summary>
    public const string RoleSpaceEmpty = "cook.role-space-empty";
}
