using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That a table keyed by something other than `int` or `string` compiles.
/// </summary>
/// <remarks>
/// The `key-types` golden records what the thirteen generators emit for a `bigint`, `uuid` or
/// `enum` key. A golden cannot say whether it builds, and this is the one feature where that
/// gap is the whole risk: every generator declares a dictionary over the key's own type, so
/// the failure mode is not a wrong value but a type that the language will not accept as a
/// key - and text compared against text is happy either way.
///
/// Recording the golden found exactly that, twice, in the two languages checked here:
///
///   * PHP subscripted its array with the key object. PHP arrays take an `int` or a `string`
///     and raise a `TypeError` on anything else, so `findByIndex` was a method that could not
///     run. Reachable before this fixture existed - a `*` column has accepted an enum all
///     along - and nothing had tried one.
///   * C++ named `std::unordered_map<tabbit::Uuid, ...>` with no `std::hash` specialization
///     for `Uuid`, so the table declared a member the standard library cannot instantiate.
///
/// PHP has no compile step to gate, so what stands behind it is the golden plus the
/// conversion of the offsets, read. C++ is checked here properly, because it is a compiler
/// that has the answer.
/// </remarks>
public class KeyTypeCompileTests
{
    private const string Scenario = "key-types";

    /// <summary>
    /// The C# a `uuid`-keyed and an `enum`-keyed table generate, compiled for a plain
    /// consumer.
    /// </summary>
    /// <remarks>
    /// `Dictionary<System.Guid, Record>` and `Dictionary<Slot, Record>` are the declarations
    /// at issue. Both are ordinary C#, which is the point: the check is that the generator
    /// spelled the key's type the way the rest of the file spells that type, rather than
    /// emitting the wire's name for it.
    /// </remarks>
    [Fact]
    public void Generated_cs_compiles_for_every_key_type()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(Scenario, "KeyTypesAccessor");

        Assert.True(result.Succeeded,
            $"Generated C# for a non-int key does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The same in C++, which is where the missing hash was.
    /// </summary>
    /// <remarks>
    /// A compile rather than a build-and-run: what was broken is a declaration, so it fails
    /// at the point the table type is instantiated and there is nothing further to observe by
    /// reading rows back. The round trip is `core`'s job and it covers uuid *columns* already.
    /// </remarks>
    [Fact]
    public void Generated_cpp_compiles_for_every_key_type()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // turns itself off silently is worse than no gate.
        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        var result = CppToolchain.Compile(Scenario, "KeyTypesAccessor");

        Assert.True(result.Succeeded,
            $"Generated C++ for a non-int key does not compile.{Environment.NewLine}{result.Output}");
    }
}
