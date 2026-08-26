using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// That every value type a field can hold is in the conformance corpus.
///
/// The generators each carry their own switch over `ValueType` deciding which reader call
/// a field turns into. Adding a type means teaching all twelve, and forgetting one still
/// compiles: the switch has a `default:` that throws, so it is a runtime failure in whoever's
/// project reaches that field first.
///
/// What makes that survivable is that the corpus has a field of every type and twelve
/// harnesses read it. A missing case does not reach a consumer - it fails here, at once, with
/// the language named.
///
/// Which leaves one hole, and this closes it: that argument only holds while the corpus really
/// does have every type. A thirteenth type nobody added to the sheets is a type no reader is
/// ever run against, and the twelve switches go back to being twelve places to forget.
///
/// So the type list is read from the enum rather than written down here. Add a `ValueType` and
/// this fails the same day, naming it - before any of the generators have been touched.
/// </summary>
public class CorpusCoverageTests
{
    private const string Scenario = "conformance";

    /// <summary>
    /// The types that are not something a sheet can declare a field as.
    /// </summary>
    /// <remarks>
    /// `None` is the absence of a type. `Unresolved` is what a reference holds between parsing
    /// and resolution, and never survives into a model.
    ///
    /// **`ForeignRecordArray` used to be here and is not any more.** It was excluded on the
    /// grounds that the cooker refused `foreign[]`, and it does not - spec/types/polymorphism.md
    /// section 4 opened it - so the corpus carries an `owners` column and every harness reads
    /// it. That column is the only place a reader has to allocate the resolved slots from the
    /// row's own element count rather than assign into a record it already sized.
    ///
    /// Named individually rather than by a rule, because the next type added should have to
    /// be thought about rather than swept in by a predicate somebody wrote for these.
    /// </remarks>
    private static readonly HashSet<ValueType> NotAFieldType = new HashSet<ValueType>
    {
        ValueType.None,
        ValueType.Unresolved,
    };

    /// <summary>
    /// Types a sheet can declare that no generator ever sees, because the cooker folds them
    /// first.
    /// </summary>
    /// <remarks>
    /// `bitset` is a type for exactly as long as parsing lasts - what makes it one is the
    /// notation it accepts - and <see cref="Tabbit.Cooking.ModelCooker"/> turns it into a
    /// 64-bit integer once every cell has been read. So the argument this gate rests on does
    /// not apply to it: there is no switch in any generator with a `Bitset` case to forget,
    /// and a corpus column would be testing the `bigint` path a second time.
    ///
    /// What does hold it is a different gate - the `bitset` golden exports the same values as
    /// a `bigint` column beside it and every artifact has to agree, which is the fold itself
    /// being checked rather than a reader's switch. spec/types/bitset.md.
    ///
    /// The composites are the same argument one step further. A `vec3f` column becomes three
    /// `float` columns before any generator runs, so a corpus column of one would be reading
    /// the `float` path three more times. What holds them is `CompositeExpansionTests`, where
    /// the same table written both ways has to produce the same bytes.
    /// spec/types/composite-value-types.md.
    /// </remarks>
    private static readonly HashSet<ValueType> FoldedBeforeGeneration =
        new HashSet<ValueType>(Tabbit.Models.CompositeTypes.All.Select(entry => entry.Type))
        {
            ValueType.Bitset,
            ValueType.BitsetArray,
        };

    /// <summary>
    /// Array forms deliberately left out, and why.
    /// </summary>
    /// <remarks>
    /// Writing this gate found eight array forms no reader had ever run against. Two were
    /// worth the columns and now have them: an enum element goes through a cast, and in C
    /// through a scratch variable, and a uuid element is sixteen bytes rather than a value -
    /// so in both the element read is something other than the scalar call in a loop.
    ///
    /// These six are that exact shape: the same length-prefixed loop as `int[]`, with the
    /// same scalar call already read as a column of its own. Covering them would be twelve
    /// more harness lines each for a composition of two things both already covered.
    ///
    /// A judgement, not a fact, which is why it is a list with a reason rather than a rule
    /// that quietly grows. If one of these ever turns out to have its own path in some
    /// target, it belongs in the corpus and not here.
    /// </remarks>
    private static readonly HashSet<ValueType> ArrayFormsCoveredByTheirScalar = new HashSet<ValueType>
    {
        ValueType.BoolArray,
        ValueType.Int64Array,
        ValueType.FloatArray,
        ValueType.DoubleArray,
        ValueType.DateTimeArray,
        ValueType.TimeSpanArray,
    };

    [Fact]
    public void The_corpus_has_a_field_of_every_value_type()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var present = TypesInTheCorpus();

        var missing = System.Enum.GetValues<ValueType>()
            .Where(type => !NotAFieldType.Contains(type) && !FoldedBeforeGeneration.Contains(type))
            .Where(type => !ArrayFormsCoveredByTheirScalar.Contains(type))
            .Where(type => !present.Contains(type.ToString()))
            .ToList();

        Assert.True(missing.Count == 0,
            "The conformance corpus has no field of these types, so no language's reader is " +
            "ever run against them:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(type => $"  {type}")) +
            Environment.NewLine + Environment.NewLine +
            "Add a column to WriteConformance in test/fixtures/tools/FixtureGen/Program.cs, " +
            "regenerate the workbook, and give each of the harnesses a line for it. " +
            "That is the work adding a value type costs, and this is where the bill arrives.");
    }

    /// <summary>
    /// And the array forms whose element read is its own path.
    /// </summary>
    /// <remarks>
    /// Separate from the test above because it fails for a different reason. A scalar missing
    /// means nobody reads that type at all; one of these missing means the length-prefixed
    /// path is unread for an element type that does not read like the others - which is where
    /// a reader gets the count right and the element wrong.
    ///
    /// Named rather than derived, so that dropping a column from the corpus is a test failure
    /// and not a quieter corpus.
    /// </remarks>
    [Fact]
    public void The_corpus_reads_the_array_forms_that_have_their_own_path()
    {
        TabbitRunner.Convert(Scenario);

        var present = TypesInTheCorpus();

        foreach (var expected in new[]
                 {
                     ValueType.Int32Array,   // the plain one
                     ValueType.StringArray,  // a length-prefixed element inside a length-prefixed array
                     ValueType.EnumArray,    // an element that needs a cast, and in C a scratch variable
                     ValueType.UuidArray,    // an element that is sixteen bytes rather than a value
                 })
        {
            Assert.Contains(expected.ToString(), present);
        }
    }

    /// <summary>
    /// Every `type` the summary records for a field, across every table in the corpus.
    /// </summary>
    /// <remarks>
    /// Through the summary rather than by parsing the workbook, because the summary is the
    /// cooked model - which is what the generators see. A column whose type the cooker rewrote
    /// on the way through would otherwise be counted as the type the sheet spelled.
    /// </remarks>
    private static IReadOnlySet<string> TypesInTheCorpus()
    {
        string path = Path.Combine(RepoLayout.OutputDir(Scenario), "summary", "summary.json");

        Assert.True(File.Exists(path), $"The corpus wrote no summary at {path}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in document.RootElement.GetProperty("data").GetProperty("tables").EnumerateArray())
        {
            foreach (var field in table.GetProperty("fields").EnumerateArray())
                found.Add(field.GetProperty("type").GetString());
        }

        Assert.NotEmpty(found);

        return found;
    }
}
