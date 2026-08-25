using Newtonsoft.Json;
using Serilog;
using Tabbit.Helpers;
using Tabbit.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Messages;

namespace Tabbit.Exporters;

/// <summary>
/// What the columns looked like the last time data was written, and what may change
/// about them.
///
/// The format itself makes a schema change survivable at read time: a column is found
/// by tag, an unknown one is skipped, and one whose type no longer fits the member is
/// refused by name. That is the last line of defence, and it fires in whoever's process
/// is reading the file - a client that has already shipped.
///
/// This is the first line. It runs at conversion time, against a record of the previous
/// schema that lives in source control, and it refuses to write data whose columns
/// changed in a way that would break a build already out there. The refusal names the
/// column and says what to do about it, which is the difference between finding out now
/// and finding out from a crash report.
///
/// The rules, and every one of them is about what an already-deployed reader does with
/// the file this run is about to write:
///
///   a new tag                  fine. A reader that does not know it skips it.
///   a renamed column           fine. The tag is the identity, not the name.
///   a reordered column         fine. Position means nothing in the file.
///   a column that vanished     tombstone it in the sheet (`#Name@N`) so its tag stays
///                              reserved, or acknowledge it. Otherwise the tag is free
///                              for something else to take, and then an old reader
///                              reads the new column as the old one.
///   a type change              acknowledge it. Even a widening breaks an old reader -
///                              it refuses to narrow, by design - so this is a change
///                              that has to go out with regenerated code.
///   a shape change             the same: a fixed array that grew is a column an old
///                              reader will not take.
///   a retired tag returning    refused outright, with no acknowledgment available. It
///                              is the one change the tag scheme cannot survive: an old
///                              reader would read the new column as the retired one and
///                              be right to.
///
/// Tables with no explicit `@N` tags are held to more than this, because their tags come
/// from column order: delete a column there and every column after it changes tag. A
/// name that moved to a different tag is that, and it is refused.
/// </summary>
public class SchemaBaseline
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    /// <summary>What this file is, for whoever opens it without context.</summary>
    public string Comment { get; set; } =
        "Tabbit's record of the columns data was last written with. Commit it: the " +
        "converter compares the schema against this and refuses a change that would " +
        "break a reader already deployed. See doc/binary-format.md.";

    /// <summary>
    /// What this file's numbers mean, which is not the same question as what it holds.
    /// </summary>
    /// <remarks>
    /// `Kind` is stored as the wire's own number, so the meaning of a baseline is tied to the
    /// table format version. v107 renumbered the array kinds, and a v1 baseline compared
    /// against a v107 build reported every variable-length array as a scalar that had become
    /// an array - a breaking change that had not happened.
    ///
    /// An older baseline is therefore replaced rather than compared. Nothing is lost by it:
    /// the question this file asks is whether an already-deployed reader would break, and a
    /// reader from before a format version bump cannot read the new files at all. It is
    /// refused by version at the header, so the answer is already yes and unavoidable.
    /// </remarks>
    public const int CurrentBaselineVersion = 2;

    public int BaselineVersion { get; set; } = CurrentBaselineVersion;

    /// <summary>Table name to its columns, keyed by tag.</summary>
    public Dictionary<string, Dictionary<string, Column>> Tables { get; set; }
        = new Dictionary<string, Dictionary<string, Column>>();

    /// <summary>One column as it was when data was last written.</summary>
    public class Column
    {
        /// <summary>The name it had then. Only ever used in a message.</summary>
        public required string Name { get; set; }

        /// <summary>The wire element, as the descriptor's low nibble.</summary>
        public byte Element { get; set; }

        /// <summary>Scalar or array.</summary>
        /// <remarks>
        /// An element count used to sit beside this. It said how many elements a fixed
        /// array held per row, and a change to it was a breaking change; since v107 there
        /// is no fixed array and a length is per row, so adding an element to a group is
        /// no longer a schema change at all. spec/tcb-v107-dynamic-arrays.md.
        /// </remarks>
        public byte Kind { get; set; }

        /// <summary>
        /// Whether the column is gone and the tag is spent.
        ///
        /// Kept rather than dropped, because the whole point is to remember: a tag that
        /// has carried data once can never carry anything else.
        /// </summary>
        public bool Retired { get; set; }

        /// <summary>Whether the table it belongs to spells its tags out.</summary>
        public bool ExplicitTag { get; set; }
    }

    /// <summary>
    /// Compares the model against the baseline at `filename`, and writes the model's own
    /// shape back as the new baseline.
    ///
    /// Throws when a change is one an already-deployed reader would not survive and the
    /// recipe has not acknowledged it. Nothing is written in that case - the write goes
    /// through the staging area, which is only committed by a run that finishes.
    /// </summary>
    /// <param name="acknowledged">
    /// `Table.Column` entries from the recipe's `AcceptSchemaChanges`, naming the columns
    /// whose changes are deliberate and going out with regenerated code.
    /// </param>
    public static void Check(string filename, Model model, IReadOnlyList<string> acknowledged)
    {
        var baseline = Load(filename);
        var accepted = new HashSet<string>(acknowledged ?? new List<string>());
        var problems = new List<string>();

        var updated = new SchemaBaseline();

        foreach (var table in model.Tables)
        {
            var current = Current(table);

            baseline.Tables.TryGetValue(table.Name, out var previous);
            previous ??= new Dictionary<string, Column>();

            updated.Tables[table.Name] = Compare(table, current, previous, accepted, problems);
        }

        // A table that is no longer in the model keeps its entry: the data files it wrote
        // are still out there, and dropping the record would let its tags come back.
        foreach (var pair in baseline.Tables)
        {
            if (updated.Tables.ContainsKey(pair.Key))
                continue;

            foreach (var column in pair.Value.Values)
                column.Retired = true;

            updated.Tables[pair.Key] = pair.Value;
        }

        if (problems.Count > 0)
        {
            throw new TabbitException(null,
                Message.Of(ExportMessages.SchemaBrokeReaders,
                    ("Count", problems.Count),
                    ("Problems", string.Join("\n\n  ", problems)),
                    ("Baseline", Path.GetFullPath(filename))));
        }

        StagingFiles.WriteToJsonFile(filename, updated);

        Log.Information($"Schema baseline checked against '{Path.GetFullPath(filename)}'");
    }

    /// <summary>
    /// One table's columns after the comparison: what the current schema says, plus the
    /// tags the baseline remembers and the schema no longer uses.
    /// </summary>
    private static Dictionary<string, Column> Compare(
        Table table,
        Dictionary<string, Column> current,
        Dictionary<string, Column> previous,
        HashSet<string> accepted,
        List<string> problems)
    {
        var result = new Dictionary<string, Column>();

        foreach (var pair in current)
        {
            string tag = pair.Key;
            Column now = pair.Value;

            result[tag] = now;

            if (!previous.TryGetValue(tag, out Column? before))
                continue;

            if (before.Retired)
            {
                // No acknowledgment for this one. Every other change leaves an old
                // reader either reading the right column or refusing to read; this one
                // leaves it reading the wrong column and succeeding.
                problems.Add(
                    $"`{table.Name}.{now.Name}` has taken tag {tag}, which `{before.Name}` " +
                    "used and gave up. A reader built while it was still there would read " +
                    $"this column as `{before.Name}`. Give `{now.Name}` a tag no column of " +
                    "this table has ever had.");
                continue;
            }

            if (!before.ExplicitTag && !now.ExplicitTag && before.Name != now.Name
                && !accepted.Contains($"{table.Name}.{now.Name}"))
            {
                // Without `@N` tags the tag is the column's position, so a name that
                // moved means every column after it moved too.
                problems.Add(
                    $"`{table.Name}` has no explicit tags, and tag {tag} was `{before.Name}` " +
                    $"and is now `{now.Name}`. Deleting or reordering a column shifts every " +
                    "tag after it, which silently repoints an already-deployed reader. Give " +
                    $"the columns of `{table.Name}` explicit `@N` tags, or acknowledge this " +
                    $"with \"{table.Name}.{now.Name}\".");
                continue;
            }

            if (before.Element == now.Element && before.Kind == now.Kind)
                continue;

            if (accepted.Contains($"{table.Name}.{now.Name}"))
                continue;

            problems.Add(
                $"`{table.Name}.{now.Name}` (tag {tag}) was {Describe(before)} and is now " +
                $"{Describe(now)}. A reader built from the previous schema refuses this " +
                "column rather than reading it wrongly, so the change has to ship with " +
                $"regenerated code. Acknowledge it with \"{table.Name}.{now.Name}\" in " +
                "`AcceptSchemaChanges`.");
        }

        foreach (var pair in previous)
        {
            if (current.ContainsKey(pair.Key))
                continue;

            Column before = pair.Value;

            // Kept as retired whatever happens next, so the tag cannot come back even if
            // the deletion itself is acknowledged.
            before.Retired = true;
            result[pair.Key] = before;

            bool tombstoned = table.ReservedTags.Contains(int.Parse(pair.Key));

            if (tombstoned || accepted.Contains($"{table.Name}.{before.Name}"))
                continue;

            problems.Add(
                $"`{table.Name}.{before.Name}` (tag {pair.Key}) is gone from the schema. " +
                $"Tombstone it in the sheet as `#{before.Name}@{pair.Key}` so nothing takes " +
                $"its tag, or acknowledge the deletion with \"{table.Name}.{before.Name}\" " +
                "in `AcceptSchemaChanges`.");
        }

        return result;
    }

    /// <summary>The current schema's columns, in the shape the baseline stores.</summary>
    private static Dictionary<string, Column> Current(Table table)
    {
        var columns = new Dictionary<string, Column>();

        // Keyed by tag, so the unit has to be the wire column - a record group holds one
        // per member. Reading serial fields here would key a whole group by its first
        // member's tag and describe the other members as columns that had vanished.
        foreach (var column in table.WireColumns)
        {
            columns[column.TagCarrier.WireTag!.Value.ToString()] = new Column
            {
                Name = column.Name,
                Element = TcbFormat.ElementFor(column),
                Kind = TcbFormat.KindFor(column),
                ExplicitTag = table.HasExplicitTags,
            };
        }

        return columns;
    }

    /// <summary>A column's wire shape, for a message a person has to act on.</summary>
    private static string Describe(Column column)
    {
        string element = column.Element switch
        {
            TcbFormat.ElementVarint => "a varint",
            TcbFormat.ElementBool => "a bool",
            TcbFormat.ElementI32 => "a 32 bit integer",
            TcbFormat.ElementI64 => "a 64 bit integer",
            TcbFormat.ElementF32 => "a single",
            TcbFormat.ElementF64 => "a double",
            TcbFormat.ElementString => "a string",
            TcbFormat.ElementUuid => "a uuid",
            _ => $"element type {column.Element}",
        };

        return column.Kind switch
        {
            TcbFormat.KindArray => $"an array of {element}",
            _ => element,
        };
    }

    private static SchemaBaseline Load(string filename)
    {
        // A missing baseline is a first run rather than an error: the file is written by
        // the run that had nothing to compare against, and reviewed like any other new
        // file before it is committed.
        if (!File.Exists(filename))
        {
            Log.Information(
                $"No schema baseline at '{Path.GetFullPath(filename)}' yet. Writing one - " +
                "commit it, and later runs are checked against it.");

            return new SchemaBaseline();
        }

        try
        {
            var loaded = JsonConvert.DeserializeObject<SchemaBaseline>(File.ReadAllText(filename))
                         ?? new SchemaBaseline();

            if (loaded.BaselineVersion != CurrentBaselineVersion)
            {
                Log.Warning(
                    $"The schema baseline at '{Path.GetFullPath(filename)}' was written by an " +
                    $"older build (baseline version {loaded.BaselineVersion}, this build writes " +
                    $"{CurrentBaselineVersion}). Its column shapes are recorded as wire numbers " +
                    "and those have moved, so it is replaced rather than compared - review the " +
                    "new file and commit it.");

                return new SchemaBaseline();
            }

            return loaded;
        }
        catch (JsonException e)
        {
            throw new TabbitException(null,
                Message.Of(ExportMessages.SchemaBaselineUnreadable,
                    ("Baseline", Path.GetFullPath(filename)), ("Detail", e.Message)));
        }
    }
}
