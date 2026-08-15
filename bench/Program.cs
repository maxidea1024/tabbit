using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tabbit.Bench;

/// <summary>
/// Measures loading the same converted dataset from its three file formats: binary
/// (.tcb, via the generated reader), named JSON, and compact JSON. What it reports
/// and how the JSON side stays fair is described in doc/benchmark.md; how to run it
/// is in bench/readme.md.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: <sizes|verify|binary|json|json-compact> [dataRoot] [iterations]");
            return 2;
        }

        string mode = args[0];
        string dataRoot = args.Length > 1 ? args[1] : "bench/data";
        int iterations = args.Length > 2 ? int.Parse(args[2]) : 10;

        if (!Directory.Exists(Path.Combine(dataRoot, "binary")))
        {
            Console.Error.WriteLine(
                $"No data at `{dataRoot}`. Generate it first:\n" +
                "    dotnet run --project src/Tabbit.csproj -- --recipe bench/recipe.jsonc");
            return 2;
        }

        switch (mode)
        {
            case "sizes":
                Sizes(dataRoot);
                return 0;

            case "verify":
                return await VerifyAsync(dataRoot) ? 0 : 1;

            case "binary":
            case "json":
            case "json-compact":
                Bench(mode, dataRoot, iterations);
                return 0;

            case "probe":
                Probe(dataRoot);
                return 0;

            default:
                Console.Error.WriteLine($"Unknown mode `{mode}`.");
                return 2;
        }
    }

    // ----------------------------------------------------------------- sizes

    private static void Sizes(string dataRoot)
    {
        foreach (string format in new[] { "binary", "json", "json-compact" })
        {
            long raw = 0, gzipped = 0;
            int files = 0;
            foreach (string file in Directory.EnumerateFiles(Path.Combine(dataRoot, format)))
            {
                if (Path.GetFileName(file).StartsWith("manifest", StringComparison.Ordinal))
                    continue;

                byte[] bytes = File.ReadAllBytes(file);
                raw += bytes.Length;
                gzipped += GzipLength(bytes);
                files++;
            }

            Console.WriteLine($"##RESULT## {JsonSerializer.Serialize(new { format, files, raw, gzipped })}");
        }
    }

    private static long GzipLength(byte[] bytes)
    {
        using var sink = new MemoryStream();
        using (var gzip = new GZipStream(sink, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);

        return sink.Length;
    }

    // ----------------------------------------------------------------- verify

    /// <summary>
    /// Loads all three formats and compares them value by value on sampled rows. This is
    /// what makes the numbers comparable at all: a faster load of different data would
    /// not be a result.
    /// </summary>
    private static async Task<bool> VerifyAsync(string dataRoot)
    {
        var plans = JsonPath.BuildPlans(Path.Combine(dataRoot, "json"));

        await Rescue.Tables.Tables.ReadAllAsync(Path.Combine(dataRoot, "binary"));
        var named = await JsonPath.LoadAllAsync(plans, Path.Combine(dataRoot, "json"), compact: false);
        var compact = await JsonPath.LoadAllAsync(plans, Path.Combine(dataRoot, "json-compact"), compact: true);

        int failures = 0;
        long totalRows = 0;
        for (int t = 0; t < plans.Length; t++)
        {
            var plan = plans[t];
            var records = BinaryRecords(plan);
            totalRows += records.Count;

            if (named[t].Rows.Count != records.Count || compact[t].Rows.Count != records.Count)
            {
                Console.Error.WriteLine(
                    $"{plan.Name}: row counts differ - binary {records.Count}, " +
                    $"json {named[t].Rows.Count}, compact {compact[t].Rows.Count}");
                failures++;
                continue;
            }

            if (records.Count == 0)
                continue;

            foreach (int row in new[] { 0, records.Count / 2, records.Count - 1 }.Distinct())
            {
                failures += CompareRow(plan, records[row], named[t].Rows[row], "json", row);
                failures += CompareRow(plan, records[row], compact[t].Rows[row], "json-compact", row);
            }
        }

        Console.WriteLine($"verify: {plans.Length} tables, {totalRows} rows, {failures} failures");
        return failures == 0;
    }

    private static int CompareRow(JsonPath.TablePlan plan, object record, object dto, string side, int row)
    {
        int failures = 0;
        foreach (var prop in plan.RecordProps)
        {
            object expected = prop.GetValue(record);
            object actual = plan.DtoType.GetField(prop.Name).GetValue(dto);

            bool equal = expected is Array expectedArray
                ? ArraysEqual(expectedArray, (Array)actual)
                : Equals(expected, actual);

            if (!equal)
            {
                Console.Error.WriteLine($"{plan.Name}[{row}].{prop.Name} ({side}): binary `{expected}` vs `{actual}`");
                failures++;
            }
        }

        return failures;

        static bool ArraysEqual(Array a, Array b)
        {
            if (b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
                if (!Equals(a.GetValue(i), b.GetValue(i)))
                    return false;

            return true;
        }
    }

    private static IList BinaryRecords(JsonPath.TablePlan plan)
    {
        object table = typeof(Rescue.Tables.Tables).GetProperty(plan.Name).GetValue(null);
        return (IList)plan.TableType.GetProperty("Records").GetValue(table);
    }

    // ----------------------------------------------------------------- probe

    /// <summary>Per-table retained bytes for named vs compact, to localize differences.</summary>
    private static void Probe(string dataRoot)
    {
        var plans = JsonPath.BuildPlans(Path.Combine(dataRoot, "json"));
        var rows = new List<(string name, long named, long compact)>();

        foreach (var plan in plans)
        {
            long named = RetainedOf(plan, dataRoot, compact: false);
            long compact = RetainedOf(plan, dataRoot, compact: true);
            rows.Add((plan.Name, named, compact));
        }

        foreach (var row in rows.OrderByDescending(r => r.named - r.compact).Take(10))
            Console.WriteLine($"{row.name,-28} named {row.named,12:N0}  compact {row.compact,12:N0}  delta {row.named - row.compact,12:N0}");
    }

    private static long RetainedOf(JsonPath.TablePlan plan, string dataRoot, bool compact)
    {
        var one = new[] { plan };
        string dir = Path.Combine(dataRoot, compact ? "json-compact" : "json");
        RunToCompletion(() => JsonPath.LoadAllAsync(one, dir, compact)); // warm caches
        long before = FullCollectAndMeasure();
        object keep = RunToCompletion(() => JsonPath.LoadAllAsync(one, dir, compact));
        long after = FullCollectAndMeasure();
        GC.KeepAlive(keep);
        return after - before;
    }

    // ----------------------------------------------------------------- bench

    private static void Bench(string format, string dataRoot, int iterations)
    {
        // The load runs to completion inside RunToCompletion rather than being awaited
        // here: an await would lift the finished Task into this method's state machine,
        // and a completed Task is the async box holding the load's locals - including
        // the file buffers and, for binary, the whole snapshot. A baseline taken with
        // that box still reachable counts a dead copy of the dataset as live.
        Func<object> load = format switch
        {
            "binary" => () => RunToCompletion(() =>
            {
                // The accessor's statics hold the load; there is nothing to return.
                return Rescue.Tables.Tables.ReadAllAsync(Path.Combine(dataRoot, "binary"))
                    .ContinueWith<object>(_ => null);
            }),
            _ => LoadJson(format, dataRoot),
        };

        long heapAtStart = FullCollectAndMeasure();

        // Warmup: JIT, serializer metadata, and the OS file cache. From here on the
        // numbers measure decoding, not the disk - which is the comparison that holds
        // across machines.
        load();

        // The heap baseline is taken after the warmup's copy is dropped, so `retained`
        // is one loaded copy of the dataset and nothing else. What the loader itself
        // keeps warm across loads - System.Text.Json's array pools and metadata, the
        // emitted DTO plumbing - lands in `loaderBytes` instead of being billed to the
        // data. The binary accessor publishes into statics, which reflection can clear;
        // the JSON paths hold their result only through `keep`.
        ClearBinaryStatics();
        long heapBefore = FullCollectAndMeasure();

        var process = Process.GetCurrentProcess();
        var wallMs = new double[iterations];
        var cpuMs = new double[iterations];
        var allocBytes = new long[iterations];
        object keep = null;

        for (int i = 0; i < iterations; i++)
        {
            keep = null;
            FullCollect();

            process.Refresh();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();

            keep = load();

            stopwatch.Stop();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            process.Refresh();

            wallMs[i] = stopwatch.Elapsed.TotalMilliseconds;
            cpuMs[i] = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            allocBytes[i] = allocatedAfter - allocatedBefore;
        }

        // One loaded copy is alive here: `keep` for the JSON paths, the accessor's
        // statics for binary. Everything transient is collected first.
        long heapAfter = FullCollectAndMeasure();
        GC.KeepAlive(keep);

        var result = new
        {
            format,
            iterations,
            wallMsMedian = Median(wallMs),
            wallMsMin = wallMs.Min(),
            // Mean rather than median: Windows accounts processor time in 15.625 ms
            // quanta, and at these durations a median only lands on quantum edges.
            cpuMsMean = cpuMs.Average(),
            allocMedian = Median(allocBytes.Select(b => (double)b).ToArray()),
            retainedBytes = heapAfter - heapBefore,
            loaderBytes = heapBefore - heapAtStart,
            processorCount = Environment.ProcessorCount,
            serverGc = GCSettings.IsServerGC,
            runtime = RuntimeInformation.FrameworkDescription,
        };

        Console.WriteLine($"##RESULT## {JsonSerializer.Serialize(result)}");
    }

    private static Func<object> LoadJson(string format, string dataRoot)
    {
        bool compact = format == "json-compact";
        string dir = Path.Combine(dataRoot, compact ? "json-compact" : "json");

        // Plans (DTO emission, compiled setters) are startup work a JSON-shipping
        // project does at compile time, so they stay outside the timed window.
        var plans = JsonPath.BuildPlans(Path.Combine(dataRoot, "json"));

        return () => RunToCompletion(() => JsonPath.LoadAllAsync(plans, dir, compact));
    }

    /// <summary>
    /// Starts the load and blocks until it finishes, so the Task dies with this frame.
    /// There is no synchronization context here, so blocking the caller cannot deadlock
    /// the pool threads the continuations run on.
    /// </summary>
    private static T RunToCompletion<T>(Func<Task<T>> start)
        => start().GetAwaiter().GetResult();

    private static void ClearBinaryStatics()
    {
        foreach (var tableProp in typeof(Rescue.Tables.Tables).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (tableProp.PropertyType.GetNestedType("Record") != null)
                tableProp.SetValue(null, null);
        }
    }

    private static void FullCollect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long FullCollectAndMeasure()
    {
        FullCollect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static double Median(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
