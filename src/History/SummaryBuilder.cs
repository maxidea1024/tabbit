using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tabbit.Models;
using Tabbit.Targets;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.History;

/// <summary>
/// Turns a model into the one document every view of it is rendered from.
///
/// One walk over the data. The fingerprint has already canonicalised every cell, and
/// this reads those back through it rather than deriving the values a second way -
/// two renderings of one value are two chances to disagree, and the whole point of the
/// canonical form is that there is one.
/// </summary>
public static class SummaryBuilder
{
    /// <summary>
    /// Where counting a column's distinct values gives up.
    ///
    /// The count answers "should this column have been an enum", which is a question
    /// about small numbers. A column of unique keys would otherwise hold the entire
    /// column in memory to report a number equal to the row count.
    /// </summary>
    private const int DistinctCap = 10_000;

    /// <summary>
    /// Describes what a conversion produced.
    /// </summary>
    /// <param name="model">
    /// Everything the sheets declared. Never a model narrowed by target side: a summary
    /// taken from a client build would report the server's tables as missing.
    /// </param>
    public static SummaryDocument Build(Model model, CommitInfo commit, TargetContext context)
    {
        var fingerprint = ModelFingerprint.Of(model);

        return new SummaryDocument
        {
            Run = RunOf(model, commit, context),
            Data = DataOf(model, fingerprint),
        };
    }

    // ------------------------------------------------------------------ run

    private static SummaryRun RunOf(Model model, CommitInfo commit, TargetContext context)
    {
        return new SummaryRun
        {
            GeneratedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
            ToolVersion = typeof(SummaryBuilder).Assembly.GetName().Version?.ToString(),

            // From the environment rather than from the options, so it is the same word
            // the recipe's `${TABBIT_ENV}` resolved to - one of them steering the paths
            // while the other labelled the file is the failure this records against.
            Environment = System.Environment.GetEnvironmentVariable(RunEnvironment.Variable) is { } name
                && !string.IsNullOrWhiteSpace(name) ? name : null,

            // The name, not the path: where a machine keeps its checkout says nothing
            // about the data, and putting it in makes two identical conversions differ.
            Recipe = context?.Options?.RecipeFilename is null
                ? null
                : System.IO.Path.GetFileName(context.Options.RecipeFilename),

            // The marker, not the prose: this document is read by a program more often
            // than by a person, and `cs` is what every other side in it says.
            RequestedTargetSide = Side(CommandLineTargetSide.Of(context?.Options)),

            // Null rather than an empty list for the sheets that use no tags, so a summary
            // of a project that has not asked for any is the document it always was.
            RowTags = model.RowTags.Count == 0
                ? null
                : [.. model.RowTags.Select(tag => new SummaryRowTag
                  {
                      Tag = tag.Tag,
                      Rows = tag.Rows,
                      Omitted = tag.Omitted,
                  })],

            Commit = CommitOf(commit),
        };
    }

    private static SummaryCommit CommitOf(CommitInfo commit) => new SummaryCommit
    {
        Hash = commit.Hash,
        ShortHash = commit.ShortHash,
        Branch = commit.Branch,
        AuthorName = commit.AuthorName,
        AuthorEmail = commit.AuthorEmail,
        CommittedAt = commit.CommittedAt?.ToString("o", CultureInfo.InvariantCulture),
        Subject = commit.Subject,
        Origin = char.ToLowerInvariant(commit.Origin.ToString()[0]) + commit.Origin.ToString().Substring(1),
        Dirty = commit.IsDirty,

        // Identified is not enough. A snapshot from a dirty working copy holds work the
        // commit does not describe, and crediting it to that commit's author names the
        // wrong person - which is worse than naming nobody.
        Attributable = commit.IsIdentified && !commit.IsDirty,
    };

    // ----------------------------------------------------------------- data

    private static SummaryData DataOf(Model model, ModelFingerprint fingerprint)
    {
        var tables = fingerprint.Tables.Select(TableOf).ToList();

        return new SummaryData
        {
            Hash = fingerprint.Hash,
            Totals = TotalsOf(model, tables),
            FieldTypes = Tally(tables.SelectMany(t => t.Fields).Select(f => f.TypeName)),
            FieldTargetSides = Tally(tables.SelectMany(t => t.Fields).Select(f => f.TargetSide)),
            Sources = SourcesOf(model),
            Tables = Referencing(model, tables),
            Enums = EnumsOf(model, fingerprint),
            ConstantSets = ConstantSetsOf(model, fingerprint),
        };
    }

    private static SummaryTotals TotalsOf(Model model, IReadOnlyList<SummaryTable> tables) => new SummaryTotals
    {
        Tables = tables.Count,
        Rows = tables.Sum(t => t.RowCount),
        Fields = tables.Sum(t => t.FieldCount),
        Cells = tables.Sum(t => t.CellCount),
        EmptyCells = tables.Sum(t => t.EmptyCellCount),
        ContentBytes = tables.Sum(t => t.ContentBytes),

        Enums = model.Enums.Count,
        EnumLabels = model.Enums.Sum(e => e.Labels.Count),
        ConstantSets = model.ConstantSets.Count,
        Constants = model.ConstantSets.Sum(c => c.Constants.Count),

        ReferenceFields = tables.Sum(t => t.Fields.Count(f => f.IsReference)),
        ArrayFields = tables.Sum(t => t.Fields.Count(f => f.IsArray)),
    };

    private static SummaryTable TableOf(TableFingerprint table)
    {
        // Column 0 is the primary index by construction, which is also what every
        // generated accessor keys its lookup on.
        var counters = table.Fields.Select((f, column) => new FieldCounter(f, isIndex: column == 0)).ToList();

        foreach (var row in table.Rows)
        {
            int column = 0;

            foreach (var cell in table.CellsOf(row))
                counters[column++].Count(cell.Value!);
        }

        var fields = counters.Select(c => c.ToField()).ToList();

        return new SummaryTable
        {
            Name = table.Name,
            Hash = table.Hash,
            SchemaHash = table.SchemaHash,
            Location = LocationOf(table.Location),

            RowCount = table.Rows.Count,
            FieldCount = table.Fields.Count,
            CellCount = (long)table.Rows.Count * table.Fields.Count,
            EmptyCellCount = counters.Sum(c => (long)c.Empty),
            ContentBytes = counters.Sum(c => c.Bytes),

            Fields = fields,
        };
    }

    /// <summary>
    /// Fills in the parts of a table that need the whole model: its comment, its side,
    /// and which tables it points at.
    /// </summary>
    private static IReadOnlyList<SummaryTable> Referencing(Model model, List<SummaryTable> tables)
    {
        var byName = tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var referencedBy = tables.ToDictionary(t => t.Name, _ => new SortedSet<string>(StringComparer.Ordinal),
                                               StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            if (!byName.TryGetValue(table.Name, out var summary))
                continue;

            summary.RawName = table.RawName;
            summary.Comment = Blank(table.Comment);
            summary.TargetSide = Side(table.TargetSide);

            var references = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var field in table.Fields.Where(f => f.IsRef && f.ResolvedRefTable is not null))
            {
                references.Add(field.ResolvedRefTable!.Name);

                if (referencedBy.TryGetValue(field.ResolvedRefTable!.Name, out var incoming))
                    incoming.Add(table.Name);
            }

            summary.References = references.ToList();
        }

        foreach (var table in tables)
            table.ReferencedBy = referencedBy[table.Name].ToList();

        return tables;
    }

    private static IReadOnlyList<SummarySource> SourcesOf(Model model)
    {
        var sources = new SortedDictionary<string, (HashSet<string> Sheets, int Tables, int Rows)>(
            StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            string? file = FilePath(table.Location);

            if (!sources.TryGetValue(file!, out var source))
                source = (new HashSet<string>(StringComparer.Ordinal), 0, 0);

            source.Sheets.Add(table.Location?.Sheet ?? "");

            sources[file!] = (source.Sheets, source.Tables + 1, source.Rows + table.Data.Count);
        }

        return sources.Select(s => new SummarySource
        {
            File = s.Key,
            Sheets = s.Value.Sheets.Count,
            Tables = s.Value.Tables,
            Rows = s.Value.Rows,
        }).ToList();
    }

    private static IReadOnlyList<SummaryEnum> EnumsOf(Model model, ModelFingerprint fingerprint)
    {
        var usedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                if (field.ElementType != ValueType.Enum || field.EnumOrNull is null)
                    continue;

                if (!usedBy.TryGetValue(field.EnumOrNull.Name, out var users))
                    usedBy[field.EnumOrNull.Name] = users = new SortedSet<string>(StringComparer.Ordinal);

                users.Add($"{table.Name}.{field.Name}");
            }
        }

        return model.Enums.Select(enumm => new SummaryEnum
        {
            Name = enumm.Name,
            Comment = Blank(enumm.Comment),
            TargetSide = Side(enumm.TargetSide),
            Location = LocationOf(enumm.Location),

            Labels = enumm.Labels.Select(label => new SummaryEnumLabel
            {
                Name = label.Name,
                Value = label.Value,
                Comment = Blank(label.Comment),
            }).ToList(),

            UsedBy = usedBy.TryGetValue(enumm.Name, out var users)
                ? users.ToList()
                : (IReadOnlyList<string>)Array.Empty<string>(),
        }).ToList();
    }

    private static IReadOnlyList<SummaryConstantSet> ConstantSetsOf(Model model, ModelFingerprint fingerprint)
    {
        // Keyed by name, so a constant's canonical value is read from the fingerprint
        // rather than rendered a second way here.
        var values = fingerprint.ConstantSets.ToDictionary(
            set => set.Name,
            set => set.Members.ToDictionary(m => m.Name, m => m.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

        return model.ConstantSets.Select(set => new SummaryConstantSet
        {
            Name = set.Name,
            Comment = Blank(set.Comment),
            TargetSide = Side(set.TargetSide),
            Location = LocationOf(set.Location),

            Constants = set.Constants.Select(constant => new SummaryConstant
            {
                Name = constant.Name,
                TypeName = constant.TypeName,
                Value = values.TryGetValue(set.Name, out var members)
                        && members.TryGetValue(constant.Name, out string? value)
                    ? value
                    : null,
                Comment = Blank(constant.Comment),
            }).ToList(),
        }).ToList();
    }

    // -------------------------------------------------------------- counting

    /// <summary>
    /// Accumulates one column's statistics as the rows go past.
    /// </summary>
    private sealed class FieldCounter
    {
        private readonly FieldFingerprint _field;
        private readonly bool _isIndex;
        private readonly HashSet<string> _distinct = new HashSet<string>(StringComparer.Ordinal);

        private bool _capped;
        private bool _hasBlank;
        private int _maxLength;

        public FieldCounter(FieldFingerprint field, bool isIndex)
        {
            _field = field;
            _isIndex = isIndex;
        }

        public int Empty { get; private set; }

        public long Bytes { get; private set; }

        public void Count(string value)
        {
            if (value is null)
            {
                Empty++;

                // A flag rather than a key in the set. A blank cell is a value the
                // column takes and has to be counted as one, but there is no string
                // that could stand for it - every string is a value some cell might
                // really hold, and using "" would report a column that is blank in
                // some rows and empty in others as having one value where it has two.
                _hasBlank = true;
                return;
            }

            Bytes += Encoding.UTF8.GetByteCount(value);

            if (value.Length > _maxLength)
                _maxLength = value.Length;

            Track(value);
        }

        private void Track(string value)
        {
            if (_capped)
                return;

            if (_distinct.Count >= DistinctCap && !_distinct.Contains(value))
            {
                // Stops here rather than growing without bound. The set is kept so the
                // reported count is the cap exactly, not whatever it reached.
                _capped = true;
                return;
            }

            _distinct.Add(value);
        }

        public SummaryField ToField() => new SummaryField
        {
            Name = _field.Name,
            RawName = _field.RawName,
            TypeName = _field.TypeName,
            Type = _field.Type.ToString(),
            TargetSide = Side(_field.TargetSide),
            Comment = Blank(_field.Comment),
            Location = LocationOf(_field.Location),

            IsIndex = _isIndex,
            IsArray = ValueTypes.IsArray(_field.Type),
            IsReference = _field.IsRef,
            RefTable = Blank(_field.RefTableName),
            RefField = Blank(_field.RefFieldName),

            EmptyCount = Empty,
            DistinctCount = _distinct.Count + (_hasBlank ? 1 : 0),
            DistinctCapped = _capped,

            // Only where a length means something. The width of `1048576` is a fact
            // about the number, not about the column.
            MaxLength = ValueTypes.ElementOf(_field.Type) == ValueType.String ? _maxLength : (int?)null,
        };
    }

    // -------------------------------------------------------------- helpers

    private static IDictionary<string, int> Tally(IEnumerable<string> values)
    {
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            tally.TryGetValue(value ?? "", out int count);
            tally[value ?? ""] = count + 1;
        }

        return tally;
    }

    private static SummaryLocation? LocationOf(Location location)
    {
        if (location is null)
            return null;

        return new SummaryLocation
        {
            File = FilePath(location),
            Sheet = location.Sheet,
            Cell = location.CellRange,
            Url = Blank(location.SheetUrl),
        };
    }

    /// <summary>
    /// The source path with forward slashes.
    ///
    /// A path written with the separator of whichever machine ran the build would make
    /// the same data describe differently on Windows and on CI.
    /// </summary>
    private static string? FilePath(Location? location)
        => location?.Filename?.Replace('\\', '/') ?? "";

    private static string Side(TargetSide side)
    {
        return side switch
        {
            TargetSide.ClientOnly => "c",
            TargetSide.ServerOnly => "s",
            _ => "cs",
        };
    }

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
