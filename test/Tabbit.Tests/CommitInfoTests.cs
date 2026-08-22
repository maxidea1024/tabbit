using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Tabbit;
using Tabbit.History;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Working out which commit a conversion is of, and who made it.
///
/// Against a real repository built for the test rather than a stubbed git, because what
/// is being checked is the reading of git's actual output - the format string, the
/// separator, the detached HEAD, the encoding of a non-ASCII author name. A stub would
/// only confirm that this code agrees with my belief about what git prints.
///
/// The absence of git is its own case rather than a reason to skip. Resolution is
/// written so that missing git costs the attribution and nothing else - a conversion
/// still runs - and
/// <see cref="Without_a_repository_the_conversion_is_simply_unidentified"/> is what
/// holds that promise.
/// </summary>
public class CommitInfoTests : IDisposable
{
    private readonly string _directory;

    public CommitInfoTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tabbit-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            // git marks its object files read-only, which a plain recursive delete
            // refuses on Windows.
            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a passing test over.
        }
    }

    private static bool GitIsAvailable => Git(Path.GetTempPath(), out _, "--version");

    private static Options WithCommitOptions(string repository = null, string commit = null,
                                             string branch = null, string author = null, string date = null)
        => new Options
        {
            Repository = repository,
            Commit = commit,
            Branch = branch,
            CommitAuthor = author,
            CommitDate = date,
        };

    private void MakeRepository(string authorName, string authorEmail, string subject)
    {
        Assert.True(Git(_directory, out _, "init", "--initial-branch=main"), "git init failed.");
        Assert.True(Git(_directory, out _, "config", "user.name", authorName));
        Assert.True(Git(_directory, out _, "config", "user.email", authorEmail));
        Assert.True(Git(_directory, out _, "config", "commit.gpgsign", "false"));

        File.WriteAllText(Path.Combine(_directory, "sheet.txt"), "one");

        Assert.True(Git(_directory, out _, "add", "sheet.txt"));
        Assert.True(Git(_directory, out string committed, "commit", "-m", subject), committed);
    }

    // --------------------------------------------------------------- tests

    /// <summary>
    /// The case that must work on every machine: no git, no crash, no invented author.
    /// </summary>
    [Fact]
    public void Without_a_repository_the_conversion_is_simply_unidentified()
    {
        var resolved = CommitInfo.Resolve(WithCommitOptions(repository: _directory), new RecipeModel());

        Assert.False(resolved.IsIdentified);
        Assert.Null(resolved.Hash);
        Assert.Null(resolved.AuthorName);
        Assert.Equal(CommitOrigin.None, resolved.Origin);
    }

    /// <summary>
    /// An identifier given on the command line is taken as-is, so a project keeping its
    /// sheets somewhere without commits can still record a history.
    /// </summary>
    [Fact]
    public void An_explicit_commit_is_used_even_with_no_git_anywhere()
    {
        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, commit: "build-1042", branch: "release"),
            new RecipeModel());

        Assert.True(resolved.IsIdentified);
        Assert.Equal("build-1042", resolved.Hash);
        Assert.Equal("release", resolved.Branch);
        Assert.Equal(CommitOrigin.CommandLine, resolved.Origin);
    }

    [Fact]
    public void An_explicit_author_is_split_into_a_name_and_an_address()
    {
        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, commit: "x", author: "김한글 <hangul@example.com>"),
            new RecipeModel());

        Assert.Equal("김한글", resolved.AuthorName);
        Assert.Equal("hangul@example.com", resolved.AuthorEmail);
    }

    [Fact]
    public void An_author_given_without_an_address_is_still_a_name()
    {
        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, commit: "x", author: "CI"), new RecipeModel());

        Assert.Equal("CI", resolved.AuthorName);
        Assert.Null(resolved.AuthorEmail);
    }

    /// <summary>
    /// Reported before any workbook is read, rather than after the whole conversion.
    /// </summary>
    [Fact]
    public void A_date_that_is_not_a_date_is_rejected_up_front()
    {
        var ex = Assert.Throws<TabbitException>(
            () => CommitInfo.ValidateOptions(WithCommitOptions(date: "last tuesday")));

        Assert.Equal(Tabbit.History.RecordMessages.CommitDateNotADate, ex.MessageId);
    }

    [Fact]
    public void A_date_given_explicitly_is_kept()
    {
        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, commit: "x", date: "2026-08-03T14:05:00+09:00"),
            new RecipeModel());

        Assert.Equal(DateTimeOffset.Parse("2026-08-03T14:05:00+09:00"), resolved.CommittedAt);
    }

    [Fact]
    public void The_head_commit_and_its_author_are_read_from_the_working_copy()
    {
        Skip.IfNoGit();

        MakeRepository("김한글", "hangul@example.com", "테이블 추가");

        var resolved = CommitInfo.Resolve(WithCommitOptions(repository: _directory), new RecipeModel());

        Assert.True(resolved.IsIdentified);
        Assert.Equal(40, resolved.Hash.Length);
        Assert.Equal(CommitOrigin.Git, resolved.Origin);
        Assert.Equal("main", resolved.Branch);

        // Non-ASCII on both, which is what a legacy-codepage decode would destroy - and
        // an author name is one of the two things this whole feature is for.
        Assert.Equal("김한글", resolved.AuthorName);
        Assert.Equal("hangul@example.com", resolved.AuthorEmail);
        Assert.Equal("테이블 추가", resolved.Subject);

        Assert.False(resolved.IsDirty);
    }

    /// <summary>
    /// A subject holding the field separator must not be read as another field.
    /// </summary>
    [Fact]
    public void A_commit_subject_containing_a_separator_is_read_whole()
    {
        Skip.IfNoGit();

        // The character git separates the fields with, written so it is visible here.
        string subject = "fix: a" + (char)0x1F + "b | c";
        MakeRepository("A", "a@example.com", subject);

        var resolved = CommitInfo.Resolve(WithCommitOptions(repository: _directory), new RecipeModel());

        Assert.Equal("A", resolved.AuthorName);
        Assert.Equal("a@example.com", resolved.AuthorEmail);

        // Whole, rather than truncated at the separator: the subject is the last field
        // and is therefore whatever remains, not one more field.
        Assert.Equal(subject, resolved.Subject);
    }

    /// <summary>
    /// Uncommitted work belongs to nobody the commit names, and a snapshot taken from
    /// it would credit it to whoever made the last commit.
    /// </summary>
    [Fact]
    public void Uncommitted_changes_make_the_conversion_unattributable()
    {
        Skip.IfNoGit();

        MakeRepository("A", "a@example.com", "first");

        File.WriteAllText(Path.Combine(_directory, "sheet.txt"), "two");

        var resolved = CommitInfo.Resolve(WithCommitOptions(repository: _directory), new RecipeModel());

        Assert.True(resolved.IsIdentified);
        Assert.True(resolved.IsDirty);
    }

    /// <summary>
    /// What a CI checkout produces. "HEAD" is not a branch name, and recording it would
    /// file every pull request's snapshots into one interleaved history.
    /// </summary>
    [Fact]
    public void A_detached_head_yields_no_branch_rather_than_the_word_HEAD()
    {
        Skip.IfNoGit();

        MakeRepository("A", "a@example.com", "first");

        Assert.True(Git(_directory, out string head, "rev-parse", "HEAD"));
        Assert.True(Git(_directory, out _, "checkout", "--detach", head.Trim()));

        var resolved = CommitInfo.Resolve(WithCommitOptions(repository: _directory), new RecipeModel());

        Assert.True(resolved.IsIdentified);
        Assert.Null(resolved.Branch);
    }

    /// <summary>
    /// A CI job knows the branch its detached checkout came from, and what it says wins.
    /// </summary>
    [Fact]
    public void A_branch_given_on_the_command_line_beats_the_checkout()
    {
        Skip.IfNoGit();

        MakeRepository("A", "a@example.com", "first");

        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, branch: "pr-42"), new RecipeModel());

        Assert.Equal("pr-42", resolved.Branch);
    }

    /// <summary>
    /// An author given explicitly is not overwritten by the commit's, so a build system
    /// that knows better than the checkout can say so.
    /// </summary>
    [Fact]
    public void An_explicit_author_beats_the_commits_own()
    {
        Skip.IfNoGit();

        MakeRepository("Committer", "committer@example.com", "first");

        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, author: "Designer <designer@example.com>"),
            new RecipeModel());

        Assert.Equal("Designer", resolved.AuthorName);
        Assert.Equal("designer@example.com", resolved.AuthorEmail);
    }

    /// <summary>
    /// A short hash is expanded to the one spelling the history keys on, so the same
    /// commit passed two ways is one snapshot rather than two.
    /// </summary>
    [Fact]
    public void A_short_hash_is_expanded_to_the_full_one()
    {
        Skip.IfNoGit();

        MakeRepository("A", "a@example.com", "first");

        Assert.True(Git(_directory, out string head, "rev-parse", "HEAD"));
        string full = head.Trim();

        var resolved = CommitInfo.Resolve(
            WithCommitOptions(repository: _directory, commit: full.Substring(0, 7)), new RecipeModel());

        Assert.Equal(full, resolved.Hash);
    }

    // ------------------------------------------------------------- plumbing

    /// <summary>
    /// Fails rather than skips, as every other toolchain gate in this suite does. A
    /// gate that turns itself off when the tool is missing reports success for a thing
    /// it never checked, and git is on every machine that can clone this repository.
    /// </summary>
    private static class Skip
    {
        public static void IfNoGit()
            => Assert.True(GitIsAvailable, "git is required to check how commit information is read.");
    }

    private static bool Git(string directory, out string output, params string[] args)
    {
        output = null;

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return false;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            process.WaitForExit(30_000);

            output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
