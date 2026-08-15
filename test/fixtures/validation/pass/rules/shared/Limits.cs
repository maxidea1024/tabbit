// Shared code: compiled into every rule file's compilation, and never run on its own.
//
// This is where the helpers a project uses more than once go. It is ordinary C#, so it
// declares types and methods rather than statements.

internal static class Limits
{
    /// <summary>What this fixture calls a plausible item count.</summary>
    internal const int MostItems = 1000;

    internal static string Describe(int count) => $"{count} of at most {MostItems}";
}
