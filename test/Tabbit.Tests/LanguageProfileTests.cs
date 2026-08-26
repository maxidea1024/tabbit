using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tabbit.CodeGeneration;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Every output language against every type a sheet can hold.
///
/// The the generators each carry their own switch over <see cref="ValueType"/>, and
/// each ends in a `default:` that throws. That is the right thing at the moment a
/// conversion asks for a type the generator cannot render - but it means adding a type to
/// the enum leaves thirteen places that compile perfectly and fail only when somebody's
/// sheet uses it, one language at a time, in whatever order they are unlucky in.
///
/// So the question is asked here once, of the table the generators read. A new
/// <see cref="ValueType"/> fails this the moment it is added, naming the languages that
/// have not been taught it, rather than reaching a user first.
///
/// The profiles are found by reflection rather than listed, because a list is the other
/// thing somebody forgets: a new language whose profile nothing here mentions
/// would be exactly as unchecked as the types are now.
/// </summary>
public class LanguageProfileTests
{
    /// <summary>
    /// Every profile <see cref="LanguageProfile"/> declares.
    /// </summary>
    private static IReadOnlyList<LanguageProfile> Profiles()
        => typeof(LanguageProfile)
           .GetFields(BindingFlags.Public | BindingFlags.Static)
           .Where(field => field.FieldType == typeof(LanguageProfile))
           .Select(field => (LanguageProfile)field.GetValue(null))
           .ToList();

    /// <summary>
    /// The scalar types a generator has to be able to name.
    ///
    /// `None` and `Unresolved` are not types a field ends up holding - one is unset and
    /// the other is a reference before resolution ran. `Enum` and `ForeignRecord` are
    /// deliberately absent from the profiles: both name something declared in the sheets,
    /// and each language qualifies that its own way, so those two arms stay in the
    /// generators.
    ///
    /// `Bitset` is absent for a third reason: it does not reach a generator at all. It is a
    /// type for exactly as long as parsing lasts - what makes it one is the notation it
    /// accepts - and the cooker folds it to a 64-bit integer once every cell has been read,
    /// so a profile entry for it would be a name nothing ever asks for. spec/types/bitset.md.
    ///
    /// Everything else is here.
    /// </summary>
    private static IEnumerable<ValueType> RenderableScalars()
        => Enum.GetValues<ValueType>()
               .Where(type => type == Tabbit.Models.ValueTypes.ElementOf(type))
               .Where(type => type != ValueType.None
                           && type != ValueType.Unresolved
                           && type != ValueType.Enum
                           && type != ValueType.ForeignRecord
                           && type != ValueType.Bitset)

               // The composites, for the same reason as `Bitset` above: they are types for as
               // long as parsing lasts and the cooker folds them away, so no generator ever
               // sees one. A language that named `vec3f` would be naming a type that cannot
               // reach it - and the fold, not a per-language entry, is what makes that true.
               // spec/types/composite-value-types.md.
               .Where(type => !Tabbit.Models.CompositeTypes.IsComposite(type));

    [Fact]
    public void The_profiles_are_all_found()
    {
        var ids = Profiles().Select(profile => profile.Id).OrderBy(id => id, StringComparer.Ordinal);

        // Named rather than counted, so adding a language is a deliberate edit here and
        // removing one cannot pass by accident.
        Assert.Equal(
            new[]
            {
                "c", "cpp", "csharp", "dart", "go", "java", "kotlin", "lua", "php",
                "python", "ruby", "rust", "swift", "typescript", "unreal",
            },
            ids);
    }

    /// <summary>
    /// Every language can name every scalar type.
    /// </summary>
    [Fact]
    public void Every_language_can_name_every_scalar_type()
    {
        var missing = new List<string>();

        foreach (var profile in Profiles())
        {
            foreach (var type in RenderableScalars())
            {
                if (!profile.ScalarTypes.ContainsKey(type))
                    missing.Add($"  {profile.Id} has no name for {type}");
            }
        }

        Assert.True(missing.Count == 0,
            $"A type was added to ValueType and not to every language:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// And every language with a read-call table has a call for every scalar type.
    /// </summary>
    /// <remarks>
    /// This was a copy of the same switch, one per generator, each ending in a `default:`
    /// that throws. Adding a value type meant ten edits, and forgetting one still compiled -
    /// it surfaced at runtime in whoever's project reached that field first.
    ///
    /// Now it is ten entries in LanguageProfile.cs, and this fails naming the language and
    /// the type - in the same file and the same run as the test above, which already asks
    /// for the type's name. One place to add a type instead of eleven.
    ///
    /// Three profiles have no table at all: the C++, C# and Unreal readers resolve a read by
    /// overload, one method per type rather than a method per name, so there is no per-type
    /// call to record. Skipped by asking whether the table exists rather than by naming them,
    /// so a fourth reader written that way needs nothing here.
    /// </remarks>
    [Fact]
    public void Every_language_with_a_read_table_can_read_every_scalar_type()
    {
        var missing = new List<string>();
        int tables = 0;

        foreach (var profile in Profiles())
        {
            if (profile.ReadCalls == null)
                continue;

            tables++;

            foreach (var type in RenderableScalars())
            {
                if (!profile.ReadCalls.ContainsKey(type))
                    missing.Add($"  {profile.Id} has no read call for {type}");
            }
        }

        Assert.True(missing.Count == 0,
            $"A type was added to ValueType and not to every language's reads:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing));

        // And the three that resolve by overload really are the only ones without a table.
        Assert.Equal(Profiles().Count - 3, tables);
    }

    /// <summary>
    /// A read call for a type the language cannot read is refused by name, not by returning
    /// something that looks like a call.
    /// </summary>
    [Fact]
    public void A_type_a_language_cannot_read_is_refused_by_name()
    {
        foreach (var profile in Profiles())
        {
            if (profile.ReadCalls == null)
            {
                // Asking a reader that resolves by overload says so, rather than answering.
                var overloaded = Assert.Throws<TabbitDefectException>(
                    () => profile.ReadCall(ValueType.Int32));

                Assert.Contains(profile.Id, overloaded.Message);
                Assert.Contains("overload", overloaded.Message);
                continue;
            }

            var thrown = Assert.Throws<TabbitDefectException>(
                () => profile.ReadCall(ValueType.Unresolved));

            Assert.Contains(profile.Id, thrown.Message);
            Assert.Contains("Unresolved", thrown.Message);
        }
    }

    /// <summary>
    /// And every array form resolves to its element, so a generator can name an array by
    /// naming the element and wrapping it.
    /// </summary>
    [Fact]
    public void Every_language_can_name_every_array_type()
    {
        var missing = new List<string>();

        foreach (var profile in Profiles())
        {
            foreach (var element in RenderableScalars())
            {
                var array = Tabbit.Models.ValueTypes.ArrayOf(element);

                if (array == ValueType.None)
                {
                    missing.Add($"  {element} has no array form");
                    continue;
                }

                // ScalarTypeName takes an array as readily as a scalar and answers for
                // its element, which is the contract every generator relies on.
                string named = profile.ScalarTypeName(array);

                Assert.False(string.IsNullOrWhiteSpace(named),
                    $"{profile.Id} named {array} as blank.");

                Assert.Equal(profile.ScalarTypeName(element), named);
            }
        }

        Assert.True(missing.Count == 0,
            $"An array form is missing:{Environment.NewLine}" + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// A type no language should be asked for is refused by name, and the message says
    /// which language refused it.
    ///
    /// `Unresolved` is the one to ask with: a field still holding it means reference
    /// resolution never ran, and a generator that rendered it as something would emit
    /// code around a placeholder.
    /// </summary>
    [Fact]
    public void A_type_a_language_cannot_render_is_refused_by_name()
    {
        foreach (var profile in Profiles())
        {
            // TabbitDefectException, not TabbitException: a type a generator has no case for
            // is a gap only this repository can close, so it is not a report anybody else can
            // act on. What the test is about - refused by name rather than answered with
            // something that looks like a call - is unchanged.
            var ex = Assert.Throws<TabbitDefectException>(
                () => profile.ScalarTypeName(ValueType.Unresolved));

            Assert.Contains(profile.Id, ex.Message);
            Assert.Contains("Unresolved", ex.Message);
        }
    }

    /// <summary>
    /// A profile that escapes nothing has to mean it.
    ///
    /// Four of them carry an empty reserved list - C#, Go, PHP and Unreal - and each has
    /// a reason recorded beside it. What makes those reasons trustworthy is not the
    /// comment: the reserved-words fixture compiles every language's output, so a wrong
    /// one is a failing build. This only pins that the escape format is still usable if
    /// the list ever fills, because a format with no placeholder would silently produce
    /// the same name back.
    /// </summary>
    [Fact]
    public void Every_escape_format_actually_changes_the_name()
    {
        foreach (var profile in Profiles())
        {
            Assert.Contains("{0}", profile.MemberNameEscape);

            string escaped = string.Format(profile.MemberNameEscape, "name");

            Assert.NotEqual("name", escaped);
        }
    }

    /// <summary>
    /// Every reserved name a profile lists is actually escaped, and nothing else is.
    /// </summary>
    [Fact]
    public void Only_the_reserved_names_are_escaped()
    {
        foreach (var profile in Profiles())
        {
            foreach (var reserved in profile.ReservedMemberNames)
                Assert.NotEqual(reserved, profile.MemberName(reserved));

            // A name nothing could reserve, so it must come back untouched.
            Assert.Equal("tabbitOrdinaryName", profile.MemberName("tabbitOrdinaryName"));
        }
    }
}
