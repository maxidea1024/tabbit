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

        // A gravestone one level up from a member's: a dropped variant, kept so that its
        // discriminator is not handed to another one. spec/types/polymorphism.md section 5.1.1.
        ["removed"] = MetaKey.Carried,
    };

    /// <summary>Keys a member may carry.</summary>
    private static readonly Dictionary<string, MetaKey> OnField = new(StringComparer.Ordinal)
    {
        ["text"] = MetaKey.Carried,

        // The set a `text` column is gathered into is written here rather than as a second
        // value on `text`, because the sheet's brackets write it as its own key and the two
        // notations state one dictionary - spec/layout/primary-layout.md section 4.2.
        ["namespace"] = MetaKey.Carried,

        ["asset"] = MetaKey.Carried,
        ["min"] = MetaKey.Carried,
        ["max"] = MetaKey.Carried,
        ["allowed"] = MetaKey.Carried,

        // A gravestone, which the parser reads and this build acts on - it is what keeps a
        // dropped member's wire tag from being handed to a new one.
        ["removed"] = MetaKey.Carried,

        // The tables a value has to be an id of, checked and nothing else. It is not a
        // weaker `foreign`: `foreign` names one table, resolves it and narrows the column
        // to that table's key type, while this states where a value may have come from and
        // leaves the type alone. Several tables is what it exists for - "one of these" has
        // no single type to resolve to, so it is a check and not a reference.
        // spec/references/reference-surface-naming.md section 6.
        ["refs"] = MetaKey.Carried,

        ["notDefault"] = MetaKey.Carried,
        ["regex"] = MetaKey.Carried,
        ["size"] = MetaKey.Carried,

        // The one of the four that has nowhere to go. Section 6.5 puts uniqueness on
        // the array rather than on the type, because the same struct used somewhere
        // that is not an array has nothing for it to mean - and the array a struct is
        // used as is a group of sheet columns, which has no brackets to write it in.
        ["uniqueBy"] = MetaKey.NotCarried,

        // A consumer's own label. Held on the field and nothing else - this tool does not
        // read the words, check them or produce them. spec/layout/tags.md section 6.
        ["tag"] = MetaKey.Carried,
    };

    /// <summary>Keys an enum entry may carry.</summary>
    private static readonly Dictionary<string, MetaKey> OnEnumValue = new(StringComparer.Ordinal)
    {
        // A second name for a label, which this tool has no field for on either side.
        ["alias"] = MetaKey.NotCarried,
    };

    /// <summary>
    /// The keys of each kind that this build acts on, for an editor offering them.
    /// </summary>
    /// <remarks>
    /// The ones the notation defines and this build does not carry are left out. Offering a
    /// key and then reporting it as one that does nothing is two answers to the same question,
    /// and the offer is the one somebody acts on.
    /// </remarks>
    public static IEnumerable<string> CarriedOnStruct => Carried(OnStruct);

    public static IEnumerable<string> CarriedOnField => Carried(OnField);

    public static IEnumerable<string> CarriedOnEnumValue => Carried(OnEnumValue);

    private static IEnumerable<string> Carried(Dictionary<string, MetaKey> keys)
        => keys.Where(entry => entry.Value == MetaKey.Carried)
               .Select(entry => entry.Key)
               .OrderBy(key => key, StringComparer.Ordinal);

    private enum MetaKey
    {
        /// <summary>Read below, and acted on.</summary>
        Carried,

        /// <summary>Defined by the notation, and not carried by this build.</summary>
        NotCarried,
    }

    /// <summary>
    /// Reports the keys nothing reads, once per declaration.
    /// </summary>
    public static void Check(SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        // The tombstones too, which are not in `Structs`: a typo in the brackets of a
        // declaration nothing generates is still a typo, and the number it holds is the
        // reason the line is there at all.
        foreach (var declared in declarations.Structs.Values.Concat(declarations.RemovedVariants))
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

    /// <summary>
    /// Reports the keys nothing reads on one column's brackets, wherever they were written.
    /// </summary>
    /// <remarks>
    /// For a layout whose type cells carry the same brackets a declaration does. It checks
    /// against the same dictionary, so a key this build carries in one notation is carried in
    /// the other and a typo is a typo in both - `spec/layout/primary-layout.md` section 4.2.
    /// </remarks>
    public static void CheckFieldKeys(
        SchemaMeta meta, string where, string owner, Diagnostics diagnostics)
        => Check(meta, OnField, where, owner, diagnostics);

    private static void Check(
        SchemaMeta meta,
        Dictionary<string, MetaKey> known,
        string where,
        string owner,
        Diagnostics diagnostics)
    {
        foreach (var entry in meta.Beyond([.. known.Keys.Where(key => known[key] == MetaKey.Carried)]))
        {
            string id = known.ContainsKey(entry.Key)
                ? SchemaMessages.MetaKeyNotCarried
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
        => Apply(
            table, field, member.Meta, member.Name, member.Type.ToString(),
            member.Type.IsArray, diagnostics);

    /// <summary>
    /// The same, for a notation that wrote the brackets somewhere other than a declaration.
    /// </summary>
    /// <remarks>
    /// **The keys are the notation's, not the declaration's.** A layout whose type cells carry
    /// the same brackets reads them by handing the pairs here, so there is one dictionary of
    /// keys, one meaning for each and one set of checks - rather than a second implementation
    /// that agrees until it does not. `spec/layout/primary-layout.md` section 4.2 makes that the rule.
    /// </remarks>
    public static void Apply(
        Table table, Field field, SchemaMeta meta, string memberName, string typeName,
        bool typeIsArray, Diagnostics diagnostics)
    {
        ApplyRole(field, meta, memberName, typeName, diagnostics);
        ApplyBounds(field, meta, memberName, diagnostics);
        ApplyAllowedValues(table, field, meta, memberName, diagnostics);
        ApplyReferencedTables(table, field, meta, memberName, diagnostics);
        ApplyPattern(table, field, meta, memberName, typeName, diagnostics);
        ApplyLength(field, meta, memberName, typeName, typeIsArray, diagnostics);

        ApplyMetaTags(field, meta);

        // A flag, so there is nothing to narrow: either side saying it makes it true.
        if (meta.Has("notDefault"))
        {
            field.Constraints.NotDefault = true;
            field.Constraints.NotDefaultLocation = meta.LocationOf("notDefault");
        }
    }

    /// <summary>
    /// The labels a declaration or a sheet wrote for something outside this tool.
    /// </summary>
    /// <remarks>
    /// Both sides add: a struct's members may be labelled where the type is declared and the
    /// column that carries one may say more, and neither is a narrowing of the other because
    /// nothing here reads the words. One key written on both sides takes the column's value,
    /// which is the direction everything else in this class goes.
    /// </remarks>
    private static void ApplyMetaTags(Field field, SchemaMeta meta)
    {
        string? written = meta.Value("tag");

        if (written is null)
            return;

        MetaTagText.ReadInto(written, field.MetaTags);
    }

    /// <summary>
    /// `text` and `asset`, which are what a string is for rather than what it is.
    /// </summary>
    private static void ApplyRole(
        Field field, SchemaMeta meta, string memberName, string typeName,
        Diagnostics diagnostics)
    {
        bool text = meta.Has("text");
        string? asset = meta.Value("asset");
        string? space = meta.Value("namespace");

        if (!text && asset is null)
        {
            // A namespace with nothing to qualify. Reported rather than dropped - a key that
            // was written and is not read is what this class exists to catch.
            if (space is not null)
            {
                diagnostics.Error(meta.LocationOf("namespace"), Message.Of(
                    SchemaMessages.RoleSpaceWithoutText, ("Member", memberName)));
            }

            return;
        }

        if (field.Type is not (Models.ValueType.String or Models.ValueType.StringArray))
        {
            diagnostics.Error(meta.LocationOf(text ? "text" : "asset"), Message.Of(
                SchemaMessages.RoleNotAString,
                ("Key", text ? "text" : "asset"),
                ("Member", memberName),
                ("Type", typeName)));

            return;
        }

        if (text && asset is not null)
        {
            diagnostics.Error(meta.LocationOf("asset"), Message.Of(
                SchemaMessages.RoleWrittenTwice, ("Member", memberName)));
            return;
        }

        // The folders an asset is looked for in come from the recipe, keyed by the kind, so
        // there is nothing for a namespace to qualify on that side.
        if (space is not null && !text)
        {
            diagnostics.Error(meta.LocationOf("namespace"), Message.Of(
                SchemaMessages.RoleSpaceNotText, ("Member", memberName)));
            return;
        }

        // The sheet's own is left where it is. A column that already said what its strings
        // are for said it about that column, and the declaration is the weaker statement.
        if (field.Role != StringRole.None)
            return;

        if (text)
        {
            field.Role = StringRole.Text;

            // **`text` carries a value as well as being a flag.** `(text)` gathers into the
            // default set and `(text=Common)` names one, which is the same pair of readings the
            // sheet's brackets have - spec/layout/primary-layout.md section 4.2. Reading only the flag
            // accepted `(text=Common)` and dropped the name, which is the shape of quiet loss
            // this tool exists to prevent.
            string? group = meta.Value("text");

            if (group is { Length: > 0 })
                field.RoleGroup = group;

            if (space is { Length: > 0 })
                field.RoleNamespace = space;

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
    private static void ApplyBounds(
        Field field, SchemaMeta meta, string memberName, Diagnostics diagnostics)
    {
        if (Bound(meta, memberName, "min", diagnostics) is { } minimum
            && (field.Constraints.Minimum is null || minimum > field.Constraints.Minimum))
        {
            field.Constraints.Minimum = minimum;
            field.Constraints.MinimumLocation = meta.LocationOf("min");
        }

        if (Bound(meta, memberName, "max", diagnostics) is { } maximum
            && (field.Constraints.Maximum is null || maximum < field.Constraints.Maximum))
        {
            field.Constraints.Maximum = maximum;
            field.Constraints.MaximumLocation = meta.LocationOf("max");
        }
    }

    private static double? Bound(
        SchemaMeta meta, string memberName, string key, Diagnostics diagnostics)
    {
        string? written = meta.Value(key);

        if (written is null)
            return null;

        if (double.TryParse(
                written, NumberStyles.Float, CultureInfo.InvariantCulture, out double bound))
            return bound;

        diagnostics.Error(meta.LocationOf(key), Message.Of(
            SchemaMessages.BoundNotANumber,
            ("Key", key), ("Member", memberName), ("Written", written)));

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
        Table table, Field field, SchemaMeta meta, string memberName, string typeName,
        Diagnostics diagnostics)
    {
        string? pattern = meta.Value("regex");

        if (pattern is null)
            return;

        // Matching against a formatted number is not what a pattern on a number would
        // mean to whoever wrote it, and there is no reading of it that is. Refused
        // rather than skipped: a check that silently does nothing is worse than none.
        if (field.Type is not (Models.ValueType.String or Models.ValueType.StringArray))
        {
            diagnostics.Error(meta.LocationOf("regex"), Message.Of(
                SchemaMessages.PatternNotAString,
                ("Member", memberName), ("Type", typeName)));

            return;
        }

        if (field.Constraints.Pattern is { } already && already != pattern)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.PatternWrittenTwice,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Member", memberName),
                ("Sheet", already),
                ("Declared", pattern)));

            return;
        }

        field.Constraints.Pattern = pattern;
        field.Constraints.PatternLocation = meta.LocationOf("regex");
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
    private static void ApplyLength(
        Field field, SchemaMeta meta, string memberName, string typeName, bool typeIsArray,
        Diagnostics diagnostics)
    {
        string? written = meta.Value("size");

        if (written is null)
            return;

        // A length is how many elements a cell holds, and a scalar holds one by being
        // one. Refused for the reason a pattern on a number is.
        if (!typeIsArray)
        {
            diagnostics.Error(meta.LocationOf("size"), Message.Of(
                SchemaMessages.SizeNotAnArray,
                ("Member", memberName), ("Type", typeName)));

            return;
        }

        int? least = null;
        int? most = null;

        int dots = written.IndexOf("..", System.StringComparison.Ordinal);

        if (dots < 0)
        {
            least = Count(meta, memberName, written, diagnostics);
            most = least;
        }
        else
        {
            string low = written[..dots];
            string high = written[(dots + 2)..];

            least = low.Length > 0 ? Count(meta, memberName, low, diagnostics) : null;
            most = high.Length > 0 ? Count(meta, memberName, high, diagnostics) : null;
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

        field.Constraints.LengthLocation = meta.LocationOf("size");
    }

    private static int? Count(
        SchemaMeta meta, string memberName, string written, Diagnostics diagnostics)
    {
        if (int.TryParse(
                written, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            return count;

        diagnostics.Error(meta.LocationOf("size"), Message.Of(
            SchemaMessages.SizeNotACount, ("Member", memberName), ("Written", written)));

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
        Table table, Field field, SchemaMeta meta, string memberName,
        Diagnostics diagnostics)
    {
        string? written = meta.Value("allowed");

        if (written is null)
            return;

        var declared = written
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (declared.Count == 0)
        {
            diagnostics.Error(meta.LocationOf("allowed"), Message.Of(
                SchemaMessages.AllowedEmpty, ("Member", memberName)));
            return;
        }

        if (field.Constraints.AllowedValues is not { Count: > 0 } already)
        {
            field.Constraints.AllowedValues = declared;
            field.Constraints.AllowedValuesLocation = meta.LocationOf("allowed");
            return;
        }

        var both = already.Where(declared.Contains).ToList();

        if (both.Count == 0)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.AllowedIntersectionEmpty,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Member", memberName),
                ("Sheet", string.Join(";", already)),
                ("Declared", written)));

            return;
        }

        field.Constraints.AllowedValues = both;
    }

    /// <summary>
    /// `refs`, the tables a value has to be an id of.
    /// </summary>
    /// <remarks>
    /// **A check, and the type is untouched.** `allowed` is a list of values and this is a
    /// list of the places values come from, so it sits in the same brackets, parts on the
    /// same `;`, and narrows the same way when both a declaration and a sheet wrote one.
    /// spec/references/reference-surface-naming.md section 6.
    ///
    /// **Not on a column that is already a reference.** `foreign` names one table and
    /// resolves it, which is a stronger statement than "the value is an id of one of
    /// these"; carrying both would leave two declarations that can disagree about the same
    /// column, and nothing decides which one loses.
    ///
    /// Tables named here are checked for existence later, once every sheet is read - a name
    /// cannot be resolved while the sheet that declares it may still be unread.
    /// </remarks>
    private static void ApplyReferencedTables(
        Table table, Field field, SchemaMeta meta, string memberName,
        Diagnostics diagnostics)
    {
        string? written = meta.Value("refs");

        if (written is null)
            return;

        if (field.IsRef || field.RefTableName is not null)
        {
            diagnostics.Error(meta.LocationOf("refs"), Message.Of(
                SchemaMessages.MetaRefsIsForeign,
                ("Key", "refs"), ("Where", $"{table.Name}.{field.RawName}"),
                ("Owner", table.Name)));
            return;
        }

        var declared = written
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (declared.Count == 0)
        {
            diagnostics.Error(meta.LocationOf("refs"), Message.Of(
                SchemaMessages.RefsEmpty, ("Member", memberName)));
            return;
        }

        if (field.Constraints.ReferencedTables is not { Count: > 0 } already)
        {
            field.Constraints.ReferencedTables = declared;
            field.Constraints.ReferencedTablesLocation = meta.LocationOf("refs");
            return;
        }

        // Both said something, so the value has to satisfy both - it is an id of a table in
        // each list, which is a table in both.
        var both = already
            .Where(name => declared.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (both.Count == 0)
        {
            diagnostics.Error(field.TypeLocation, Message.Of(
                SchemaMessages.RefsIntersectionEmpty,
                ("Table", table.Name),
                ("Column", field.RawName),
                ("Member", memberName),
                ("Sheet", string.Join(";", already)),
                ("Declared", written)));

            return;
        }

        field.Constraints.ReferencedTables = both;
    }
}
