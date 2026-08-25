using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Targets;

namespace Tabbit.CodeGeneration;

/// <summary>
/// What every code-generation target does the same way.
///
/// Two methods, and both used to be copied into each generator. Neither is interesting,
/// which is the point: thirteen copies of an uninteresting method are thirteen places for
/// one of them to drift, and nothing reports it. The Unreal generator's copy of the reader
/// writer pointed at the C++ reader for months, which is how it came to ship an Unreal
/// module full of std::string.
///
/// What is genuinely per-language stays in the generator: the file layout, the type names,
/// the escaping, the reader calls. This is only the plumbing they share.
///
/// Three generators keep their own <c>CommentLines</c> because theirs is not this one, and
/// they are worth naming so nobody folds them in later on the strength of the signature:
///
///   TypeScript wraps the whole comment in `/** ... */` and runs its lines together.
///
///   Python maps each line through its own doc escaping.
///
///   C# tests <c>IsNullOrEmpty</c> rather than <c>IsNullOrWhiteSpace</c>, so a comment of
///   nothing but spaces reaches its template as one blank line instead of none.
///
/// The last of those is a difference of two words and shows up as one blank line in one
/// generated file. Which is the reason this list exists rather than a note saying the
/// methods are all the same.
/// </summary>
public abstract class CodeGenerator<TRecipe> : Target<TRecipe>
    where TRecipe : class, IOutputRecipe
{
    /// <summary>
    /// What a reference column's resolved row is called, in the model's own casing.
    /// </summary>
    /// <remarks>
    /// **The column's name belongs to the key**, which is what the cell holds; the row is
    /// something this tool linked after loading and it takes a derived name. Derived rather
    /// than shortened: every short form has to leave the column's name out, and leaving it
    /// out is what makes two names collide - a table with a `mail` column and a `mailId`
    /// reference has two things wanting to be called `Mail`.
    ///
    /// The target comes first because that is what a reader browsing a record looks for,
    /// and it is the form the several-target accessors used before those went away.
    ///
    /// Each generator hands the result through its own casing pass, so `MailByMailId` is
    /// `mail_by_mail_id` where that is the language's spelling.
    /// spec/reference-surface-naming.md section 5.
    /// </remarks>
    protected static string RowAccessorName(string target, string column)
        => target.ToPascalCase() + "By" + column.ToPascalCase();

    /// <summary>
    /// Whether a reference hands back a row, which is what the naming rule is about.
    /// </summary>
    /// <remarks>
    /// A dotted reference (`foreign Item.Name`) hands back a value out of the target rather
    /// than the row, so the column's name stays on that value and the key keeps the name it
    /// had. There is no row for a derived name to belong to.
    /// spec/reference-surface-naming.md section 9.
    /// </remarks>
    protected static bool ResolvesToRow(Models.Field field)
        => field.ResolvedRefField is null;

    /// <summary>
    /// A sheet comment split into the lines a comment block needs.
    ///
    /// Line endings are normalized because a comment typed into Excel on Windows carries
    /// CRLF, and a template emitting it verbatim after a `//` leaves a stray blank line in
    /// the generated file.
    /// </summary>
    protected static IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// Writes the language's binary reader beside the generated code.
    /// </summary>
    /// <remarks>
    /// From an embedded resource rather than from `lib/` on disk, so what a published
    /// build writes cannot differ from what is committed - and so a generated output tree
    /// is self-contained: nothing to install, no include path to set, and no chance of a
    /// consumer pairing generated code with a reader of a different vintage.
    /// </remarks>
    /// <param name="resourceName">Logical name, as Tabbit.csproj declares it.</param>
    /// <param name="path">Where to write it. Made absolute here.</param>
    /// <summary>
    /// Whether this target's files are written with a UTF-8 byte order mark.
    /// </summary>
    /// <remarks>
    /// False everywhere but the three that MSVC compiles. StagingFiles explains why those
    /// three need it: without a mark, MSVC reads a source file in the system codepage, and
    /// a Korean comment carried over from a sheet can end in a byte that codepage reads as
    /// a backslash - which continues the comment over the declaration below it.
    /// </remarks>
    protected virtual bool WritesByteOrderMark => false;

    protected void WriteBinaryReaderRuntime(string resourceName, string path)
    {
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
            throw new TabbitDefectException($"Embedded resource `{resourceName}` is missing from the build.");

        using var reader = new StreamReader(stream);

        Emit(Path.GetFullPath(path), Marked(reader.ReadToEnd(), path), WritesByteOrderMark);
    }

    /// <summary>
    /// Whether this generator's files go straight to disk instead of through the staging
    /// area.
    /// </summary>
    /// <remarks>
    /// Off for every recipe entry, and that is what makes a failed run leave the previous
    /// output alone: files are gathered and moved into place only once everything has
    /// succeeded.
    ///
    /// On for one caller - the validation pipeline, which generates an accessor of its own to
    /// compile the rules against. That output is not what the run produces; it is scratch, it
    /// is rewritten every run, and it has to exist as real files before the run is anywhere
    /// near succeeding, because the validation that decides whether it succeeds is what reads
    /// it. Staging it would also put it in the commit, which would publish a folder no recipe
    /// asked for.
    /// </remarks>
    internal bool WritesWithoutStaging { get; set; }

    /// <summary>Writes one generated file, through the staging area unless told otherwise.</summary>
    protected void Emit(string filename, string text, bool withByteOrderMark = false)
    {
        if (!WritesWithoutStaging)
        {
            StagingFiles.WriteAllTextToFile(filename, text, withByteOrderMark);
            return;
        }

        FileHelper.EnsurePathExists(filename);
        File.WriteAllText(filename, text, new System.Text.UTF8Encoding(withByteOrderMark));
    }

    /// <summary>
    /// Writes one generated file that is not text.
    /// </summary>
    /// <remarks>
    /// For a target that emits an assembly. The sweep recognises its own output by a marker in the
    /// first bytes of a file, which an assembly cannot carry - so this registers the file with the
    /// staging area the way the binary exporter does, and the sweep leaves it alone because it was
    /// written rather than found.
    /// </remarks>
    protected void EmitBytes(string filename, byte[] bytes)
    {
        if (!WritesWithoutStaging)
        {
            StagingFiles.WriteAllBytesToFile(filename, bytes);
            return;
        }

        FileHelper.EnsurePathExists(filename);
        File.WriteAllBytes(filename, bytes);
    }

    /// <summary>
    /// Puts the sweep's marker at the top of a runtime file.
    /// </summary>
    /// <remarks>
    /// Runtime files are copied out of `lib/` verbatim, and until now that meant they
    /// arrived without the `Generated by Tabbit` header every other emitted file
    /// carries. The marker is what gives the sweep permission to remove a file, so
    /// without it a runtime file this tool stopped writing stayed in the consumer's
    /// project forever - which is exactly what happened when `tcb_reader.ts`
    /// was renamed to `tcb-reader.ts` and both were left sitting there.
    ///
    /// Added here rather than in `lib/`, because the file in `lib/` is not generated. It
    /// is the source, it is edited and reviewed, and a header claiming otherwise would
    /// be a lie told to every person who opens it.
    /// </remarks>
    private static string Marked(string contents, string path)
    {
        // Nothing in `lib/` should already say it - those files are source, and one that
        // claimed otherwise would both lie to its readers and stack a second banner here.
        // `GeneratedFileMarkerTests` is what keeps that true.
        if (GeneratedFileMarker.HeadIsMarked(contents))
            return contents;

        string extension = Path.GetExtension(path).ToLowerInvariant();
        string comment = extension switch
        {
            ".py" or ".rb" => "#",
            ".lua" => "--",
            _ => "//",
        };

        // Two toolchains read the header rather than merely display it, so the claim is
        // spelled the way each of them recognizes:
        //
        //   Go     - gofmt, vet and the linters skip a file whose header matches
        //            `^// Code generated .* DO NOT EDIT\.$` before the package clause.
        //   C#     - Roslyn analysers and formatters skip a file opening with an
        //            `<auto-generated>` block, which is what every C# file this tool
        //            emits already carries. A runtime file without it would be the one
        //            generated file a consumer's analysers complained about.
        //
        // The sweep finds its phrase inside all three, because that match ignores case.
        string[] claim = extension switch
        {
            ".go" => ["Code generated by Tabbit. DO NOT EDIT."],
            ".cs" =>
            [
                "<auto-generated>",
                "    THIS CODE WAS GENERATED BY Tabbit. DO NOT EDIT.",
                "</auto-generated>",
            ],
            _ => [$"Written here by Tabbit. {GeneratedFileMarker.TextWithWarning}"],
        };

        var lines = new List<string>
        {
            $"{comment} ------------------------------------------------------------------------------",
        };

        lines.AddRange(claim.Select(line => $"{comment} {line}"));

        lines.AddRange(
        [
            $"{comment}",
            $"{comment} The runtime, copied in beside the generated code so the output is",
            $"{comment} self-contained. Edit it in the Tabbit repository, not here: this file is",
            $"{comment} rewritten on every run, and the header above is what lets a later run remove",
            $"{comment} it if it ever moves.",
            $"{comment} ------------------------------------------------------------------------------",
            "",
            "",
        ]);

        string banner = string.Join("\n", lines);

        // Two first lines cannot have anything put in front of them: `<?php`, because
        // PHP prints whatever precedes it, and Ruby's `frozen_string_literal`, which
        // stops being a magic comment if it is not the first one. The banner goes after
        // either, which the sweep's marker window is wide enough to still see.
        bool afterFirstLine =
            contents.StartsWith("<?php", StringComparison.Ordinal)
            || contents.StartsWith("# frozen_string_literal:", StringComparison.Ordinal);

        if (afterFirstLine)
        {
            int firstBreak = contents.IndexOf('\n');

            if (firstBreak >= 0)
            {
                return contents.Substring(0, firstBreak + 1)
                       + "\n" + banner
                       + contents.Substring(firstBreak + 1).TrimStart('\n');
            }
        }

        return banner + contents;
    }

    /// <summary>
    /// Asks for the generated files this run did not write to be removed from
    /// <paramref name="directory"/> once the run commits.
    /// </summary>
    /// <remarks>
    /// Every target that writes a file per table needs this, and a target that writes one
    /// file wants it too the day it stops: delete a table from the sheets and its file
    /// stays behind, naming types nothing declares any more.
    ///
    /// Only files carrying this tool's own header are removed, so a target pointed at a
    /// directory holding somebody's own source cannot delete any of it. Which is why this
    /// is on by default and <c>Sweep: false</c> in a recipe entry turns it off - the option
    /// exists for a consumer who edits the output, and editing generated files is a
    /// decision that deserves a line in a recipe.
    /// </remarks>
    protected static void SweepStaleOutput(string directory, bool sweep)
    {
        if (sweep && !string.IsNullOrEmpty(directory))
            StagingFiles.SweepDirectory(directory);
    }
}
