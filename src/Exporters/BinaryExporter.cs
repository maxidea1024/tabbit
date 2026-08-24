using CommandLine;
using Tabbit.Recipe;
using Tabbit.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using Tabbit.Helpers;
using System.Collections.Generic;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// One binary file per table, in Tabbit's own Tcb format.
///
/// This is what the generated C# and C++ readers consume.
/// </summary>
public class BinaryRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Extension of each table file. Must match the extension the code
    /// generators are told to expect.
    /// </summary>
    public string FileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Reserved. Not implemented: the format writes a reserved byte where a
    /// compression flag would go, but nothing sets or reads it, and the
    /// generated readers reject a non-zero value.
    /// </summary>
    public bool Compress { get; set; } = false;

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    ///
    /// Declare the same side on the exporter and on the code generator
    /// that reads its files: the two must agree on the column set or the
    /// generated reader will not match the data.
    /// </summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>Removes files this run did not write.</summary>
    /// <remarks>
    /// On, because the output is a file per table: rename or delete a table and
    /// its old file stays behind. A stale data file is worse than a stale source
    /// file - it ships, it costs transfer, and a build still asking for the old
    /// name reads it, which is old values from a rollback nobody performed.
    ///
    /// Only files the manifest already lists are removed. That ledger is this
    /// tool's own record of what it put here, so a directory holding anything
    /// else is untouchable - the file has to have been written by a previous run
    /// to be removable by this one.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>
    /// Where to keep the record of the columns data was last written with.
    ///
    /// Commit the file. Every run compares the schema against it and refuses a
    /// change that a reader already built from the previous schema would not
    /// survive - a deleted column whose tag is left free, a type that changed,
    /// a fixed array that grew. Blank switches the check off, which leaves the
    /// generated readers' own refusals as the only guard, and those fire in the
    /// client rather than here.
    /// </summary>
    public string SchemaBaseline { get; set; } = "";

    /// <summary>
    /// Columns whose changed shape is deliberate, as `Table.Column`.
    ///
    /// A type change is not a thing the baseline can wave through on its own:
    /// an already-deployed reader refuses the column rather than reading it
    /// wrongly, so the change only works if regenerated code ships with the
    /// data. Naming the column here says that it does.
    ///
    /// An acknowledgment is spent once. The next run compares against a baseline
    /// that already has the new shape, so the entry can be taken back out.
    /// </summary>
    public List<string> AcceptSchemaChanges { get; set; } = new List<string>();

    /// <summary>
    /// Where to write a report of what every column measured. Blank switches it off.
    /// </summary>
    /// <remarks>
    /// The exporter already encodes every applicable candidate in full and keeps the
    /// smallest, so the sizes are measurements rather than estimates - the report
    /// states the same numbers the choice was made on, plus what the alternatives
    /// would have come to.
    ///
    /// It also measures layouts the format does not have, over the distinct values of
    /// each string column, so that adding one can be argued from a number. Doing that
    /// costs real time on a large export, which is why it happens only when a path is
    /// named here.
    /// </remarks>
    public string EncodingReport { get; set; } = "";

    /// <summary>
    /// The environment variable holding the encryption key, as 64 hexadecimal
    /// characters. Blank leaves the files unencrypted.
    /// </summary>
    /// <remarks>
    /// The name of the variable, never the key. A recipe is committed and handed
    /// around, and a key written into one is in a repository's history from then on.
    ///
    /// What the encryption is for is stated in the format's own documentation, and
    /// the short of it is that it stops a data file from opening as plain text and
    /// from accepting an edit, not from being read by someone who can take the key
    /// out of the client that carries it.
    /// </remarks>
    public string EncryptionKeyVariable { get; set; } = "";

    /// <summary>
    /// A file holding the encryption key, as 64 hexadecimal characters. An
    /// alternative to <see cref="EncryptionKeyVariable"/>; naming both is refused.
    /// </summary>
    public string EncryptionKeyFile { get; set; } = "";

    /// <summary>
    /// The environment variable holding the MAC key, as 64 hexadecimal characters.
    /// Blank leaves the files without a MAC, and a reader without a key to check.
    /// </summary>
    /// <remarks>
    /// The name of the variable, never the key, for the same reason as the encryption
    /// key - and a different key from that one, because a file can be authenticated
    /// without being encrypted and the other way round.
    ///
    /// What it adds is the one thing encryption does not: a file that was edited after
    /// it was written stops loading. The structural checks cannot do this, because a
    /// fixed-width value accepts every bit pattern, and neither can the cipher, whose
    /// keystream XOR lets a bit be flipped through the ciphertext without a key.
    ///
    /// Turning it on has an order to it. Export the data with a MAC first, then ship
    /// the key in the client - a client that holds a MAC key refuses files that carry
    /// no MAC, which is what stops the check being removed by zeroing sixteen bytes.
    /// </remarks>
    public string MacKeyVariable { get; set; } = "";

    /// <summary>
    /// A file holding the MAC key, as 64 hexadecimal characters. An alternative to
    /// <see cref="MacKeyVariable"/>; naming both is refused.
    /// </summary>
    public string MacKeyFile { get; set; } = "";
}

[TabbitTarget("binary", TargetKind.Export, Order = 10)]
public class BinaryExporter : Target<BinaryRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    private Manifest _manifest = null!;

    /// <summary>The key this entry's files are sealed with, or null when they are not.</summary>
    private byte[]? _key;

    /// <summary>The key this entry's files are signed with, or null when they are not.</summary>
    private byte[]? _macKey;

    /// <summary>
    /// A record group is already expressible: it is one fixed-array column per member.
    /// </summary>
    /// <remarks>
    /// Nothing was added to the format for it. Because the file is column oriented, an
    /// array of records is a struct of arrays - and an array column with the member's
    /// element type is exactly that. So no new kind, no version bump, and the column
    /// encodings keep applying per member, which storing a record as one blob would have
    /// defeated. spec/nested-fields.md has the layout.
    /// </remarks>
    /// <summary>Nothing to opt into: this target generates no lookup.</summary>
    /// <remarks>A key is a column like any other in the file, and the file carries no lookup.</remarks>
    protected override bool SupportsCompositeKeys => true;

    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// Nothing was added for depth either, and for the same reason: the writer walks
    /// <see cref="Models.Table.WireColumns"/>, and one wire column is one leaf of the path
    /// however deep that path is. A record inside a record is the same fixed-array columns at
    /// the same widths - only the name reaching a consumer is longer.
    /// spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// A second bitmap, one bit per element written, in front of a value block no encoding
    /// had to learn about. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, BinaryRecipe binaryRecipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(binaryRecipe.Path))
            return;

        // Before anything is written: a schema change that would break a reader already
        // out there stops the run here, with nothing exported and the reason named.
        if (!string.IsNullOrEmpty(binaryRecipe.SchemaBaseline))
        {
            SchemaBaseline.Check(
                binaryRecipe.SchemaBaseline, context.Model, binaryRecipe.AcceptSchemaChanges);
        }

        // Before the first table is written, so that a missing or malformed key stops the run
        // rather than leaving a directory half in one form and half in the other.
        TcbEnvelope.KeysFor(binaryRecipe, out _key, out _macKey);

        string manifestFilename = Path.Combine(binaryRecipe.Path, "manifest-binary.json");

        _manifest = Manifest.Load(manifestFilename);

        // Before anything is written, while the ledger is still the previous run's: a
        // table renamed or removed leaves its file behind otherwise, and a stale .tcb is
        // worse than a stale source file - it ships, and a build still asking for the old
        // name reads it.
        if (binaryRecipe.Sweep)
            _manifest.PruneStaleFiles(binaryRecipe.Path);

        // Off unless the recipe names a file to put it in. What it measures - deflating every
        // string column, splitting every distinct value - is work worth doing only when
        // somebody is going to read the answer.
        var report = string.IsNullOrEmpty(binaryRecipe.EncodingReport) ? null : new TcbEncodingReport();

        // context.Model is already narrowed to this entry's target side.
        //
        // Planned first, then written at once, then recorded - the same three steps the json
        // export takes and for the same reason: the staging list's order reaches the build
        // cache's seal and the manifest's order is the manifest file, so both are settled
        // here in table order before any thread starts.
        //
        // A run that asked for the encoding report takes the sequential path. The report is a
        // list in the order the columns were measured and it is written to a file, so a run
        // asking for it is asking a question about the encoding rather than for the fastest
        // export. spec/conversion-time.md section 5.
        var planned = report is null ? Plan(binaryRecipe, context.Model) : null;

        if (planned is null)
        {
            foreach (var table in context.Model.Tables)
                ExportTable(binaryRecipe, table, report!);
        }
        else
        {
            System.Threading.Tasks.Parallel.ForEach(planned, job =>
            {
                var writer = Encode(job.Table, job.Set.Rows, null, Spread.Nothing);

                Seal(writer);

                ReadOnlySpan<byte> bytes = writer.WrittenSpan;

                Log.Information($"Exporting binary file '{job.Destination}' ({bytes.Length} bytes)");
                StagingFiles.WriteBytesInto(job.Staged, bytes);
            });

            foreach (var job in planned)
                _manifest.Add(job.Name, job.Staged);
        }

        _manifest.BuildAndWriteToFile(manifestFilename);

        if (report != null)
            WriteEncodingReport(binaryRecipe, report);
    }

    /// <summary>
    /// Writes the encoding report beside the tables, and says where it went.
    /// </summary>
    /// <remarks>
    /// Not through the manifest: the manifest is the ledger of what a consuming build reads,
    /// and sweeping is driven off it. A report is for whoever is deciding what the format
    /// should hold next, so it is written where it was asked for and left alone afterwards.
    /// </remarks>
    private static void WriteEncodingReport(
        BinaryRecipe recipe, TcbEncodingReport report)
    {
        string filename = Path.GetFullPath(recipe.EncodingReport);

        string? directory = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(filename, report.Render());

        Log.Information($"Wrote the encoding report for {report.ColumnCount} columns to '{filename}'");
    }

    /// <summary>
    /// One file per set of rows the table has.
    /// </summary>
    /// <remarks>
    /// A table with one set - which is nearly all of them - yields one file, so this is the
    /// ordinary path rather than a branch around it. The schema is the table's and is written
    /// into each file identically; only the rows differ. spec/table-row-sets.md.
    /// </remarks>
    /// <summary>One file this entry will write, and the staging file it will write it into.</summary>
    private sealed class Job
    {
        public required Table Table { get; init; }
        public required RowSet Set { get; init; }

        /// <summary>File name as the manifest records it, extension included.</summary>
        public required string Name { get; init; }

        /// <summary>Where it will end up, for the log line.</summary>
        public required string Destination { get; init; }

        /// <summary>Where it is written first, claimed in table order.</summary>
        public required string Staged { get; init; }
    }

    /// <summary>
    /// Every file this entry will write, in table order - or null when one of them cannot be
    /// claimed.
    /// </summary>
    /// <remarks>
    /// The claiming is all-or-nothing, and it has to be: a partial claim would leave the
    /// ledger holding files the sequential path is about to claim again, and that path's own
    /// check reads what is already there. <see cref="StagingFiles.ClaimAll"/>.
    /// </remarks>
    private static List<Job>? Plan(BinaryRecipe recipe, Model model)
    {
        var names = new List<(Table Table, RowSet Set, string Name, string Destination)>();

        foreach (var table in model.Tables)
        {
            foreach (var rowSet in table.RowSets)
            {
                string name = table.DataFileName + rowSet.Name + recipe.FileExtension;

                names.Add((
                    table, rowSet, name,
                    Path.GetFullPath(Path.Combine(recipe.Path, name))));
            }
        }

        // All of them or none. What comes back is the staging file for each, in this order -
        // and null means something else has one of these files, which the sequential path
        // reports properly.
        var staged = StagingFiles.ClaimAll(names.ConvertAll(planned => planned.Destination));

        if (staged is null)
            return null;

        var jobs = new List<Job>(names.Count);

        for (int at = 0; at < names.Count; at++)
        {
            jobs.Add(new Job
            {
                Table = names[at].Table,
                Set = names[at].Set,
                Name = names[at].Name,
                Destination = names[at].Destination,
                Staged = staged[at],
            });
        }

        return jobs;
    }

    /// <summary>
    /// Applies the outermost layers to a finished table, in the order a reader undoes them.
    /// </summary>
    /// <remarks>
    /// Encryption then the tag, so a reader can refuse an altered file before it decrypts
    /// one - and so that the nonce the encryption just wrote is covered by the tag. Here
    /// rather than inside the encoder because what they work over is a finished file, and the
    /// encoder stays the one path that both the export and the validation pipeline go through.
    /// </remarks>
    private void Seal(TcbWriter writer)
    {
        if (_key != null)
            TcbEnvelope.Seal(writer.WrittenBytes, _key);

        if (_macKey != null)
            TcbMac.Sign(writer.WrittenBytes, _macKey);
    }

    private void ExportTable(
        BinaryRecipe recipe, Table table, TcbEncodingReport report)
    {
        foreach (var rowSet in table.RowSets)
            ExportRowSet(recipe, table, rowSet, report);
    }

    private void ExportRowSet(
        BinaryRecipe recipe, Table table, RowSet rowSet, TcbEncodingReport report)
    {
        var writer = Encode(table, rowSet.Rows, report);

        string name = table.DataFileName + rowSet.Name;

        var filename = Path.Combine(recipe.Path, name + recipe.FileExtension);
        filename = Path.GetFullPath(filename);

        Seal(writer);

        ReadOnlySpan<byte> bytes = writer.WrittenSpan;

        // A view over the writer's buffer, not a copy: this is the biggest allocation
        // in the export and there is no reason to make it twice.
        Log.Information($"Exporting binary file '{filename}' ({bytes.Length} bytes)");
        string stagingFilename = StagingFiles.WriteAllBytesToFile(filename, bytes);

        _manifest.Add(name + recipe.FileExtension, stagingFilename);
    }

    /// <summary>
    /// One table as the file holds it, in a buffer rather than on disk.
    /// </summary>
    /// <remarks>
    /// Split out from the export because the bytes have a second reader: the validation
    /// pipeline hands them to the generated reader in memory, so a rule sees the tables through
    /// the same code the consuming project will. Sharing this is the whole point - a second
    /// encoder written for validation could differ from this one, and the difference would show
    /// up as a rule passing on data the game reads differently.
    /// </remarks>
    internal static TcbWriter Encode(Table table) => Encode(table, table.Data, null);

    /// <summary>
    /// Whether this call may spread a table's columns across threads.
    /// </summary>
    /// <remarks>
    /// **Off when the caller is already running a table per thread.** Both axes are real -
    /// a project has more tables than a table has columns, and one table encoded on its own
    /// still has columns to spread - but only the outer one should be taken, or every table
    /// pays to set up a fan-out that has no core to run on.
    ///
    /// The export takes the tables; the validation pipeline encodes one table at a time and
    /// takes the columns. spec/conversion-time.md section 5.
    /// </remarks>
    internal enum Spread
    {
        Columns,
        Nothing,
    }

    /// <summary>
    /// The same, recording what every candidate measured into <paramref name="report"/>.
    /// </summary>
    internal static TcbWriter Encode(
        Table table, List<List<Cell>> rows, TcbEncodingReport? report,
        Spread spread = Spread.Columns)
    {
        TcbWriter writer = new TcbWriter();

        // Wire columns, not serial fields. They are the same list for every table written
        // before records existed, and differ for a record group: it stores one column per
        // member, so the file is a struct of arrays where the API is an array of structs.
        var columns = table.WireColumns;

        // Every column is encoded into its own buffer before a byte of the file
        // exists, because the descriptor states each block's encoding and length up
        // front - and which encoding wins is only known once the candidates have
        // actually been written out and measured.
        var blocks = new ColumnBlock[columns.Count];

        // Encoded in parallel, into their own slots.
        //
        // **Every candidate encoding of every column is written out in full and measured**,
        // which is what makes the choice a measurement rather than a guess - and it is the
        // largest piece of work the export does. The columns are independent: each reads the
        // rows and writes one buffer, so nothing here is shared but the reading.
        //
        // The slots are what keeps this a refactoring. A block lands at its column's index
        // whatever order the work finished in, so the file is assembled below exactly as it
        // was before - which is the property the golden trees check.
        //
        // Two things had to be true first, and both are, above: `table.WireColumns` forces
        // this table's lazily built column lists before any thread reads them, and those
        // are the only caches in the model that a column encoder touches.
        if (report is null && spread == Spread.Columns)
        {
            System.Threading.Tasks.Parallel.For(0, columns.Count, at =>
            {
                blocks[at] = EncodeColumn(table, rows, columns[at], report);
            });
        }
        else
        {
            // Sequential for two reasons that arrive separately. A report is a list in the
            // order the columns were measured and it is written to a file, so a run that asks
            // for one is asking a question about the encoding rather than for the fastest
            // export. And a caller already running a table per thread has taken the outer
            // axis - see `Spread`.
            for (int at = 0; at < columns.Count; at++)
                blocks[at] = EncodeColumn(table, rows, columns[at], report);
        }

        // The signature, the version, and the fields the envelope and the MAC fill in later -
        // reserved here so that applying either of those layers moves nothing.
        TcbFormat.WriteHeader(writer);

        writer.WriteCounter32(rows.Count);

        // The descriptors: one per logical column, so the file says what it holds. A
        // reader matches columns by tag rather than position, skips a tag it does not
        // know by the block's byte length, and refuses a wire it cannot read by name -
        // which between them is the whole of what makes schema changes survivable.
        writer.WriteCounter32(columns.Count);

        for (int at = 0; at < columns.Count; at++)
        {
            var column = columns[at];

            writer.WriteCounter32(column.TagCarrier.Tag!.Value);
            writer.Write(TcbFormat.Wire(
                TcbFormat.ElementFor(column), TcbFormat.KindFor(column), TcbFormat.NullableFor(column),
                TcbFormat.ElementNullableFor(column)));
            writer.Write(blocks[at].Encoding);
            writer.Write((uint)blocks[at].Payload.Length);
        }

        // Column-oriented: each column's rows are one contiguous block. That is what
        // lets an unknown column be skipped in a single advance, with no per-type skip
        // logic for the readers to each get subtly wrong.
        for (int at = 0; at < columns.Count; at++)
            writer.Write(blocks[at].Payload.WrittenSpan);

        return writer;
    }

    /// <summary>
    /// One column's data block: the buffer its values were encoded into, and which
    /// encoding that buffer uses - what the descriptor states and the file carries.
    /// </summary>
    private readonly struct ColumnBlock
    {
        public ColumnBlock(byte encoding, TcbWriter payload)
        {
            Encoding = encoding;
            Payload = payload;
        }

        public byte Encoding { get; }
        public TcbWriter Payload { get; }
    }

    /// <summary>
    /// Encodes one column into its own buffer and says which encoding it chose.
    ///
    /// No statistics, no heuristics: every applicable candidate is written out in
    /// full and the smallest kept, ties going to the lowest encoding number. Encode
    /// time is the one resource this format's design does not care about, and a
    /// measured byte count is the one selector that is never wrong. The candidates
    /// and their layouts are spec/tcb-v102-column-encoding.md.
    /// </summary>
    private static ColumnBlock EncodeColumn(
        Table table, List<List<Cell>> rows, WireColumn column, TcbEncodingReport? report)
    {
        var raw = new TcbWriter();

        foreach (var row in rows)
        {
            // A group whose length the row decides: a record member in a trimming table, or
            // a serial array in one. The count comes from how many of the group's columns the
            // row filled in, not from anything in a single cell, and every member of a record
            // group writes the same one.
            //
            // Ahead of the delimited-cell case because both are variable-length and only this
            // one gets its length from the group rather than from its own cell.
            if (column.IsVariableLengthArray && !column.Group.IsVariableLengthArray)
            {
                int elements = table.ElementCountIn(column.Group, row);

                raw.WriteCounter32(elements);

                for (int element = 0; element < elements; element++)
                {
                    var field = column.Cells[element];
                    ExportValue(raw, row[field.Index].Value!, field);
                }

                continue;
            }

            if (column.IsVariableLengthArray)
            {
                ExportArrayValue(raw, row[column.TagCarrier.Index].Value!, column.TagCarrier);
                continue;
            }

            // Every array states its length per row since v107, and the raw layout has to
            // say it as plainly as the encoded one does. A folded group's is its column
            // count, the same for every row - and it is still written every row, because
            // the descriptor no longer has a place to say it once.
            // spec/tcb-v107-dynamic-arrays.md.
            if (column.IsArray)
                raw.WriteCounter32(column.Cells.Count);

            foreach (var field in column.Cells)
                ExportValue(raw, row[field.Index].Value!, field);
        }

        var (lengths, values) = CollectElements(table, rows, column);

        var stream = BuildStream(column, values);
        var kind = TcbFormat.KindFor(column);

        TcbColumnEncoder.Selection selection;

        if (kind == TcbFormat.KindScalar)
        {
            selection = TcbColumnEncoder.Choose(stream);
        }
        else
        {
            // An array's block holds two streams - how long each row is, and the elements
            // themselves - so it is one candidate against raw rather than a list of them.
            // Which encoding each of those streams uses is chosen inside it, by the same
            // measurement, and named in the block.
            selection = new TcbColumnEncoder.Selection(raw);

            // Not for uuid. An array reaches the encodings by having its elements read
            // through the same cursor a scalar column's are, and a uuid is the one element
            // that has no cursor to read it through - it has no encoding of its own either,
            // so a column of them would gain only what its row lengths compress to.
            if (stream.Element != TcbFormat.ElementUuid)
            {
                selection.Offer(
                    TcbFormat.EncodingArray,
                    TcbColumnEncoder.EncodeArray(stream, lengths.ToArray()));
            }
        }

        var chosen = WithPresence(table, rows, column,
            new ColumnBlock(selection.Best.Encoding, selection.Best.Payload));

        report?.Add(new TcbEncodingReport.ColumnEntry
        {
            Table = table.Name,
            Column = column.Name,
            Element = TcbFormat.ElementFor(column),
            Kind = TcbFormat.KindFor(column),
            Nullable = TcbFormat.NullableFor(column),
            Rows = rows.Count,
            Encoding = chosen.Encoding,
            Bytes = chosen.Payload.Length,
            Candidates = selection.Measured,

            // What a general compressor would still find in the block the encodings chose.
            // The header reserves a flag for a compression layer, and this is the number
            // that says whether carrying a decompressor into every reader runtime would buy
            // more than another encoding would.
            DeflatedBytes = report == null
                ? 0
                : TcbStringStructure.Deflate(chosen.Payload.WrittenSpan),

            // Only for strings, and only when a report was asked for: the structural
            // measurement deflates the values and splits every one of them, which is work
            // no export should be doing when nobody is going to read the result.
            Structure = stream.Strings == null
                ? null
                : TcbStringStructure.Measure(Distinct(stream.Strings!)),
            Layers = report == null ? null : MeasureLayers(table, rows, column),

            // What the presence bitmap would come to if it were encoded rather than left
            // raw. v103 decided to leave it raw and said the bitmap of a column whose
            // presence varies is close to incompressible - which was a judgement, never a
            // measurement. This is the measurement.
            PresenceEncodedBytes = report == null ? 0 : MeasurePresence(table, rows, column),
        });

        return chosen;
    }

    /// <summary>
    /// What a column would cost if its values were encoded per element rather than left raw.
    /// </summary>
    /// <remarks>
    /// Every encoding in the format applies to a scalar column only. An array's rows differ in
    /// length as well as in value, so the block would have to hold two streams - how long each
    /// row is, and the elements themselves - and that second dimension was measured as 1.8
    /// percent of one dataset's bytes and left out of the format on that basis.
    ///
    /// This is the same measurement, taken again wherever it is asked for, because the figure
    /// is a property of the data and not of the format. It splits the block the way an
    /// encoded array would - lengths in one stream, elements flattened into another - and runs
    /// the real encoders over each, so what it reports is what such a block would come to and
    /// not an estimate of it.
    ///
    /// It also asks, of a float column, how many of its values are whole numbers. Design data
    /// carries counts and identifiers through spreadsheet cells that are floating point
    /// because a spreadsheet has no other number, and eight bytes for a value of 3 is the
    /// largest single thing a measurement can find.
    /// </remarks>
    private static TcbEncodingReport.LayerEntry MeasureLayers(
        Table table, List<List<Cell>> rows, WireColumn column)
    {
        var (lengths, values) = CollectElements(table, rows, column);

        byte element = TcbFormat.ElementFor(column);
        bool varying = TcbFormat.KindFor(column) == TcbFormat.KindArray;

        // A row's own length is a small ascending-ish integer stream, which is exactly what
        // the integer encodings are for.
        int lengthBytes = 0;

        if (varying)
        {
            var counts = lengths.ToArray();

            lengthBytes = Math.Min(
                TcbColumnEncoder.Varint(counts).Length,
                Math.Min(TcbColumnEncoder.Rle(counts).Length, TcbColumnEncoder.DeltaRle(counts).Length));
        }

        var entry = new TcbEncodingReport.LayerEntry
        {
            Elements = values.Count,
            LengthBytes = lengthBytes,
        };

        switch (element)
        {
            case TcbFormat.ElementF32:
            case TcbFormat.ElementF64:
            {
                int width = element == TcbFormat.ElementF32 ? 4 : 8;
                var raw = new byte[values.Count][];

                var whole = new List<int>(values.Count);
                bool allWhole = true;

                for (int at = 0; at < values.Count; at++)
                {
                    double number = element == TcbFormat.ElementF32
                        ? (float)values[at]
                        : (double)values[at];

                    var scratch = new TcbWriter();

                    if (element == TcbFormat.ElementF32)
                        scratch.Write((float)values[at]);
                    else
                        scratch.Write((double)values[at]);

                    raw[at] = scratch.WrittenSpan.ToArray();

                    // Whole and within an int32, which is what a counter32 carries. A value
                    // outside that range would need a wider integer in the layout, and
                    // identifiers that large are not what this is looking for.
                    if (allWhole && number == Math.Floor(number)
                        && number >= int.MinValue && number <= int.MaxValue)
                    {
                        whole.Add((int)number);
                    }
                    else
                    {
                        allWhole = false;
                    }
                }

                entry.RawBytes = values.Count * width;
                entry.ElementBytes = Math.Min(
                    entry.RawBytes,
                    Math.Min(TcbColumnEncoder.ValueDictionary(raw, false).Length, TcbColumnEncoder.ValueDictionary(raw, true).Length));

                entry.WholeNumbers = allWhole;

                if (allWhole)
                {
                    var counts = whole.ToArray();

                    entry.WholeBytes = Math.Min(
                        TcbColumnEncoder.Varint(counts).Length,
                        Math.Min(TcbColumnEncoder.Rle(counts).Length, TcbColumnEncoder.DeltaRle(counts).Length));
                }

                break;
            }

            case TcbFormat.ElementString:
            {
                var strings = new string[values.Count];

                for (int at = 0; at < values.Count; at++)
                    strings[at] = (string)values[at] ?? string.Empty;

                var plain = new TcbWriter();
                foreach (string value in strings)
                    plain.Write(value);

                entry.RawBytes = plain.Length;
                entry.ElementBytes = Math.Min(
                    entry.RawBytes,
                    Math.Min(TcbColumnEncoder.Dictionary(strings, false).Length, TcbColumnEncoder.DictionaryFront(strings, true).Length));

                break;
            }

            case TcbFormat.ElementI32:
            case TcbFormat.ElementVarint:
            {
                var numbers = new int[values.Count];

                for (int at = 0; at < values.Count; at++)
                    numbers[at] = (int)values[at];

                entry.RawBytes = element == TcbFormat.ElementI32
                    ? values.Count * 4
                    : TcbColumnEncoder.Varint(numbers).Length;

                entry.ElementBytes = Math.Min(
                    entry.RawBytes,
                    Math.Min(
                        TcbColumnEncoder.Varint(numbers).Length,
                        Math.Min(TcbColumnEncoder.Rle(numbers).Length, TcbColumnEncoder.DeltaRle(numbers).Length)));

                break;
            }

            case TcbFormat.ElementBool:
            {
                var numbers = new int[values.Count];

                for (int at = 0; at < values.Count; at++)
                    numbers[at] = (bool)values[at] ? 1 : 0;

                entry.RawBytes = values.Count;
                entry.ElementBytes = Math.Min(entry.RawBytes, TcbColumnEncoder.Rle(numbers).Length);

                break;
            }

            case TcbFormat.ElementI64:
            {
                var raw = new byte[values.Count][];

                for (int at = 0; at < values.Count; at++)
                {
                    var scratch = new TcbWriter();
                    ExportValue(scratch, values[at], column.TagCarrier);
                    raw[at] = scratch.WrittenSpan.ToArray();
                }

                entry.RawBytes = values.Count * 8;
                entry.ElementBytes = Math.Min(
                    entry.RawBytes,
                    Math.Min(
                        TcbColumnEncoder.ValueDictionary(raw, false).Length,
                        TcbColumnEncoder.ValueDictionary(raw, true).Length));

                break;
            }

            default:
            {
                // The elements that no encoding applies to, counted so the report's totals
                // still add up to the file.
                entry.RawBytes = entry.ElementBytes = 0;
                break;
            }
        }

        // What the bit-width layout comes to for this column, whether or not it won. The
        // report is what says which columns it reaches and at what width, and it calls the
        // same code the encoder does so the two cannot drift apart.
        var integers = IntegersOf(element, values);

        if (integers is not null)
        {
            var packed = TcbColumnEncoder.Bitpack(integers);

            entry.BitpackApplies = packed.Applies;
            entry.BitpackWidth = packed.Width;
            entry.BitpackBase = packed.Base;
            entry.BitpackBytes = packed.BestBytes;
            entry.BitpackInner = packed.BestInner;
        }

        return entry;
    }

    /// <summary>
    /// What a nullable column's presence bitmap would cost if it were encoded.
    /// </summary>
    /// <remarks>
    /// The bitmap **is** a bit-packed boolean column of width one, so it is measured by the
    /// same call the value blocks use - which also means the two cannot answer differently
    /// about the same bits.
    ///
    /// Zero for a required column, which has no bitmap to encode.
    /// </remarks>
    private static int MeasurePresence(Table table, List<List<Cell>> rows, WireColumn column)
    {
        if (!TcbFormat.NullableFor(column))
            return 0;

        // The encoding byte and the bitmap, which is what the block now holds.
        return 1 + TcbColumnEncoder.EncodeByteStream(PresenceBits(table, rows, column)).Payload.Length;
    }

    /// <summary>
    /// A column's elements as 64-bit integers, for the element types that are integers.
    /// </summary>
    /// <remarks>
    /// Ticks for the two time types, because that is what the wire already carries them as -
    /// a duration packed by how much it varies is the same measurement as any other integer
    /// column, and the value is not reinterpreted by asking.
    /// </remarks>
    private static long[]? IntegersOf(byte element, List<object> values)
    {
        var result = new long[values.Count];

        switch (element)
        {
            case TcbFormat.ElementBool:
                for (int at = 0; at < values.Count; at++)
                    result[at] = (bool)values[at] ? 1 : 0;

                return result;

            case TcbFormat.ElementVarint:
            case TcbFormat.ElementI32:
                for (int at = 0; at < values.Count; at++)
                    result[at] = (int)values[at];

                return result;

            case TcbFormat.ElementI64:
                for (int at = 0; at < values.Count; at++)
                {
                    result[at] = values[at] switch
                    {
                        long number => number,
                        DateTime moment => moment.Ticks,
                        TimeSpan duration => duration.Ticks,
                        _ => 0,
                    };
                }

                return result;

            default:
                return null;
        }
    }

    /// <summary>
    /// Every element of a column, flattened, and how many of them each row held.
    /// </summary>
    /// <remarks>
    /// The same three cases the raw block is written from, so the flattening cannot disagree
    /// with what the file holds about which values belong to which row.
    /// </remarks>
    private static (List<int> Lengths, List<object> Values) CollectElements(
        Table table, List<List<Cell>> rows, WireColumn column)
    {
        var lengths = new List<int>(rows.Count);
        var values = new List<object>();

        foreach (var row in rows)
        {
            if (column.IsVariableLengthArray && !column.Group.IsVariableLengthArray)
            {
                int elements = table.ElementCountIn(column.Group, row);
                lengths.Add(elements);

                for (int element = 0; element < elements; element++)
                    values.Add(row[column.Cells[element].Index].Value!);

                continue;
            }

            if (column.IsVariableLengthArray)
            {
                var array = (System.Array)row[column.TagCarrier.Index].Value!;
                int length = array?.Length ?? 0;

                lengths.Add(length);

                for (int at = 0; at < length; at++)
                    values.Add(array!.GetValue(at)!);

                continue;
            }

            lengths.Add(column.Cells.Count);

            foreach (var field in column.Cells)
                values.Add(row[field.Index].Value!);
        }

        return (lengths, values);
    }

    /// <summary>
    /// A column's flattened values in the forms the candidates need them.
    /// </summary>
    /// <remarks>
    /// The unencoded stream is built first, through the same call the raw block is written
    /// with, and the fixed-width values are sliced back out of it. That is deliberate: a
    /// dictionary entry is by construction the same bytes a raw block would have held - a
    /// float's exact bit pattern, a date's ticks - and there is no second encoding path for
    /// it to disagree with.
    ///
    /// The tag carrier types every value. All of a wire column's cells are the same member
    /// at different positions, so they share an element type; using the first is what the
    /// raw path does as well.
    /// </remarks>
    private static TcbColumnEncoder.Stream BuildStream(WireColumn column, List<object> values)
    {
        byte element = TcbFormat.ElementFor(column);
        var field = column.TagCarrier;

        var raw = new TcbWriter();

        foreach (var value in values)
            ExportValue(raw, value, field);

        var stream = new TcbColumnEncoder.Stream
        {
            Element = element,
            Count = values.Count,
            Raw = raw,

            // Every element that is an integer, widened to the one type the bit-width
            // candidate works in. Null for the rest, which is what tells it not to apply.
            Longs = IntegersOf(element, values),
        };

        switch (element)
        {
            case TcbFormat.ElementI32:
            case TcbFormat.ElementVarint:
            {
                var integers = new int[values.Count];

                for (int at = 0; at < values.Count; at++)
                    integers[at] = (int)values[at];

                return stream with { Integers = integers };
            }

            case TcbFormat.ElementBool:
            {
                var integers = new int[values.Count];

                for (int at = 0; at < values.Count; at++)
                    integers[at] = (bool)values[at] ? 1 : 0;

                return stream with { Integers = integers };
            }

            case TcbFormat.ElementString:
            {
                var strings = new string[values.Count];

                for (int at = 0; at < values.Count; at++)
                    strings[at] = (string)values[at] ?? string.Empty;

                return stream with { Strings = strings };
            }

            case TcbFormat.ElementF32:
            case TcbFormat.ElementF64:
            {
                bool single = element == TcbFormat.ElementF32;
                var numbers = new double[values.Count];

                for (int at = 0; at < values.Count; at++)
                    numbers[at] = single ? (float)values[at] : (double)values[at];

                return stream with
                {
                    Fixed = Slice(raw, values.Count, single ? 4 : 8),
                    Numbers = numbers,
                };
            }

            case TcbFormat.ElementI64:
                return stream with { Fixed = Slice(raw, values.Count, 8) };

            default:
                return stream;
        }
    }

    /// <summary>Each value's own bytes, taken back out of the stream they were written into.</summary>
    private static byte[][] Slice(TcbWriter raw, int count, int width)
    {
        var span = raw.WrittenSpan;
        var values = new byte[count][];

        for (int at = 0; at < count; at++)
            values[at] = span.Slice(at * width, width).ToArray();

        return values;
    }

    /// <summary>The distinct values in first-appearance order, so the measurement is stable.</summary>
    private static List<string> Distinct(string[] values)
    {
        var seen = new HashSet<string>();
        var distinct = new List<string>();

        foreach (string value in values)
        {
            if (seen.Add(value))
                distinct.Add(value);
        }

        return distinct;
    }

    /// <summary>
    /// Puts the presence bitmap ahead of a nullable column's block, and leaves any other
    /// column exactly as it was.
    /// </summary>
    /// <remarks>
    /// **After** the encoding is chosen and in front of what it produced. Two consequences,
    /// both intended: the nine encodings never see the bitmap, so none of them had to learn
    /// about it; and the values are still written for every row, so the decode of each
    /// encoding is untouched. A row without a value carries the type's empty one, which
    /// costs bytes a compacted layout would not - and buys not rewriting nine decode paths
    /// in every language to count only the rows that are present.
    ///
    /// One bit per row, low bit first, padded to a byte. Raw, because the bitmap of a column
    /// where presence varies is close to incompressible and the bitmap of one where it does
    /// not should not have been written at all.
    /// </remarks>
    private static ColumnBlock WithPresence(
        Table table, List<List<Cell>> rows, WireColumn column, ColumnBlock block)
    {
        bool rowBitmap = TcbFormat.NullableFor(column);
        bool elementBitmap = TcbFormat.ElementNullableFor(column);

        if (!rowBitmap && !elementBitmap)
            return block;

        var payload = new TcbWriter();

        if (rowBitmap)
        {
            var (encoding, bitmap) = TcbColumnEncoder.EncodeByteStream(PresenceBits(table, rows, column));

            payload.Write(encoding);
            payload.Write(bitmap.WrittenSpan);
        }

        // After the row bitmap and before the values, which is the order a reader meets them
        // in: whether a row has an array at all, then which of that array's places hold a
        // value, then the values. spec/nullable-array-elements.md.
        if (elementBitmap)
        {
            var bits = ElementPresenceBits(table, rows, column, out int elements);
            var (encoding, bitmap) = TcbColumnEncoder.EncodeByteStream(bits);

            // How many bits the bitmap holds, ahead of it. A variable-length column's total
            // is the sum of its row lengths, and those lengths are inside the value block -
            // behind the bitmap - so a reader that met the bitmap first could not size it.
            // Five bytes at most, once per column. spec/nullable-array-elements.md.
            payload.WriteCounter32(elements);

            payload.Write(encoding);
            payload.Write(bitmap.WrittenSpan);
        }

        payload.Write(block.Payload.WrittenSpan);

        return new ColumnBlock(block.Encoding, payload);
    }

    /// <summary>One bit per row saying whether that row has a value, low bit first.</summary>
    private static byte[] PresenceBits(Table table, List<List<Cell>> rows, WireColumn column)
    {
        int rowCount = rows.Count;
        var bitmap = new byte[(rowCount + 7) / 8];

        for (int at = 0; at < rowCount; at++)
        {
            if (rows[at][column.TagCarrier.Index].HasValue)
                bitmap[at >> 3] |= (byte)(1 << (at & 7));
        }

        return bitmap;
    }


    /// <summary>
    /// One bit per element written, low bit first, in the order the value block wrote them.
    /// </summary>
    /// <remarks>
    /// As long as the elements the block actually holds rather than as long as the columns:
    /// a variable-length row writes its own count and an absent array writes none, so a
    /// reader accumulates as it walks the rows it is already walking.
    ///
    /// The three branches are the value writer's three, and they are here rather than shared
    /// with it because the two answer different questions about the same walk - what to write
    /// and whether the sheet wrote it.
    /// </remarks>
    private static byte[] ElementPresenceBits(
        Table table, List<List<Cell>> rows, WireColumn column, out int elements)
    {
        var bits = new List<bool>();

        foreach (var row in rows)
        {
            // A group whose length the row decides. Every element is a column of its own, so
            // its own cell answers.
            if (column.IsVariableLengthArray && !column.Group.IsVariableLengthArray)
            {
                int filled = table.ElementCountIn(column.Group, row);

                for (int at = 0; at < filled; at++)
                    bits.Add(row[column.Cells[at].Index].HasValue);

                continue;
            }

            // A delimited cell, where the elements and their presence are both inside one
            // cell. A row whose array is absent wrote no elements and therefore no bits.
            if (column.IsVariableLengthArray)
            {
                var cell = row[column.TagCarrier.Index];
                int length = (cell.Value as System.Array)?.Length ?? 0;

                for (int at = 0; at < length; at++)
                {
                    bits.Add(cell.ElementHasValue is not { } present
                        || at >= present.Length
                        || present[at]);
                }

                continue;
            }

            foreach (var field in column.Cells)
                bits.Add(row[field.Index].HasValue);
        }

        elements = bits.Count;

        var bitmap = new byte[(bits.Count + 7) / 8];

        for (int at = 0; at < bits.Count; at++)
        {
            if (bits[at])
                bitmap[at >> 3] |= (byte)(1 << (at & 7));
        }

        return bitmap;
    }

    /// <summary>
    /// Writes a delimited array cell: element count first, then the elements.
    /// </summary>
    private static void ExportArrayValue(TcbWriter writer, object? value, Field field)
    {
        var elements = (System.Array)value!;
        int length = elements?.Length ?? 0;

        writer.WriteCounter32(length);

        for (int i = 0; i < length; i++)
            ExportValue(writer, elements!.GetValue(i), field);
    }

    private static void ExportValue(TcbWriter writer, object? value, Field field)
    {
        // Element type, so the same switch serves a scalar field and one element
        // of an array field.
        Models.ValueType valueType = field.ElementType;

        // A reference is stored as the target's primary index, so what travels is that
        // key's type rather than the record type the field presents. `int32` used to be
        // written here as a constant, which is why a table keyed by anything else could
        // not be pointed at. spec/reference-key-types.md.
        if (field.IsRef)
            valueType = field.RefKeyType;

        switch (valueType)
        {
            case Models.ValueType.String:
                writer.Write((string)value!);
                break;
            case Models.ValueType.Bool:
                writer.Write((bool)value!);
                break;
            case Models.ValueType.Int32:
                writer.Write((int)value!);
                break;
            case Models.ValueType.Int64:
                writer.Write((long)value!);
                break;
            case Models.ValueType.Float:
                writer.Write((float)value!);
                break;
            case Models.ValueType.Double:
                writer.Write((double)value!);
                break;
            case Models.ValueType.DateTime:
                writer.Write((DateTime)value!);
                break;
            case Models.ValueType.TimeSpan:
                writer.Write((TimeSpan)value!);
                break;
            case Models.ValueType.Uuid:
                writer.Write((Guid)value!);
                break;
            case Models.ValueType.Enum:
                writer.WriteOptimalInt32((int)value!);
                break;
            case Models.ValueType.ForeignRecord:
                writer.Write((int)value!);
                break;
            default:
                throw new TabbitDefectException($"unsupported type  `{valueType}`");
        }
    }
}
