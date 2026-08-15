// Shared code: compiled into every rule file's compilation, and never run on its own.
//
// Here because the two table rules ask the same question about text columns, and a rule
// folder is ordinary C# - what a project uses twice becomes a helper rather than a copy.

internal static class Naming
{
    /// <summary>Whether a text column was filled in at all.</summary>
    internal static bool IsFilled(string value) => !string.IsNullOrWhiteSpace(value);
}
