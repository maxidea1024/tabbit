using Newtonsoft.Json;
using Tabbit.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit;

public class Manifest
{
    public DateTime LastUpdatedDate { get; set; }

    public string MasterHash { get; set; } = "";

    public long TotalSize { get; set; }

    public class Item
    {
        public required string Name { get; set; }

        // Relative, because an absolute path is one machine's answer: a manifest written
        // on a build server would name directories that do not exist anywhere else.
        [JsonIgnore]
        public required string Filename { get; set; }

        public long Size { get; set; }

        public required string Hash { get; set; }

        public DateTime LastUpdatedDate { get; set; }

        [JsonIgnore]
        public bool Dirty { get; set; }
    }

    public List<Item> Items { get; set; } = new List<Item>();
    private int _dirtyCount = 0;

    public void Add(string name, string filename)
    {
        // Asked of the writer before the filesystem. A target hands this the staging file it
        // has just written, and reading that file back to measure it read every byte of the
        // output a second time - 4.59 s of the `json` target's 13.25 s on the sample
        // project. The fallback stays for a file this run did not write through
        // StagingFiles. spec/ops/conversion-time.md section 4.
        if (!StagingFiles.TryWrittenContents(filename, out string hash, out long size))
        {
            size = FileHelper.GetFileSize(filename);
            hash = Helper.CalculateMD5HashFromFile(filename);
        }

        var existing = Find(name);
        if (existing is not null)
        {
            existing.Filename = filename;

            if (hash != existing.Hash)
            {
                existing.Dirty = true;
                existing.Hash = hash;
                existing.Size = size;
                existing.LastUpdatedDate = DateTime.Now;
                _dirtyCount++;
            }
        }
        else
        {
            var item = new Item
            {
                Dirty = true,
                Name = name,
                Hash = hash,
                Filename = filename,
                Size = size,
                LastUpdatedDate = DateTime.Now
            };
            _dirtyCount++;

            Items.Add(item);
            _byName?.TryAdd(name, item);
        }
    }

    /// <summary>
    /// The item this name already has, or null.
    /// </summary>
    /// <remarks>
    /// Indexed rather than scanned. A target adds one entry per table and asks this for
    /// each of them, so a linear scan makes the ledger quadratic in the number of tables -
    /// which on a project of five hundred is the difference between nothing and something.
    ///
    /// Built on first use rather than in the constructor, because <see cref="Items"/>
    /// arrives from the deserializer after that has run.
    /// </remarks>
    private Item? Find(string name)
    {
        if (_byName is null)
        {
            _byName = new Dictionary<string, Item>(Items.Count, StringComparer.Ordinal);

            // Whichever comes first wins, which is what List.Find answered. A manifest with
            // two entries for one name is already broken; this is not the place that says so.
            foreach (var item in Items)
                _byName.TryAdd(item.Name, item);
        }

        return _byName.TryGetValue(name, out var found) ? found : null;
    }

    private Dictionary<string, Item>? _byName;

    /// <summary>
    /// Asks for the files this ledger already lists to be removed unless this run writes
    /// them again.
    /// </summary>
    /// <remarks>
    /// Called right after loading, while <see cref="Items"/> is still the previous run's
    /// record: rename a table and its old file simply stays in the output directory, and
    /// a stale data file is worse than a stale source file. It ships, it costs transfer,
    /// and a build still asking for the old name reads it - old values, from a rollback
    /// nobody performed.
    ///
    /// The ledger is the permission. Nothing outside it can be named here, so a directory
    /// holding somebody else's files is untouchable no matter what is in it.
    /// </remarks>
    public void PruneStaleFiles(string directory)
    {
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.Name))
                StagingFiles.RegisterPruneCandidate(System.IO.Path.Combine(directory, item.Name));
        }
    }

    public static Manifest Load(string filename)
    {
        // Read from the committed output rather than from staging: staging is emptied
        // once a run commits, so by now there is nothing there.
        //string stagingFilename = StagingFiles.RegisterStagingFile(filename);

        try
        {
            return FileHelper.ReadFromJsonFile<Manifest>(filename) ?? new Manifest();
        }
        catch
        {
            return new Manifest();
        }
    }

    public void BuildAndWriteToFile(string filename)
    {
        // Drop what is no longer there.
        if (Items.RemoveAll(x => x.Filename is null) is int dropped and > 0)
        {
            _dirtyCount += dropped;
            _byName = null;
        }

        if (_dirtyCount > 0 || Items.Count == 0)
        {
            LastUpdatedDate = DateTime.Now;
            MasterHash = Helper.CalculateMD5HashFromFiles(Items.Select(x => x.Filename).ToArray());
            TotalSize = 0;

            foreach (var item in Items)
                TotalSize += item.Size;

            StagingFiles.WriteToJsonFile(filename, this);
        }
    }
}
