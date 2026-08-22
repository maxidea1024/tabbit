using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Tabbit.Messages;

namespace Tabbit.History;

/// <summary>What happened when a conversion asked to be recorded.</summary>
public enum RecordOutcome
{
    /// <summary>A snapshot was written.</summary>
    Recorded,

    /// <summary>This commit was already in the history, describing the same model.</summary>
    AlreadyPresent,

    /// <summary>Recording it would have put a wrong answer in the history.</summary>
    Refused,
}

/// <summary>
/// Decides whether a conversion may be recorded, and records it.
///
/// Separate from the target so the decisions can be exercised against a real database
/// without a conversion around them. They are most of what the feature is: the writing
/// is mechanical, and every refusal here is a case where recording would leave the
/// history holding something plausible and wrong.
/// </summary>
internal static class HistoryRecorder
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    /// <summary>
    /// Whether this conversion can honestly be filed under a commit at all.
    ///
    /// Checked before a connection is opened, because neither answer depends on what
    /// the history holds.
    /// </summary>
    public static bool CanRecord(CommitInfo commit, HistoryRecipe recipe)
    {
        if (!commit.IsIdentified)
        {
            commit.WarnIfNotAttributable();

            Log.Warning(Message.Of(RecordMessages.LogNothingRecorded).In(MessageCatalog.Current));
            return false;
        }

        if (commit.IsDirty && !recipe.RecordDirty)
        {
            commit.WarnIfNotAttributable();

            Log.Warning(Message.Of(RecordMessages.LogNothingRecordedDirty,
                ("Commit", commit.ShortHash)).In(MessageCatalog.Current));

            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a snapshot, unless the history already holds this commit or the chain
    /// would have to go backwards.
    /// </summary>
    public static RecordOutcome Record(
        HistoryStore store,
        SummaryDocument summary,
        ModelFingerprint fingerprint,
        CommitInfo commit,
        HistoryRecipe recipe,
        out long snapshotId)
    {
        snapshotId = 0;

        var existing = store.FindSnapshot(commit.Hash!);

        if (existing is not null)
        {
            if (string.Equals(existing.ModelHash, summary.Data.Hash, StringComparison.Ordinal))
            {
                // The same commit converted twice, which is what a rerun or a second CI
                // job does. Nothing to record, and nothing wrong.
                Log.Information($"The history already holds commit {commit.ShortHash}; nothing changed.");

                snapshotId = existing.Id;
                return RecordOutcome.AlreadyPresent;
            }

            throw new TabbitException(null,
                Message.Of(RecordMessages.ModelDiffersForCommit,
                    ("Commit", commit.ShortHash), ("Branch", BranchOf(commit))));
        }

        var head = store.ReadHead();

        if (!MayFollow(head, commit, recipe))
            return RecordOutcome.Refused;

        var changes = SnapshotDiff.Compute(fingerprint, store);

        if (changes.IsEmpty && head is not null)
        {
            // A commit that touched something other than the sheets. Recorded anyway:
            // without it the next real change would be measured from further back and
            // attributed across a range rather than to the commit that made it.
            Log.Information(
                $"Commit {commit.ShortHash} changed nothing in the sheets; recording the snapshot anyway.");
        }

        snapshotId = store.Write(new SnapshotWrite
        {
            Summary = summary,
            Fingerprint = fingerprint,
            Changes = changes,
            Seq = (head?.Seq ?? 0) + 1,
            ParentId = head?.Id,
            FollowsParent = FollowsParent(head, commit),
            ChangedTables = ChangedTables(changes),
            RowHashes = RowHashes(fingerprint, changes),
        });

        Log.Information(
            $"Recorded snapshot {snapshotId} for {commit.ShortHash} on `{BranchOf(commit)}`: {changes}.");

        return RecordOutcome.Recorded;
    }

    /// <summary>
    /// Whether this commit may extend the chain the branch already has.
    ///
    /// A snapshot's changes are measured against the snapshot before it, so the chain
    /// has to move forwards. Recording an older commit after a newer one produces a
    /// changeset that undoes the newer one's work and credits it to the older one's
    /// author - a plausible answer that is exactly backwards.
    /// </summary>
    private static bool MayFollow(SnapshotRow? head, CommitInfo commit, HistoryRecipe recipe)
    {
        if (head is null || recipe.AllowOutOfOrder || !IsBehind(head, commit))
            return true;

        Log.Error(Message.Of(RecordMessages.LogCommitIsBehind,
            ("Commit", commit.ShortHash), ("Head", Short(head.CommitHash)),
            ("Branch", BranchOf(commit))).In(MessageCatalog.Current));

        return false;
    }

    /// <summary>
    /// Whether the commit comes before the head.
    ///
    /// Asked of git where it can answer, because ancestry is the actual question and a
    /// timestamp is only a proxy: two commits made a second apart on different machines
    /// can be dated in either order. The proxy is the fallback for the projects that
    /// pass their own identifiers and have no repository to ask.
    /// </summary>
    private static bool IsBehind(SnapshotRow head, CommitInfo commit)
    {
        if (commit.RepositoryPath is not null
            && GitProbe.TryIsAncestor(commit.RepositoryPath, head!.CommitHash, commit.Hash!, out bool descends))
        {
            return !descends;
        }

        return head.CommittedAt.HasValue
               && commit.CommittedAt.HasValue
               && commit.CommittedAt.Value.UtcDateTime < head.CommittedAt.Value;
    }

    /// <summary>
    /// Whether these changes cover only this commit, or the commits behind it too.
    ///
    /// A build that skipped some commits - a database that was down, or simply nobody
    /// converting every commit - produces a snapshot whose changes span the gap. Saying
    /// so is what stops a report crediting one person with several people's work.
    ///
    /// True when it cannot be told: claiming a gap that may not exist would put a
    /// warning on a report that has nothing wrong with it.
    /// </summary>
    private static bool FollowsParent(SnapshotRow? head, CommitInfo commit)
    {
        if (head is null)
            return true;

        if (commit.RepositoryPath is not null
            && GitProbe.TryIsDirectParent(commit.RepositoryPath, head!.CommitHash, commit.Hash!, out bool direct))
        {
            return direct;
        }

        return true;
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Tables whose column list has to be rewritten: the ones whose schema moved.</summary>
    private static HashSet<string> ChangedTables(SnapshotChanges changes)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in changes.Schema)
        {
            if (change.EntityKind == EntityKind.Table || change.EntityKind == EntityKind.Field)
                tables.Add(change.EntityName);
        }

        return tables;
    }

    /// <summary>The row hash of every row about to be written.</summary>
    private static Dictionary<(string Table, string RowKey), string> RowHashes(
        ModelFingerprint fingerprint, SnapshotChanges changes)
    {
        var wanted = new HashSet<(string, string)>(
            changes.Rows.Where(r => r.Kind != ChangeKind.Removed).Select(r => (r.Table, r.RowKey)));

        var hashes = new Dictionary<(string, string), string>();

        foreach (var table in fingerprint.Tables)
        {
            foreach (var row in table.Rows)
            {
                if (wanted.Contains((table.Name, row.Key)))
                    hashes[(table.Name, row.Key)] = row.Hash;
            }
        }

        return hashes;
    }

    private static string BranchOf(CommitInfo commit) => commit.Branch ?? "(no branch)";

    private static string? Short(string hash)
        => hash is null ? null : hash.Substring(0, Math.Min(12, hash.Length));
}
