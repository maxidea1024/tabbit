using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The conformance corpus: one table of boundary values, read by every language whose
/// reader is checked, compared against what the JSON exporter wrote from the same cells.
///
/// This exists so that adding an output language costs a harness of about fifty lines
/// rather than a gate of its own. The three gates that came before it each name the
/// tables of one particular fixture and cannot be pointed anywhere else.
///
/// What the corpus is for is the class of defect that does not fail - it returns the
/// wrong value. The binary writer truncated every `long` to 32 bits for years, and the
/// JSON export lost anything past 2^53, and both survived because nothing read the data
/// back in a language that could tell.
/// </summary>
public class ConformanceTests
{
    private const string Scenario = "conformance";

    /// <summary>
    /// The corpus schema, which the comparison needs in order to canonicalize the
    /// exporter's JSON the way the harness contract asks a harness to print.
    /// </summary>
    private static readonly Dictionary<string, string> FieldTypes = new Dictionary<string, string>
    {
        { "index", "int" },
        { "intVal", "int" },
        { "bigVal", "bigint" },
        { "floatVal", "float" },
        { "doubleVal", "double" },
        { "text", "string" },
        { "flag", "bool" },
        { "when", "datetime" },
        { "span", "timespan" },
        { "uid", "uuid" },
        { "label", "enum" },
        { "ints", "int[]" },
        { "strs", "string[]" },

        // The two array forms whose element read is not the scalar one in a loop: an enum
        // element goes through a cast, and in C through a scratch variable, and a uuid
        // element is sixteen bytes rather than a value.
        { "labels", "enum[]" },
        { "uids", "uuid[]" },

        // The three the v104 encodings need somewhere to win: whole numbers carried as
        // integers, and values built from shared pieces with and without runs.
        { "count", "double" },
        { "route", "string" },
        { "zone", "string" },

        // The two references, compared as the index each came in as - which is what the
        // exporter writes for a `foreign` field, resolved value or not.
        //
        // What they are for is not the comparison. Splitting each target's output into a
        // file per table gave every language a question it did not have before, which is
        // how one table's file reaches another's, and a harness loading through the
        // accessor runs the reference resolution whether or not the result is compared. A
        // language whose split output cannot see the other table's file does not compile,
        // or does not load, and never arrives here.
        { "owner", "int" },
        { "tier", "int" },
    };

    [Fact]
    public void Generated_csharp_reader_matches_the_corpus()
    {
        var expected = Expected();

        var harness = ConformanceHarness.RunCsharp(Scenario);
        Assert.True(harness.Succeeded, $"C# harness failed.{Environment.NewLine}{harness.Output}");

        Compare("C#", expected, Parse(harness.StdOut));
    }

    [Fact]
    public void Generated_cpp_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        var harness = ConformanceHarness.RunCpp(Scenario);
        Assert.True(harness.Succeeded, $"C++ harness failed.{Environment.NewLine}{harness.Output}");

        Compare("C++", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Unreal, built against stubs rather than an engine.
    /// </summary>
    /// <remarks>
    /// The last target to get here, and the one that most needed to. Every other language's
    /// reader had its values compared against the exporter's from the day it was added; this
    /// one had "does it compile, does it use engine types, does it avoid throwing", because
    /// running it meant installing an engine and a test machine does not have one. So the
    /// varint decoding, the zig-zag, the UTF-8, the GUID byte order and the tick handling in
    /// the target most likely to ship in a game were the least checked bytes in the
    /// repository.
    ///
    /// What the stubs do and do not prove is written down in tools/unreal-stubs/CoreMinimal.h.
    /// The short version: the decoding is the generated code's and the reader's, which is
    /// what this compares; the stubs supply storage and formatting. What is still unchecked
    /// is whether the engine's own types behave as the stubs do - and that is a smaller gap
    /// than the one this closes.
    /// </remarks>
    [Fact]
    public void Generated_unreal_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.UnrealOffEngineIsAvailable(out string why), why);

        var harness = ConformanceHarness.RunUnreal(Scenario);
        Assert.True(harness.Succeeded, $"Unreal harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Unreal", expected, Parse(harness.StdOut));
    }

    [Fact]
    public void Generated_typescript_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript harness. {why}");

        var harness = ConformanceHarness.RunTypescript(Scenario);
        Assert.True(harness.Succeeded, $"TypeScript harness failed.{Environment.NewLine}{harness.Output}");

        Compare("TypeScript", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Go, the first language added on top of the corpus rather than before it.
    ///
    /// It cost a reader, a template, a view and this harness, and nothing was added to
    /// the comparison above - which is what the corpus was for.
    /// </summary>
    [Fact]
    public void Generated_go_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"A Go toolchain is required to check the generated Go. {why}");

        var harness = ConformanceHarness.RunGo(Scenario);
        Assert.True(harness.Succeeded, $"Go harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Go", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Rust, which keeps references as indices rather than resolving them into borrows.
    ///
    /// A record holding a reference to another record is a graph, and Rust will not let
    /// one own its neighbours. The corpus does not exercise a reference, so this checks
    /// the value types - which is where the format's traps are.
    /// </summary>
    [Fact]
    public void Generated_rust_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.RustIsAvailable(out string why),
            $"A Rust toolchain is required to check the generated Rust. {why}");

        var harness = ConformanceHarness.RunRust(Scenario);
        Assert.True(harness.Succeeded, $"Rust harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Rust", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Python, the first language without a single-precision float.
    ///
    /// A float32 read widens to a double holding the stored value, so the harness
    /// narrows it back before printing - the same step the TypeScript reader makes with
    /// Math.fround, for the same reason.
    /// </summary>
    [Fact]
    public void Generated_python_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated Python. {why}");

        var harness = ConformanceHarness.RunPython(Scenario);
        Assert.True(harness.Succeeded, $"Python harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Python", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Java, the first language with no unsigned types.
    ///
    /// That is exactly where the format's varint decoding goes wrong when nobody is
    /// watching: a byte with its high bit set is negative and has to be masked before
    /// it is shifted, and undoing the zig-zag fold needs the unsigned shift rather than
    /// the arithmetic one. The corpus holds five-byte varints either side of zero, so a
    /// reader that got either wrong fails here.
    /// </summary>
    [Fact]
    public void Generated_java_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"A JDK is required to check the generated Java. {why}");

        var harness = ConformanceHarness.RunJava(Scenario);
        Assert.True(harness.Succeeded, $"Java harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Java", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Kotlin, which reads on the same JVM as Java but through a reader of its own.
    ///
    /// kotlinc resolves Java sources without compiling them, so sharing the Java reader
    /// would oblige a pure Kotlin project to keep javac in its build purely to get one.
    /// A second reader is a second thing that can drift, which is what this checks.
    /// </summary>
    [Fact]
    public void Generated_kotlin_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.KotlinIsAvailable(out string why),
            $"A Kotlin compiler is required to check the generated Kotlin. {why}");

        var harness = ConformanceHarness.RunKotlin(Scenario);
        Assert.True(harness.Succeeded, $"Kotlin harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Kotlin", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Ruby, whose Integer is arbitrary precision.
    ///
    /// That removes the 64-bit trap the other dynamic languages have and leaves the
    /// encoding one: a Ruby string carries its encoding, and standard output transcodes
    /// to the default external unless it is told otherwise.
    /// </summary>
    [Fact]
    public void Generated_ruby_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"A Ruby interpreter is required to check the generated Ruby. {why}");

        var harness = ConformanceHarness.RunRuby(Scenario);
        Assert.True(harness.Succeeded, $"Ruby harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Ruby", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// Dart, the second language after TypeScript whose integer is not always 64 bits.
    ///
    /// On the web an int is a double, so int64 and both tick counts are read as BigInt
    /// and the varint is decoded with arithmetic rather than bit operations - those are
    /// 32-bit there. The corpus holds values past 2^53 and five-byte varints either side
    /// of zero, so a reader that took the obvious route disagrees here.
    /// </summary>
    [Fact]
    public void Generated_dart_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.DartIsAvailable(out string why),
            $"A Dart SDK is required to check the generated Dart. {why}");

        var harness = ConformanceHarness.RunDart(Scenario);
        Assert.True(harness.Succeeded, $"Dart harness failed.{Environment.NewLine}{harness.Output}");

        Compare("Dart", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// C, where the reader has to answer two questions the others do not: who owns a
    /// string, and what happens without exceptions.
    ///
    /// Strings live in an arena the table owns, so what this checks along with the
    /// values is that they are still readable after the file buffer has been released -
    /// a reader that pointed into the buffer would pass every value test and hand back
    /// freed memory here.
    /// </summary>
    [Fact]
    public void Generated_c_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.CIsAvailable(out string why),
            $"A C compiler is required to check the generated C. {why}");

        var harness = ConformanceHarness.RunC(Scenario);
        Assert.True(harness.Succeeded, $"C harness failed.{Environment.NewLine}{harness.Output}");

        Compare("C", expected, Parse(harness.StdOut));
    }

    /// <summary>
    /// PHP, whose integer is a full 64 bits - so unlike TypeScript and Dart it needs no
    /// wider type for the values past 2^53.
    ///
    /// The trap here is in how those bytes are turned into one: `unpack('P')` hands back
    /// an unsigned interpretation that PHP cannot hold past 2^63 and silently makes a
    /// float of, which the corpus catches.
    /// </summary>
    [Fact]
    public void Generated_php_reader_matches_the_corpus()
    {
        var expected = Expected();

        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated PHP. {why}");

        var harness = ConformanceHarness.RunPhp(Scenario);
        Assert.True(harness.Succeeded, $"PHP harness failed.{Environment.NewLine}{harness.Output}");

        Compare("PHP", expected, Parse(harness.StdOut));
    }

    // ---------------------------------------------------------- comparison

    /// <summary>
    /// The exporter's JSON, converted into the canonical form the harness contract asks
    /// a harness to print.
    /// </summary>
    internal static List<Dictionary<string, string>> Expected()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json-named", "Vectors.json"));

        return Rows(json, fromExporter: true);
    }

    internal static List<Dictionary<string, string>> Parse(string json) => Rows(json, fromExporter: false);

    private static List<Dictionary<string, string>> Rows(string json, bool fromExporter)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateArray()
                       .Select(row => Canonicalize(row, fromExporter))
                       .ToList();
    }

    /// <summary>
    /// One row as a field name to canonical text map.
    ///
    /// Text rather than typed values, because the point of the comparison is that two
    /// readers agree about a value, and a string both sides derive the same way is the
    /// least ambiguous way to say so.
    /// </summary>
    private static Dictionary<string, string> Canonicalize(JsonElement row, bool fromExporter)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in row.EnumerateObject())
        {
            // A C++ harness prints the snake_case names its own accessor exposes, since
            // asking it to translate would put the mapping in the one place that cannot
            // be reviewed against the generated code.
            string name = ToCamelCase(property.Name);

            if (!FieldTypes.TryGetValue(name, out string type))
                throw new InvalidOperationException($"The corpus has no field `{property.Name}`.");

            result[name] = Render(property.Value, type, fromExporter);
        }

        return result;
    }

    private static string Render(JsonElement value, string type, bool fromExporter)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            string element = type.Substring(0, type.Length - 2);

            return "[" + string.Join(",", value.EnumerateArray()
                                               .Select(v => Render(v, element, fromExporter))) + "]";
        }

        switch (type)
        {
            case "int":
            case "enum":
                return value.GetInt32().ToString(CultureInfo.InvariantCulture);

            case "bigint":
                // A string on both sides: the exporter writes one because JSON's single
                // numeric type would round it, and the contract asks a harness to do the
                // same for the same reason.
                return value.GetString();

            case "float":
                // Compared at float precision. The exporter writes the shortest decimal
                // that round-trips the 32-bit value; a reader that widened it to a double
                // and printed that differs in the last digits while holding the same
                // value.
                return ((float)value.GetDouble()).ToString("R", CultureInfo.InvariantCulture);

            case "double":
                return value.GetDouble().ToString("R", CultureInfo.InvariantCulture);

            case "string":
                return value.GetString();

            case "bool":
                return value.GetBoolean() ? "true" : "false";

            case "uuid":
                return value.GetString().ToLowerInvariant();

            case "datetime":
                // The exporter writes a formatted timestamp; the contract asks a harness
                // for ticks, which are exact and have no formatting to disagree about.
                return fromExporter
                    ? DateTime.Parse(value.GetString(), CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind)
                              .Ticks.ToString(CultureInfo.InvariantCulture)
                    : value.GetString();

            case "timespan":
                return fromExporter
                    ? TimeSpan.Parse(value.GetString(), CultureInfo.InvariantCulture)
                              .Ticks.ToString(CultureInfo.InvariantCulture)
                    : value.GetString();

            default:
                throw new InvalidOperationException($"The corpus comparison has no rule for `{type}`.");
        }
    }

    internal static void Compare(
        string language,
        List<Dictionary<string, string>> expected,
        List<Dictionary<string, string>> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        var failures = new List<string>();

        for (int row = 0; row < expected.Count; row++)
        {
            foreach (var field in expected[row])
            {
                if (!actual[row].TryGetValue(field.Key, out string got))
                {
                    failures.Add($"row {row}: {language} printed no `{field.Key}`");
                    continue;
                }

                if (got != field.Value)
                    failures.Add($"row {row}, {field.Key}: exporter `{field.Value}` vs {language} `{got}`");
            }
        }

        Assert.True(failures.Count == 0,
            $"{language} disagrees with the exporter on {failures.Count} value(s):" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static string ToCamelCase(string name)
    {
        if (!name.Contains('_'))
            return name;

        var parts = name.Split('_');

        return parts[0] + string.Concat(parts.Skip(1)
            .Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
