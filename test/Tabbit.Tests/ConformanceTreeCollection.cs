using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The classes that build inside the `conformance` scenario's generated tree.
/// </summary>
/// <remarks>
/// **A shared build directory, not a shared read.** A conversion is memoised per scenario, so
/// any number of classes may read one tree at once; what cannot overlap is building in it -
/// and the conformance harnesses copy a driver into the generated module and compile it
/// there, because that is the only place the generated types are importable from.
///
/// Two classes doing that at once write the same object files and the same executable, and
/// the loser reads half of the other's. That is what failed when the suite was first run in
/// parallel: the unreal, C and C# harnesses, plus the C header check compiling the same
/// headers while a driver was being built beside them.
///
/// **One collection rather than one per class**, which is what makes them serial against each
/// other and parallel against everything else - the other forty-odd scenario groups are
/// untouched by this. The narrower fix is for each harness to build in a copy, the way
/// `_lang/&lt;scenario&gt;/&lt;language&gt;` already does for the ones that were moved; this is the
/// boundary until that is finished for the rest.
///
/// doc/roadmap.md, the suite-parallelism entry.
/// </remarks>
[CollectionDefinition("conformance-tree")]
public sealed class ConformanceTreeCollection
{
}
