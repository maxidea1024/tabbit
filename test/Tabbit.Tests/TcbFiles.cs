using Tabbit.Exporters;

namespace Tabbit.Tests;

/// <summary>
/// Table files built by hand, for the tests that are about the envelope rather than about
/// the data.
/// </summary>
/// <remarks>
/// The header is fixed-width and the layers work over it in place, so a file shaped like one
/// is enough to seal, sign, alter and open - and building it here keeps the offsets in one
/// place, where the format's own constants can name them.
/// </remarks>
internal static class TcbFiles
{
    /// <summary>
    /// A file with a valid header and a body of arbitrary but repeatable bytes.
    /// </summary>
    public static byte[] Plain(int bodyLength)
    {
        var writer = new TcbWriter();

        TcbFormat.WriteHeader(writer);

        for (int at = 0; at < bodyLength; at++)
            writer.Write((byte)((at * 31) % 253));

        return writer.WrittenSpan.ToArray();
    }
}
