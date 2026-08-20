using Tabbit.Extensions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// How a name written in a sheet becomes an identifier in each language's spelling.
///
/// The cases that matter are the ones with an acronym in them, because that is where
/// splitting on every capital and splitting on words disagree - and the disagreement is
/// invisible in Pascal case, which is where it went unnoticed. `SFXCategoryType` comes
/// back out of Pascal casing as itself whichever rule is used; it is snake and kebab that
/// show `s_f_x_category_type`.
/// </summary>
public class NameCaseTests
{
    [Theory]
    // The bug this fixes: an acronym at the front, then ordinary words.
    [InlineData("SFXCategoryType", "sfx_category_type")]
    [InlineData("HTTPServer", "http_server")]
    // Acronyms as the author wrote them in a real sheet.
    [InlineData("ATK_Growth", "atk_growth")]
    [InlineData("Name_KR", "name_kr")]
    [InlineData("AccumulatedEXP", "accumulated_exp")]
    [InlineData("CRIT_DMG", "crit_dmg")]
    // A whole name that is one acronym.
    [InlineData("HP", "hp")]
    [InlineData("ID", "id")]
    // Ordinary names, which must not move.
    [InlineData("ItemTable", "item_table")]
    [InlineData("Index", "index")]
    [InlineData("Item1Bonus", "item1_bonus")]
    [InlineData("already_snake", "already_snake")]
    public void Snake_case_keeps_a_run_of_capitals_together(string source, string expected)
    {
        Assert.Equal(expected, source.ToSnakeCase());
    }

    [Theory]
    [InlineData("SFXCategoryType", "sfx-category-type")]
    [InlineData("Name_KR", "name-kr")]
    [InlineData("ItemTable", "item-table")]
    public void Kebab_case_splits_the_same_way(string source, string expected)
    {
        Assert.Equal(expected, source.ToKebabCase());
    }

    [Theory]
    // Pascal casing is where the old rule hid: both rules give this answer.
    [InlineData("SFXCategoryType", "SFXCategoryType")]
    [InlineData("Name_KR", "NameKR")]
    [InlineData("HP", "HP")]
    [InlineData("item_table", "ItemTable")]
    [InlineData("fire_ball", "FireBall")]
    // Leading and trailing underscores are kept: a name is allowed to say `_reserved`.
    [InlineData("_private", "_Private")]
    public void Pascal_case_is_unchanged(string source, string expected)
    {
        Assert.Equal(expected, source.ToPascalCase());
    }

    [Theory]
    [InlineData("item_table", "itemTable")]
    [InlineData("ItemTable", "itemTable")]
    public void Camel_case_lowers_the_first_word(string source, string expected)
    {
        Assert.Equal(expected, source.ToCamelCase());
    }

    [Theory]
    [InlineData("SFXCategoryType", "SFX_CATEGORY_TYPE")]
    [InlineData("HTTPServer", "HTTP_SERVER")]
    [InlineData("Name_KR", "NAME_KR")]
    [InlineData("ItemTable", "ITEM_TABLE")]
    [InlineData("HP", "HP")]
    [InlineData("already_snake", "ALREADY_SNAKE")]
    [InlineData("_private", "_PRIVATE")]
    public void Upper_snake_case_splits_like_snake_and_raises_every_letter(
        string source, string expected)
    {
        Assert.Equal(expected, source.ToUpperSnakeCase());
    }

    /// <summary>
    /// The form the generators used to build by hand gives the same answer, name for name.
    /// </summary>
    /// <remarks>
    /// The reason for having the form at all is that `ToSnakeCase().ToUpperInvariant()`
    /// cannot judge: deciding whether a name already follows a convention means spelling it
    /// and comparing, so the spelling and the judging have to be one function or the two
    /// drift apart on the first acronym. This pins the equivalence so moving the generators
    /// onto it is not a change to what they emit - and so that a future edit to the word
    /// splitting cannot quietly move one and not the other.
    /// </remarks>
    [Theory]
    [InlineData("SFXCategoryType")]
    [InlineData("HTTPServer")]
    [InlineData("ATK_Growth")]
    [InlineData("Name_KR")]
    [InlineData("AccumulatedEXP")]
    [InlineData("CRIT_DMG")]
    [InlineData("HP")]
    [InlineData("ID")]
    [InlineData("ItemTable")]
    [InlineData("Item1Bonus")]
    [InlineData("already_snake")]
    [InlineData("_private")]
    [InlineData("trailing_")]
    [InlineData("Tables")]
    [InlineData("Balance")]
    public void Upper_snake_case_agrees_with_the_hand_built_form(string source)
    {
        Assert.Equal(source.ToSnakeCase()!.ToUpperInvariant(), source.ToUpperSnakeCase());
    }
}
