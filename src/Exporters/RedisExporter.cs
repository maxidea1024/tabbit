using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tabbit.Models;
using Tabbit.Recipe;
using Serilog;
using StackExchange.Redis;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// Redis target. One hash per row, plus an index set per table.
/// </summary>
public class RedisRecipe : DatabaseRecipe
{
    /// <summary>Database number to select on the server.</summary>
    public int Database { get; set; } = 0;
}

/// <summary>
/// Loads the cooked tables into Redis.
///
/// Layout per table `Item`:
///
///   Item:1, Item:2, ...   one hash per row, field per column
///   Item:index            a set of every primary index present
///
/// Rows are written under a shadow prefix and then renamed into place inside a
/// MULTI/EXEC block, so a reader either sees the whole previous generation or the
/// whole new one. Redis has no notion of a table to swap, so the swap is done key
/// by key within that one atomic block.
/// </summary>
[TabbitTarget("redis", TargetKind.Export, Order = 60)]
public class RedisExporter : DatabaseExporterBase<RedisRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    protected override string TargetName => "Redis";

    /// <summary>Suffix of the set listing a table's primary index values.</summary>
    private const string IndexKeySuffix = ":index";


    protected override void ExportTo(DatabaseRecipe recipe, Model model)
    {
        var redisRecipe = (RedisRecipe)recipe;

        string connectionString = ConnectionString.Resolve(recipe.ConnectionString, RecipeSection);

        Log.Debug($"Connecting to Redis `{ConnectionString.Redact(connectionString)}`");

        var configuration = ConfigurationOptions.Parse(connectionString);

        // Needed for the swap: renaming keys and deleting by pattern are admin-ish
        // operations that the driver refuses by default.
        configuration.AllowAdmin = true;

        using var connection = ConnectionMultiplexer.Connect(configuration);
        var database = connection.GetDatabase(redisRecipe.Database);

        var written = new List<ShadowKeys>();

        try
        {
            foreach (var table in model.Tables)
                written.Add(WriteShadowKeys(database, StorageName(recipe, table), table));

            foreach (var keys in written)
                SwapIn(database, keys);
        }
        catch
        {
            DeleteShadowKeys(database, written);
            throw;
        }
    }

    /// <summary>
    /// The shadow keys written for one table, remembered so they can be swapped in
    /// or cleaned up without re-deriving them.
    /// </summary>
    private sealed class ShadowKeys
    {
        public string LiveName = "";
        public string ShadowName = "";

        /// <summary>Shadow key paired with the live key it replaces.</summary>
        public List<(RedisKey Shadow, RedisKey Live)> Pairs = [];
    }

    private ShadowKeys WriteShadowKeys(IDatabase database, string name, Table table)
    {
        var result = new ShadowKeys
        {
            LiveName = name,
            ShadowName = name + ShadowSuffix,
        };

        // Leftovers from an interrupted earlier run would otherwise be swapped in
        // alongside this run's data.
        DeleteByPrefix(database, result.ShadowName + ":");

        var columns = Columns(table);
        var indexColumn = columns.FirstOrDefault(sf => sf.IsIndexer);

        var indexValues = new List<RedisValue>(table.Data.Count);
        var batch = database.CreateBatch();
        var pending = new List<Task>();

        foreach (var row in table.Data)
        {
            string? key = indexColumn is not null
                ? ToText(row[indexColumn.FirstField!.Index].Value!)
                : (indexValues.Count + 1).ToString();

            var entries = columns
                .Select(sf => new HashEntry(ColumnName(sf), ToText(CellValue(row, sf))))
                .ToArray();

            var shadowKey = (RedisKey)($"{result.ShadowName}:{key}");
            var liveKey = (RedisKey)($"{result.LiveName}:{key}");

            pending.Add(batch.HashSetAsync(shadowKey, entries));

            result.Pairs.Add((shadowKey, liveKey));
            indexValues.Add(key);
        }

        if (indexValues.Count > 0)
        {
            var shadowIndex = (RedisKey)(result.ShadowName + IndexKeySuffix);
            pending.Add(batch.SetAddAsync(shadowIndex, indexValues.ToArray()));

            result.Pairs.Add((shadowIndex, (RedisKey)(result.LiveName + IndexKeySuffix)));
        }

        // A batch pipelines the whole table in one round trip rather than one per key.
        batch.Execute();
        Task.WaitAll(pending.ToArray());

        return result;
    }

    /// <summary>
    /// Moves this table's shadow keys onto their live names in one atomic block.
    ///
    /// The old keys are removed in the same transaction, so a reader that looks
    /// between two of the renames cannot see a mix of generations.
    /// </summary>
    private void SwapIn(IDatabase database, ShadowKeys keys)
    {
        var stale = FindKeys(database, keys.LiveName + ":").ToList();
        var replacing = new HashSet<RedisKey>(keys.Pairs.Select(p => p.Live));

        var transaction = database.CreateTransaction();
        var pending = new List<Task>();

        // Rows that existed before but not now: dropped here so a shrinking table
        // does not leave orphans behind.
        foreach (var key in stale.Where(k => !replacing.Contains(k)))
            pending.Add(transaction.KeyDeleteAsync(key));

        foreach (var (shadow, live) in keys.Pairs)
            pending.Add(transaction.KeyRenameAsync(shadow, live));

        if (!transaction.Execute())
            throw new TabbitException($"Redis refused the swap transaction for `{keys.LiveName}`.");

        Task.WaitAll(pending.ToArray());

        Log.Debug($"Swapped {keys.Pairs.Count} Redis key(s) for `{keys.LiveName}` into place");
    }

    private void DeleteShadowKeys(IDatabase database, List<ShadowKeys> written)
    {
        // Best effort: the load already failed and that exception is the one worth
        // reporting.
        try
        {
            foreach (var keys in written)
                DeleteByPrefix(database, keys.ShadowName + ":");
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not clean up Redis shadow keys: {ex.Message}");
        }
    }

    private void DeleteByPrefix(IDatabase database, string prefix)
    {
        var keys = FindKeys(database, prefix).ToArray();
        if (keys.Length > 0)
            database.KeyDelete(keys);
    }

    /// <summary>
    /// Every key starting with a prefix, plus the prefix's own index key.
    ///
    /// SCAN rather than KEYS, so a large database is not blocked while the export
    /// looks around.
    /// </summary>
    private IEnumerable<RedisKey> FindKeys(IDatabase database, string prefix)
    {
        var server = database.Multiplexer.GetServer(database.Multiplexer.GetEndPoints().First());

        foreach (var key in server.Keys(database.Database, pattern: prefix + "*"))
            yield return key;

        string indexKey = prefix.TrimEnd(':') + IndexKeySuffix;
        if (database.KeyExists(indexKey))
            yield return indexKey;
    }
}
