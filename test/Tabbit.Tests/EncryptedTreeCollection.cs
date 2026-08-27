using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The classes that convert the `encrypted` scenario.
/// </summary>
/// <remarks>
/// **Each of their tests converts it again, and that is deliberate**: one of them edits the
/// exported file to check that the MAC refuses it, so the next test needs a tree nobody has
/// written on. A conversion that names environment variables - the key and the MAC key, here
/// - is a particular run rather than the shared one, so it happens every time by design.
///
/// Which is fine until two classes do it at once. Then two conversions write one output tree
/// and claim the same staging file, and the run fails with the tool's name on it. Sharing the
/// answer instead is not open to these: the point of converting again is the fresh tree.
///
/// doc/roadmap.md, the suite-parallelism entry.
/// </remarks>
[CollectionDefinition("encrypted-tree")]
public sealed class EncryptedTreeCollection
{
}
