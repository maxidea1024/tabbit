using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Serilog;
using Tabbit.Recipe;

namespace Tabbit.History;

/// <summary>Where a snapshot's commit identity came from.</summary>
public enum CommitOrigin
{
    /// <summary>Nothing identified this conversion. The history cannot record it.</summary>
    None,

    /// <summary>Given on the command line, which is what a CI job does.</summary>
    CommandLine,

    /// <summary>Read from the working copy the sheets are in.</summary>
    Git,
}

/// <summary>
/// What identifies one conversion in the history: which commit it was of, and who made
/// that commit.
///
/// This is the who-and-when half of the feature, and it has a ceiling worth being clear
/// about. Workbooks are zip archives, so git cannot attribute a cell to a person the
/// way it attributes a line of code. The commit is as fine as attribution gets, and a
/// commit touching two designers' work names only its author.
///
/// Everything here can be supplied explicitly, because not every build runs inside a
/// git checkout - and the alternative to accepting what a CI job knows is recording
/// nothing at all.
/// </summary>
public sealed class CommitInfo
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    private CommitInfo(
        string? hash,
        string? branch,
        string? authorName,
        string? authorEmail,
        DateTimeOffset? committedAt,
        string? subject,
        bool isDirty,
        CommitOrigin origin,
        string? repositoryPath)
    {
        Hash = hash;
        Branch = branch;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        CommittedAt = committedAt;
        Subject = subject;
        IsDirty = isDirty;
        Origin = origin;
        RepositoryPath = repositoryPath;
    }

    /// <summary>
    /// The commit this conversion is of, or null when nothing identified it.
    ///
    /// Not necessarily a git hash: a project keeping its sheets somewhere without
    /// commits can pass any stable identifier, and the history treats it as opaque.
    /// </summary>
    public string? Hash { get; }

    /// <summary>First twelve characters of <see cref="Hash"/>, for display.</summary>
    public string? ShortHash => Hash is null ? null : Hash.Substring(0, Math.Min(12, Hash.Length));

    /// <summary>
    /// Branch this snapshot belongs to, or null when there is none.
    ///
    /// Snapshots are chained per branch, so this decides which history a conversion
    /// extends. A detached HEAD - what most CI checkouts produce - yields null unless
    /// the job passes `--branch`.
    /// </summary>
    public string? Branch { get; }

    public string? AuthorName { get; }

    public string? AuthorEmail { get; }

    public DateTimeOffset? CommittedAt { get; }

    /// <summary>First line of the commit message.</summary>
    public string? Subject { get; }

    /// <summary>
    /// Whether the working tree held changes the commit does not describe.
    ///
    /// A dirty snapshot is still worth recording - a designer wants to see what their
    /// unpushed edit did - but it is not attributable, and the report has to say so
    /// rather than crediting the edits to the last committer.
    /// </summary>
    public bool IsDirty { get; }

    public CommitOrigin Origin { get; }

    /// <summary>Directory git was asked about, or null when none was found.</summary>
    public string? RepositoryPath { get; }

    /// <summary>Whether this identifies a conversion well enough to record it.</summary>
    public bool IsIdentified => Hash is not null;

    /// <summary>
    /// Checks the commit options without touching git.
    ///
    /// Called before any workbook is read, so a misspelled `--commit-date` is reported
    /// immediately rather than after the whole conversion. Resolution itself is
    /// deferred, because it spawns git and most runs record no history at all.
    /// </summary>
    public static void ValidateOptions(Options options) => ParseDate(options.CommitDate);

    /// <summary>
    /// Works out what this conversion should be recorded as.
    ///
    /// Command-line values win over git, always: a CI job knows things the checkout
    /// does not, such as the branch a detached HEAD came from. Anything not given is
    /// filled in from the repository the sheets live in, and anything git cannot answer
    /// is left null rather than guessed.
    /// </summary>
    public static CommitInfo Resolve(Options options, RecipeModel recipe)
    {
        string? repository = RepositoryDirectory(options, recipe);

        string? hash = Trimmed(options.Commit);
        string? branch = Trimmed(options.Branch);
        string? authorName = null;
        string? authorEmail = null;
        DateTimeOffset? committedAt = ParseDate(options.CommitDate);
        string? subject = null;
        bool dirty = false;

        var origin = hash is not null ? CommitOrigin.CommandLine : CommitOrigin.None;

        if (repository is not null)
        {
            if (hash is null && GitProbe.TryHead(repository, out string? head))
            {
                hash = head;
                origin = CommitOrigin.Git;
            }

            if (hash is not null && GitProbe.TryDescribe(repository, hash, out var described))
            {
                // The described hash rather than what was asked for: a job may pass a
                // short hash or a ref, and the history keys on one spelling.
                hash = described.Hash ?? hash;

                authorName = described.AuthorName;
                authorEmail = described.AuthorEmail;
                committedAt ??= described.CommittedAt;
                subject = described.Subject;
            }

            if (branch is null && GitProbe.TryBranch(repository, out string? checkedOut))
                branch = checkedOut;

            if (GitProbe.TryIsDirty(repository, out bool worktreeDirty))
                dirty = worktreeDirty;
        }

        // After git, so an explicit author is not overwritten by the commit's.
        if (Trimmed(options.CommitAuthor) is string given)
            (authorName, authorEmail) = SplitAuthor(given);

        var resolved = new CommitInfo(
            hash, branch, authorName, authorEmail, committedAt, subject, dirty, origin, repository);

        resolved.Report();

        return resolved;
    }

    /// <summary>A one-line description, for logs and reports.</summary>
    public override string ToString()
    {
        if (!IsIdentified)
            return "(unidentified)";

        string who = AuthorName ?? "unknown author";
        string where = Branch is null ? "" : $" on {Branch}";

        return $"{ShortHash}{where} by {who}{(IsDirty ? " (working tree dirty)" : "")}";
    }

    /// <summary>
    /// Says out loud that what is about to be recorded cannot be attributed.
    ///
    /// For whoever is about to write a snapshot, rather than for every conversion: a
    /// dirty working copy is the normal state of a designer's machine and means nothing
    /// until something records a snapshot from it and files it under a commit its
    /// author never made.
    /// </summary>
    public void WarnIfNotAttributable()
    {
        if (!IsIdentified)
        {
            Log.Warning(
                "This conversion has no commit identity, so its changes cannot be attributed " +
                "to anyone. Pass --commit, or run the conversion inside the working copy the " +
                "sheets are in.");

            return;
        }

        if (IsDirty)
        {
            Log.Warning(
                $"The working copy at `{RepositoryPath}` has uncommitted changes, so this " +
                $"conversion does not match commit {ShortHash}. What is recorded for it will " +
                $"be marked as not attributable.");
        }
    }

    private void Report()
    {
        Log.Debug(IsIdentified
            ? $"Conversion identified as {this}."
            : "This conversion has no commit identity: no --commit was given and the sheets " +
              "are not in a git working copy.");
    }

    /// <summary>
    /// Which directory to ask git about.
    ///
    /// The sheets, not the tool: a project usually keeps its workbooks in a repository
    /// of their own, and the commit that matters is the one that changed a workbook.
    /// Falling back to the working directory covers the case where they are in the same
    /// repository as everything else.
    /// </summary>
    private static string? RepositoryDirectory(Options options, RecipeModel recipe)
    {
        foreach (string candidate in Candidates(options, recipe))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string full;

            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (Directory.Exists(full) && GitProbe.IsRepository(full))
                return full;
        }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> Candidates(Options options, RecipeModel recipe)
    {
        if (!string.IsNullOrWhiteSpace(options.Repository))
        {
            // Given explicitly, so it is the only candidate: silently falling through
            // to somewhere else would record a different repository's commits.
            yield return options.Repository;
            yield break;
        }

        var xlsx = recipe?.Sources?.Xlsx;

        if (xlsx is not null)
        {
            foreach (var source in xlsx.Where(s => !string.IsNullOrWhiteSpace(s?.Path)))
                yield return source.Path;
        }

        yield return Directory.GetCurrentDirectory();
    }

    /// <summary>Splits `Name &lt;email&gt;`, which is how git spells an author.</summary>
    private static (string? Name, string? Email) SplitAuthor(string author)
    {
        int open = author.LastIndexOf('<');
        int close = author.LastIndexOf('>');

        if (open < 0 || close < open)
            return (author, null);

        string name = author.Substring(0, open).Trim();

        return (name.Length == 0 ? null : name, author.Substring(open + 1, close - open - 1).Trim());
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        throw new TabbitException(
            $"--commit-date `{text}` is not a date. Use an ISO 8601 timestamp, such as " +
            $"`2026-08-03T14:05:00+09:00`.");
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
