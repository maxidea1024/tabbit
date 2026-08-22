using System;
using System.Security.Cryptography;

namespace Tabbit.Exporters;

/// <summary>
/// The tag that says a table file is the one the exporter wrote, byte for byte.
/// </summary>
/// <remarks>
/// What it is for, stated against what the structural checks already do: a block length that
/// does not add up, a run whose lengths overshoot the rows, a dictionary index out of range -
/// those are the ways a file can be malformed, and the format catches all of them. None of
/// them is what an edited file looks like. A fixed-width element accepts every bit pattern,
/// so replacing four bytes of an f32 column with four other bytes leaves a file that is
/// correct in every way the reader can check and holds a different number.
///
/// Encryption does not close that either. The envelope is a keystream XOR, so flipping a bit
/// of the ciphertext flips the same bit of the plaintext without the key being involved at
/// all - the file still decrypts, the key check still passes, and the structure still adds
/// up. A stream cipher hides a value; it does not fix it in place.
///
/// The honest limit is the same one the encryption has. The MAC key ships inside the client,
/// so an attacker who can take it out of a binary can compute a tag for whatever they like.
/// What changes is who that is: editing a data file goes from something a text editor does to
/// something that needs the client pulled apart first.
/// </remarks>
public static class TcbMac
{
    /// <summary>Key length in bytes. The same shape as an encryption key, and a different key.</summary>
    public const int KeySize = 32;

    /// <summary>
    /// Writes the tag for <paramref name="file"/> into its own MAC field.
    /// </summary>
    /// <remarks>
    /// Encrypt-then-MAC: this runs over the bytes as they will be stored, ciphertext and
    /// all. Two things follow, and both are the reason for the order. A reader verifies
    /// before it decrypts, so an altered file is refused without a key ever being used on
    /// it. And the header is covered - the flags, the cipher byte, the nonce and the
    /// version are all authenticated, where a tag over the plaintext would let the nonce be
    /// swapped underneath it.
    /// </remarks>
    public static void Sign(Span<byte> file, ReadOnlySpan<byte> key)
    {
        if (file.Length < TcbFormat.HeaderSize)
            throw new TabbitDefectException("A table file too short to hold a header cannot be signed.");

        Compute(file, key, file.Slice(TcbFormat.MacOffset, TcbFormat.MacSize));
    }

    /// <summary>
    /// Whether the file's MAC field is the tag its own bytes produce under this key.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> file, ReadOnlySpan<byte> key)
    {
        if (file.Length < TcbFormat.HeaderSize)
            return false;

        Span<byte> expected = stackalloc byte[TcbFormat.MacSize];
        Compute(file, key, expected);

        return CryptographicOperations.FixedTimeEquals(
            expected, file.Slice(TcbFormat.MacOffset, TcbFormat.MacSize));
    }

    /// <summary>Whether the file states a MAC at all. All zero means it does not.</summary>
    public static bool IsPresent(ReadOnlySpan<byte> file)
    {
        if (file.Length < TcbFormat.HeaderSize)
            return false;

        foreach (byte value in file.Slice(TcbFormat.MacOffset, TcbFormat.MacSize))
        {
            if (value != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// HMAC-SHA-256 over everything but the MAC field, truncated to the field's width.
    /// </summary>
    /// <remarks>
    /// Two segments rather than one, because the sixteen bytes a tag is written into cannot
    /// be part of what produces it. Skipping them is the same as zeroing them and cheaper by
    /// a copy of the file.
    ///
    /// HMAC-SHA-256 rather than the Poly1305 that pairs with the format's cipher, and the
    /// reason is porting cost rather than cryptography. Several of the other runtimes have
    /// HMAC-SHA-256 in their standard library; none of them exposes Poly1305 on its own,
    /// because platforms ship it welded into an AEAD. Sixteen bytes of tag is the truncation
    /// RFC 4868 names, and 128 bits is past the point where the difference is one anybody
    /// can act on.
    /// </remarks>
    private static void Compute(ReadOnlySpan<byte> file, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        if (key.Length != KeySize)
            throw new TabbitDefectException($"A MAC key is {KeySize} bytes, not {key.Length}.");

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);

        hmac.AppendData(file[..TcbFormat.MacOffset]);
        hmac.AppendData(file[TcbFormat.KeyCheckOffset..]);

        Span<byte> full = stackalloc byte[32];
        hmac.GetHashAndReset(full);

        full[..TcbFormat.MacSize].CopyTo(destination);
    }
}
