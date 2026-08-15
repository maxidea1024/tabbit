using System;
using System.Buffers.Binary;

namespace Tabbit.Exporters;

/// <summary>
/// The ChaCha20 stream cipher of RFC 8439, as the format's file envelope uses it.
/// </summary>
/// <remarks>
/// Chosen for what it costs to have everywhere rather than for being the strongest thing
/// available. The layer it serves is a modest one - the key travels inside the client that
/// reads the file, so an attacker who can take the key out of a binary is not being stopped
/// by anything here. What it removes is that a data file opens in an editor and reads as
/// plain text, and that a value edited into it loads as if it belonged.
///
/// Against that purpose the properties that matter are: no external dependency in any of the
/// runtimes that have to read the file, no hardware acceleration needed to be fast enough,
/// and a length that does not change - the envelope is a keystream XOR, so every structural
/// check the format already makes about block lengths still holds over the ciphertext.
///
/// Implemented here rather than taken from the platform because the platform's offering is an
/// authenticated construction, which changes the length and would mean the format carried a
/// tag it has no use for.
/// </remarks>
public static class ChaCha20
{
    /// <summary>Key length in bytes. The 256-bit variant is the only one the format allows.</summary>
    public const int KeySize = 32;

    /// <summary>Nonce length in bytes, as RFC 8439 fixes it.</summary>
    public const int NonceSize = 12;

    private const int BlockSize = 64;

    /// <summary>
    /// Exclusive-ors the keystream over <paramref name="data"/>, in place.
    /// </summary>
    /// <remarks>
    /// The same call both encrypts and decrypts, which is what a stream cipher is: the
    /// keystream depends only on the key, the nonce and the position, so applying it twice
    /// returns what went in. The block counter starts at zero and counts the 64-byte blocks
    /// from the start of <paramref name="data"/>.
    /// </remarks>
    /// <param name="counter">
    /// Which 64-byte block of the keystream the data starts at. The format always begins at
    /// zero; the parameter exists so the specification's own test vectors, which start at
    /// one, can be run against this code unchanged.
    /// </param>
    public static void Apply(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, Span<byte> data, uint counter = 0)
    {
        if (key.Length != KeySize)
            throw new TabbitException($"A ChaCha20 key is {KeySize} bytes, not {key.Length}.");

        if (nonce.Length != NonceSize)
            throw new TabbitException($"A ChaCha20 nonce is {NonceSize} bytes, not {nonce.Length}.");

        Span<uint> state = stackalloc uint[16];
        Span<uint> working = stackalloc uint[16];
        Span<byte> keystream = stackalloc byte[BlockSize];

        // "expand 32-byte k", as four little-endian words. The constant is what tells a
        // ChaCha state apart from one of the same shape built any other way.
        state[0] = 0x61707865;
        state[1] = 0x3320646e;
        state[2] = 0x79622d32;
        state[3] = 0x6b206574;

        for (int at = 0; at < 8; at++)
            state[4 + at] = BinaryPrimitives.ReadUInt32LittleEndian(key[(at * 4)..]);

        state[12] = counter;

        for (int at = 0; at < 3; at++)
            state[13 + at] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[(at * 4)..]);

        for (int offset = 0; offset < data.Length; offset += BlockSize)
        {
            Block(state, working, keystream);

            int count = Math.Min(BlockSize, data.Length - offset);

            for (int at = 0; at < count; at++)
                data[offset + at] ^= keystream[at];

            state[12]++;
        }
    }

    /// <summary>One 64-byte keystream block: twenty rounds over a copy of the state.</summary>
    private static void Block(ReadOnlySpan<uint> state, Span<uint> working, Span<byte> keystream)
    {
        state.CopyTo(working);

        // Ten double rounds. Each is four column quarter-rounds and four diagonal ones,
        // which between them let every word reach every other.
        for (int round = 0; round < 10; round++)
        {
            QuarterRound(working, 0, 4, 8, 12);
            QuarterRound(working, 1, 5, 9, 13);
            QuarterRound(working, 2, 6, 10, 14);
            QuarterRound(working, 3, 7, 11, 15);

            QuarterRound(working, 0, 5, 10, 15);
            QuarterRound(working, 1, 6, 11, 12);
            QuarterRound(working, 2, 7, 8, 13);
            QuarterRound(working, 3, 4, 9, 14);
        }

        // Added back to the original state, which is what stops the rounds from being
        // reversible and so the keystream from being recoverable.
        for (int at = 0; at < 16; at++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                keystream[(at * 4)..], unchecked(working[at] + state[at]));
        }
    }

    private static void QuarterRound(Span<uint> block, int a, int b, int c, int d)
    {
        unchecked
        {
            block[a] += block[b]; block[d] = RotateLeft(block[d] ^ block[a], 16);
            block[c] += block[d]; block[b] = RotateLeft(block[b] ^ block[c], 12);
            block[a] += block[b]; block[d] = RotateLeft(block[d] ^ block[a], 8);
            block[c] += block[d]; block[b] = RotateLeft(block[b] ^ block[c], 7);
        }
    }

    private static uint RotateLeft(uint value, int count)
        => (value << count) | (value >> (32 - count));
}
