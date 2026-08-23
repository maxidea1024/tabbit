using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What the brackets on a declaration say, once something is listening.
/// </summary>
/// <remarks>
/// **The parser carries every key and reads none of them**, which is the policy
/// `LayoutOptions` runs on and section 6.4 of the design adopts. This is the other half of
/// that policy: the keys this build acts on are read here, and every key that is left is
/// reported - because a map that quietly holds a misspelt key is a map that quietly loses
/// what somebody meant to say.
///
/// **Three answers, not two.** A key can be one this build acts on, one the notation defines
/// and this build does not carry yet, or one nothing defines at all. The middle case is the
/// reason for the split: silently ignoring `(regex="…")` would leave somebody believing a
/// check is running, and calling it a spelling mistake would send them looking for the right
/// spelling of a key they spelled correctly.
///
/// **A declaration's keys are checked once, wherever it is used.** A struct used by three
/// tables has one typo, not three.
/// </remarks>
internal static class SchemaMetadata
{
    /// <summary>Keys a struct declaration may carry, and what this build does with them.</summary>
    private static readonly Dictionary<string, MetaKey> OnStruct = new(StringComparer.Ordinal)
    {
        // The one-cell notation, which widens the composite value types' expansion pass to
        // a declared struct - section 7.3.
        ["sep"] = MetaKey.Carried,
    };

    /// <summary>Keys a member may carry.</summary>
    private static readonly Dictionary<string, MetaKey> OnField = new(StringComparer.Ordinal)
    {
        ["text"] = MetaKey.Carried,
        ["asset"] = MetaKey.Carried,
        ["min"] = MetaKey.Carried,
        ["max"] = MetaKey.Carried,
        ["allowed"] = MetaKey.Carried,

        // A gravestone, which the parser reads and this build acts on - it is what keeps a
        // dropped member's wire tag from being handed to a new one.
        ["removed"] = MetaKey.Carried,

        // Says what `foreign` already says here - this tool resolves a named table rather
        // than only checking against it, so carrying both would be one thing spelled twice.
        ["refs"] = MetaKey.SaidByForeign,

        ["notDefault"] = MetaKey.Carried,
        ["regex"] = MetaKey.Carried,
        ["size"] = MetaKey.Carried,

        // The one of the four that has nowhere to go. Section 6.5 puts uniqueness on
        // the array rather than on the type, because the same struct used somewhere
        // that is not an array has nothing for it to mean - and the array a struct is
        // used as is a group of sheet columns, which has no brackets to write it in.
        ["uniqueBy"] = MetaKey.NotCarried,

        // A consumer's own label. Nothing in this tool has anywhere to put one.
        ["tag"] = MetaKey.NotCarried,
    };

    /// <summary>Keys an enum entry may carry.</summary>
    private static readonly Dictionary<string, MetaKey> OnEnumValue = new(StringComparer.Ordinal)
    {
        // A second name for a label, which this tool has no field for on either side.
        ["alias"] = MetaKey.NotCarried,
    };

    private enum MetaKey
    {
        /// <summary>Read below, and acted on.</summary>
        Carried,

        /// <summary>Defined by the notation, and not carried by this build.</summary>
        NotCarried,

        /// <summary>Defined, and a second way to write something that already exists.</summary>
        SaidByForeign,
    }

    /// <summary>
    /// Reports the keys nothing reads, once per declaration.
    /// </summary>
    public static void Check(SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        foreach (var declared in declarations.Structs.Values)
        {
            Check(declared.Meta, OnStruct, declared.Name, declared.Name, diagnostics);

            foreach (var member in declared.Fields)
            {
                string where = $"{declared.Name}.{member.Name}";

                Check(member.Meta, OnField, where, declared.Name, diagnostics);
            }
        }

        foreach (var declared in declarations.Enums.Values)
        {
            foreach (var entry in declared.Values)
            {
                Check(
                    entry.Meta, OnEnumValue,
                    $"{declared.Name}.{entry.Name}", declared.Name, diagnostics);
            }
        }
    }

    private static void Check(
        SchemaMeta meta,
        Dictionary<string, MetaKey> known,
        string where,
        string owner,
        Diagnostics diagnostics)
    {
        foreach (var entry in meta.Beyond([.. known.Keys.Where(key => known[key] == MetaKey.Carried)]))
        {
            string id = known.TryGetValue(entry.Key, out var kind)
                ? kind switch
                {
                    MetaKey.SaidByForeign => SchemaMessages.MetaRefsIsForeign,
                    _ => SchemaMessages.MetaKeyNotCarried,
                }
                : SchemaMessages.MetaKeyUnknown;

            diagnostics.Error(entry.Location, Message.Of(
                id, ("Key", entry.Key), ("Where", where), ("Owner", owner)));
        }
    }

    /// <summary>
    /// Puts a member's declared constraints onto the column that carries it.
    /// </summary>
    /// <remarks>
    /// **Both, where both said something.** A declaration says what is true of the type
    /// wherever it is used, and a sheet's own rows say what is true of that one column; a
    /// value has to satisfy each. Narrowing only - a column may tighten what the type
    /// promises and never loosen it, which is what lets somebody read a declaration and know
    /// the floor. Section 6.3 of the design.
    /// </remarks>
    public static void Apply(
        Table table, Field field, SchemaField member, Diagnostics diagnostics)
    {
        ApplyRole(field, member, diagnostics);
        ApplyBounds(field, member, diagnostics);
        ApplyAllowedValues(table, field, member, diagnostics);
        ApplyPattern(table, field, member, diagnostics);
        ApplyLength(field, member, diagnostics);

        // A flag, so there is nothing to narrow: either side saying it makes it true.
        if (member.Meta.Has("notDefault"))
        {
            field.Constraints.NotDefault = true;
            field.Constraints.NotDefaultLocation = member.Meta.LocationOf("notDefault");
        }
    }

    /// <summary>
    /// `text` and `asset`, which are what a string is for rather than what it is.
    /// </summary>
    private static void ApplyRole(Field field, SchemaField member, Diagnostics diagnostics)
    {
        bool text = member.Meta.Has("text");
        string? asset = member.Meta.Value("asset");

        if (!text && asset is null)
            return;

        if (field.Type is not (Models.ValueType.String or Models.ValueType.StringArray))
        {
            diagnostics.Error(member.Meta.LocationOf(text ? "text" : "asset"), Message.Of(
                SchemaMessages.RoleNotAString,
                ("Key", text ? "text" : "asset"),
                ("Member", member.Name),
                ("Type", member.Type.ToString())));

            return;
        }

        if (text && asset is not null)
        {
            diagnostics.Error(member.Meta.LocationOf("asset"), Message.Of(
                SchemaMessages.RoleWrittenTwice, ("Member", member.Name)));
            return;
        }

        // The sheet's own is left where it is. A column that already said what its strings
        // are for said it about that column, and the declaration is the weaker statement.
        if (field.Role != StringRole.None)
            return;

        if (text)
        {
            field.Role = StringRole.Text;
            return;
        }

        field.Role = StringRole.Asset;

        // The kind selects which folders the recipe points at. A blank one is `asset` with
        // no kind, which the recipe may still have a folder for.
        if (asset is { Length: > 0 })
            field.RoleGroup = asset;
    }

    /// <summary>
    /// `min` and `max`, kept as the tighter of the two wherever both were written.
    /// </summary>
    private static void ApplyBounds(Field field, SchemaField member, Diagnostics diagnostics)
    {
        if (Bound(member, "min", diagnostics) is { } minimum
            && (field.Constraints.Minimum is null || minimum > field.Constraints.Minimum))
        {
            field.Constraints.Minimum = minimum;
            field.Constraints.MinimumLocation = member.Meta.LocationOf("min");
        }

        if (Bound(member, "max", diagnostics) is { } maximum
            && (field.Constraints.Maximum is null || maximum < field.Constraints.Maximum))
        {
            field.Constraints.Maximum = maximum;
            field.Constraints.MaximumLocation = member.Meta.LocationOf("max");
        }
    }

    private static double? Bound(SchemaField member, string key, Diagnostics diagnostics)
    {
        string? written = member.Meta.Value(key);

        if (written is null)
            return null;

        if (double.TryParse(
                written, NumberStyles.Float, CultureInfo.InvariantCulture, out double bound))
            return bound;

        diagnostics.Error(member.Meta.LocationOf(key), Message.Of(
            SchemaMessages.BoundNotANumber,
            ("Key", key), ("Member", member.Name), ("Written", written)));

        return null;
    }

    /// <summary>
    /// `regex`, which one column may have one of.
    /// </summary>
    /// <remarks>
    /// **Two patterns cannot be narrowed into one.** A bound has a tighter of the two and a
    /// whitelist has an intersection; two regular expressions have neither, and the
    /// conjunction of them is not a regular expression anybody could read in a report. So a
    /// column that already declares one and a member that declares another is refused rather
    /// than resolved - which of them applies is not something this can decide.
    /// </remarks>
    private static void ApplyPattern(
        Table table, Field field, SchemaField member, Diagnostics diagnostics)
    {
        string? pattern = member.Meta.Value("regex");

        if (pattern is null)
            return;

        // Matching against a formatted number is not what a pattern on a number would
        // mean to whoever wrote it, and there is no reading of it that is. Refused
        // rather than skipped: a check that silently does nothing is worse than none.
        if (field.Type is not (Models.ValueType.String or Models.ValueType.StringArray))
        {
            diagnostics.Error(member.Meta.LocationOf("regex"), Message.Of(
                SchemaMessages.PatternNotAString,
                ("Member", member.Name), ("Type", member.Type.ToString())));

            return;
        }

        if (field.Constraints.Pattern is { } already && already != pattern)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.PatternWrittenTwice,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Member", member.Name),
                ("Sheet", already),
                ("Declared", pattern)));

            return;
        }

        field.Constraints.Pattern = pattern;
        field.Constraints.PatternLocation = member.Meta.LocationOf("regex");
    }

    /// <summary>
    /// `size`, as a count or a range, kept as the tighter of the two.
    /// </summary>
    /// <remarks>
    /// **A check rather than a declaration, and there is no longer anything for it to be
    /// mistaken for.** The design left open whether to rename it - `size` reads like a
    /// statement of how long an array is - but v107 removed fixed-length arrays from the
    /// format entirely, so no notation declares a length any more and this is the only thing
    /// the word can mean.
    /// </remarks>
    private static void ApplyLength(Field field, SchemaField member, Diagnostics diagnostics)
    {
        string? written = member.Meta.Value("size");

        if (written is null)
            return;

        // A length is how many elements a cell holds, and a scalar holds one by being
        // one. Refused for the reason a pattern on a number is.
        if (!member.Type.IsArray)
        {
            diagnostics.Error(member.Meta.LocationOf("size"), Message.Of(
                SchemaMessages.SizeNotAnArray,
                ("Member", member.Name), ("Type", member.Type.ToString())));

            return;
        }

        int? least = null;
        int? most = null;

        int dots = written.IndexOf("..", System.StringComparison.Ordinal);

        if (dots < 0)
        {
            least = Count(member, written, diagnostics);
            most = least;
        }
        else
        {
            string low = written[..dots];
            string high = written[(dots + 2)..];

            least = low.Length > 0 ? Count(member, low, diagnostics) : null;
            most = high.Length > 0 ? Count(member, high, diagnostics) : null;
        }

        if (least is int floor
            && (field.Constraints.MinimumLength is null || floor > field.Constraints.MinimumLength))
        {
            field.Constraints.MinimumLength = floor;
        }

        if (most is int ceiling
            && (field.Constraints.MaximumLength is null || ceiling < field.Constraints.MaximumLength))
        {
            field.Constraints.MaximumLength = ceiling;
        }

        field.Constraints.LengthLocation = member.Meta.LocationOf("size");
    }

    private static int? Count(SchemaField member, string written, Diagnostics diagnostics)
    {
        if (int.TryParse(
                written, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            return count;

        diagnostics.Error(member.Meta.LocationOf("size"), Message.Of(
            SchemaMessages.SizeNotACount, ("Member", member.Name), ("Written", written)));

        return null;
    }

    /// <summary>
    /// `allowed`, kept as what both lists have in common wherever both were written.
    /// </summary>
    /// <remarks>
    /// An empty intersection is reported rather than stored. It is a column no value can
    /// satisfy, so every row would fail against it and none of those reports would name the
    /// two lists that cannot both be met.
    /// </remarks>
    private static void ApplyAllowedValues(
        Table table, Field field, SchemaField member, Diagnostics diagnostics)
    {
        string? written = member.Meta.Value("allowed");

        if (written is null)
            return;

        var declared = written
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (declared.Count == 0)
        {
            diagnostics.Error(member.Meta.LocationOf("allowed"), Message.Of(
                SchemaMessages.AllowedEmpty, ("Member", member.Name)));
            return;
        }

        if (field.Constraints.AllowedValues is not { Count: > 0 } already)
        {
            field.Constraints.AllowedValues = declared;
            field.Constraints.AllowedValuesLocation = member.Meta.LocationOf("allowed");
            return;
        }

        var both = already.Where(declared.Contains).ToList();

        if (both.Count == 0)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.AllowedIntersectionEmpty,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Member", member.Name),
                ("Sheet", string.Join(";", already)),
                ("Declared", written)));

            return;
        }

        field.Constraints.AllowedValues = both;
    }
}
