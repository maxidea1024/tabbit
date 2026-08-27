using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The classes that convert the `core-server` scenario.
/// </summary>
/// <remarks>
/// **One of them narrows that run further from the command line**, and a run with an
/// option on it is not the shared conversion - it clears the tree and writes a different
/// one. The other class reads the tree the plain conversion left. Serial, the second
/// conversion simply happened after the first class was done; in parallel it happens
/// while it is reading.
///
/// Two classes and one tree, so a collection is the whole fix. Where the same shape
/// appeared with a dozen readers - `core` - the writer was moved to a scenario of its
/// own instead, because serializing twelve classes costs the parallelism they are the
/// reason for.
///
/// doc/roadmap.md, the suite-parallelism entry.
/// </remarks>
[CollectionDefinition("core-server-tree")]
public sealed class CoreServerTreeCollection
{
}
