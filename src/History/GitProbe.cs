using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace Tabbit.History;

/// <summary>
/// Asks git about the working copy the sheets came from.
///
/// Every call here can fail and none of them may stop a conversion. git may not be
/// installed, the sheets may not be in a repository, the repository may be in a state
/// this does not understand. A build produces game data; being unable to describe who
/// produced it is worth a warning, not a failed build.
///
/// So every method returns whether it worked rather than throwing, and the caller
/// decides. What must never happen is a wrong answer passed off as a right one - an
/// empty author recorded as if it were the author.
/// </summary>
internal static class GitProbe
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    /// <summary>
    /// Long enough for a cold `git` on Windows, short enough that a hung one does not
    /// hold a build. A repository probe is three small commands.
    /// </summary>
    private const int TimeoutMilliseconds = 15_000;

    /// <summary>Whether the directory is inside a git working tree.</summary>
    public static bool IsRepository(string directory)
        => TryRun(directory, out string? inside, "rev-parse", "--is-inside-work-tree")
           && string.Equals(inside, "true", StringComparison.Ordinal);

    /// <summary>The commit HEAD points at.</summary>
    public static bool TryHead(string directory, [NotNullWhen(true)] out string? hash)
        => TryRun(directory, out hash, "rev-parse", "HEAD");

    /// <summary>
    /// The branch checked out, or false when HEAD is detached.
    ///
    /// A detached HEAD is what a CI checkout usually produces, and "HEAD" is not a
    /// branch name - recording it would file every CI snapshot under one branch and
    /// interleave the histories of every pull request.
    /// </summary>
    public static bool TryBranch(string directory, [NotNullWhen(true)] out string? branch)
    {
        if (!TryRun(directory, out branch, "rev-parse", "--abbrev-ref", "HEAD"))
            return false;

        if (string.IsNullOrEmpty(branch) || branch == "HEAD")
        {
            branch = null;
            return false;
        }

        return true;
    }

    /// <summary>Author, date and subject of one commit.</summary>
    public static bool TryDescribe(string directory, string commit, [NotNullWhen(true)] out GitCommit? described)
    {
        described = null;

        // A record separator rather than a line separator: a commit subject can be
        // anything, including something that looks like the next field.
        const string Format = "%H%x1f%an%x1f%ae%x1f%aI%x1f%s";

        // The same character git writes for `%x1f`, spelled so it is visible in an
        // editor. A literal control character here reads as nothing at all.
        const char Separator = (char)0x1F;

        if (!TryRun(directory, out string? output, "show", "--no-patch", "--format=" + Format, commit))
            return false;

        // `git show` on a commit prints the format once; on a tag or a tree it may
        // print nothing this shape, so the field count is checked rather than assumed.
        var fields = output.Split(Separator);
        if (fields.Length < 5)
            return false;

        DateTimeOffset? committedAt = null;
        if (DateTimeOffset.TryParse(fields[3], System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            committedAt = parsed;
        }

        described = new GitCommit(
            hash: fields[0],
            authorName: Blank(fields[1]),
            authorEmail: Blank(fields[2]),
            committedAt: committedAt,

            // Whatever remains, not one more field. The subject is free text and git
            // escapes nothing in it, so a message that happens to contain the separator
            // would otherwise arrive truncated at it.
            subject: Blank(string.Join(Separator, fields, 4, fields.Length - 4)));

        return true;
    }

    /// <summary>
    /// Whether the working tree has changes that are not in HEAD.
    ///
    /// This is what decides whether a snapshot can honestly be attributed to a commit.
    /// Uncommitted edits are somebody's work that no commit describes, so recording
    /// them under HEAD credits them to whoever happened to make the last commit.
    /// </summary>
    public static bool TryIsDirty(string directory, out bool dirty)
    {
        dirty = false;

        if (!TryRun(directory, out string? status, "status", "--porcelain", "--untracked-files=no"))
            return false;

        dirty = status.Length > 0;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="descendant"/> has <paramref name="ancestor"/> in its
    /// history.
    ///
    /// Asked instead of comparing timestamps, because ancestry is the actual question:
    /// two commits made a second apart on different machines can be dated in either
    /// order, and a clock is not what decides which came first in a branch.
    /// </summary>
    /// <returns>False when git could not answer, leaving <paramref name="descends"/> unset.</returns>
    public static bool TryIsAncestor(string directory, string ancestor, string descendant, out bool descends)
    {
        descends = false;

        if (string.IsNullOrEmpty(ancestor) || string.IsNullOrEmpty(descendant))
            return false;

        // Exit 0 means yes and exit 1 means no, so the two cannot be told apart by
        // TryRun's success alone - and an unknown revision also exits non-zero. The
        // rev-parse pair is what separates "no" from "cannot say".
        if (!TryRun(directory, out _, "rev-parse", "--verify", "--quiet", ancestor + "^{commit}")
            || !TryRun(directory, out _, "rev-parse", "--verify", "--quiet", descendant + "^{commit}"))
        {
            return false;
        }

        descends = TryRun(directory, out _, "merge-base", "--is-ancestor", ancestor, descendant);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="parent"/> is a direct parent of <paramref name="commit"/>.
    ///
    /// Not the same question as ancestry. Two snapshots whose commits are ten commits
    /// apart are still in order, but the changes between them cover ten commits' work
    /// and belong to more than one person - which a report has to say.
    /// </summary>
    /// <returns>False when git could not answer, leaving <paramref name="direct"/> unset.</returns>
    public static bool TryIsDirectParent(string directory, string parent, string commit, out bool direct)
    {
        direct = false;

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(commit))
            return false;

        // Every parent, not just the first: a merge has two, and a snapshot recorded on
        // either side of one still follows directly from it.
        if (!TryRun(directory, out string? parents, "rev-list", "--parents", "-n", "1", commit))
            return false;

        var hashes = parents.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (hashes.Length == 0)
            return false;

        if (!TryRun(directory, out string? resolved, "rev-parse", "--verify", "--quiet", parent + "^{commit}"))
            return false;

        // The first entry is the commit itself; the rest are its parents.
        direct = hashes.Skip(1).Any(h => string.Equals(h, resolved, StringComparison.OrdinalIgnoreCase));

        return true;
    }

    /// <summary>
    /// Turns anything git understands into the commit it names.
    ///
    /// A tag, a branch, `HEAD~3`, a short hash - all of them, because there is no
    /// reason to accept one spelling of "that commit" and not the others. `^{commit}`
    /// is what makes an annotated tag resolve to the commit it points at rather than to
    /// the tag object, which is a different hash and matches nothing.
    /// </summary>
    /// <returns>False when git could not be run, or the name is not a revision.</returns>
    public static bool TryResolveCommit(string directory, string? name, [NotNullWhen(true)] out string? hash)
    {
        hash = null;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!TryRun(directory, out string? resolved, "rev-parse", "--verify", "--quiet", name + "^{commit}"))
            return false;

        if (string.IsNullOrWhiteSpace(resolved))
            return false;

        hash = resolved;
        return true;
    }

    /// <summary>When a commit was made, as an ISO 8601 timestamp.</summary>
    public static bool TryCommittedAt(string directory, string commit, out DateTimeOffset at)
    {
        at = default;

        if (!TryRun(directory, out string? text, "show", "--no-patch", "--format=%cI", commit))
            return false;

        return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                                       System.Globalization.DateTimeStyles.RoundtripKind, out at);
    }

    /// <summary>The blob hash git has for a file, which identifies its exact contents.</summary>
    public static bool TryBlobHash(string directory, string path, [NotNullWhen(true)] out string? hash)
        => TryRun(directory, out hash, "rev-parse", "HEAD:" + path.Replace('\\', '/'));

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Runs git and returns its trimmed standard output.
    /// </summary>
    /// <returns>False when git could not be run, or exited non-zero.</returns>
    public static bool TryRun(string directory, [NotNullWhen(true)] out string? output, params string[] args)
    {
        output = null;

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return false;

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // git writes UTF-8. Decoding it as the console codepage turns every
            // non-ASCII author name into question marks, and an author name is one of
            // the two things this whole feature is for.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            // Read before waiting: a process whose output fills the pipe buffer blocks
            // on the write, and a wait-then-read deadlocks against it.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                Log.Debug($"`git {string.Join(' ', args)}` did not finish in {TimeoutMilliseconds} ms.");

                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }

                return false;
            }

            string text = stdout.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                Log.Debug($"`git {string.Join(' ', args)}` exited {process.ExitCode}: " +
                          stderr.GetAwaiter().GetResult().Trim());
                return false;
            }

            output = text.Trim('\n', '\r', ' ', '\t');
            return true;
        }
        catch (Exception ex)
        {
            // Most often git is not installed. Debug rather than warning: the caller
            // reports the consequence once, and it would otherwise say so three times
            // for the three probes a resolution makes.
            Log.Debug($"`git {string.Join(' ', args)}` could not be run: {ex.Message}");
            return false;
        }
    }
}

/// <summary>What git knows about one commit.</summary>
internal sealed class GitCommit
{
    public GitCommit(
        string hash, string? authorName, string? authorEmail, DateTimeOffset? committedAt,
        string? subject)
    {
        Hash = hash;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        CommittedAt = committedAt;
        Subject = subject;
    }

    public string Hash { get; }
    public string? AuthorName { get; }
    public string? AuthorEmail { get; }
    public DateTimeOffset? CommittedAt { get; }
    public string? Subject { get; }
}
