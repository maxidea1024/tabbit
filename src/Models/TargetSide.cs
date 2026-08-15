using System;

namespace Tabbit.Models;

/// <summary>
/// Target side flags
/// </summary>
[Flags]
public enum TargetSide
{
    /// <summary>None</summary>
    None = 0,

    /// <summary>Client side only</summary>
    ClientOnly = 0x1,

    /// <summary>Server side only</summary>
    ServerOnly = 0x2,

    /// <summary>Client and Server sides</summary>
    Both = 0x3,
}

/// <summary>
/// Parsing and matching for the client/server side markers.
/// </summary>
public static class TargetSides
{
    /// <summary>
    /// Reads a side marker as written in a sheet cell or a recipe: "c", "s", "cs"
    /// or blank, which means both.
    /// </summary>
    public static bool TryParse(string text, out TargetSide side)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "":
            case "cs":
            case "sc":   side = TargetSide.Both; return true;
            case "c":    side = TargetSide.ClientOnly; return true;
            case "s":    side = TargetSide.ServerOnly; return true;
        }

        side = TargetSide.None;
        return false;
    }

    /// <summary>
    /// Whether something declared for <paramref name="declared"/> belongs in output
    /// built for <paramref name="requested"/>.
    ///
    /// This is a flag overlap, not equality: an entity marked for both sides
    /// belongs in client output and in server output alike, while a server-only
    /// entity belongs in neither the client build nor a client-only column set.
    /// </summary>
    public static bool Includes(TargetSide requested, TargetSide declared)
        => (requested & declared) != TargetSide.None;

    /// <summary>
    /// The side in words, for log lines and error messages.
    /// </summary>
    public static string Describe(TargetSide side)
    {
        return side switch
        {
            TargetSide.ClientOnly => "client",
            TargetSide.ServerOnly => "server",
            TargetSide.Both => "client and server",
            _ => "no",
        };
    }
}
