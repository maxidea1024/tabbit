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

        // Constraints with no place in the model yet - section 5.8 of the Luban comparison
        // and stage five of the design.
        ["notDefault"] = MetaKey.NotCarried,
        ["regex"] = MetaKey.NotCarried,
        ["size"] = MetaKey.NotCarried,
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

                // Applied at conversion time, and this build does not apply it. A default
                // nobody applies is worse than none: the file says what a blank cell means
                // and nothing makes it mean that. Stage four of the design.
                if (member.DefaultValue is not null)
                {
                    diagnostics.Error(member.Location, Message.Of(
                        SchemaMessages.DefaultNotCarried,
                        ("Where", where), ("Written", member.DefaultValue)));
                }
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
