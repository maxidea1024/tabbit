using System;
using System.IO;
using System.Security.Cryptography;

using Tabbit.Recipe;

namespace Tabbit.Exporters;

/// <summary>
/// The encrypted form of a table file: what fills in the header the encoder reserved, and
/// where the key comes from.
/// </summary>
/// <remarks>
/// A layer against casual inspection and casual editing of data that ships inside a client.
/// The threat model is stated plainly because it bounds what this is worth: the key travels
/// in the client that reads the file, so anyone able to take it out of a binary is not being
/// stopped. No format stops that one. What this removes is that the file opens in an editor
/// and reads as text.
///
/// What it does not remove is that a value can be edited into it. A keystream XOR is
/// malleable - flipping a bit of the ciphertext flips the same bit of the plaintext - so
/// nothing here makes an altered file fail to load. That is what <see cref="TcbMac"/> is for,
/// and it is a separate layer with a separate key because the two answer different questions.
///
/// The envelope is the outermost layer bar the MAC. Encodings run first and a compression
/// layer, if the format ever gains one, would run second; both of those work by finding
/// repetition, and there is none left to find above a cipher. So encryption is always last on
/// the way out and first on the way in, and the MAC is computed over what encryption left.
/// </remarks>
public static class TcbEnvelope
{
    /// <summary>
    /// Seals a finished table file in place: the nonce, the flags, and the ciphertext.
    /// </summary>
    /// <remarks>
    /// The nonce is the plaintext's own SHA-256, not a random number. A random one would make
    /// two exports of identical data two different files, which would break the golden trees
    /// and every other place this tool promises that the same input gives the same bytes.
    /// What that gives up is that identical contents are visibly identical, which this threat
    /// model accepts. What it keeps is the property that actually matters for a stream
    /// cipher: different contents under the same key get different nonces, so no keystream is
    /// ever used twice.
    ///
    /// In place, over the header the encoder already wrote. Nothing is prepended and nothing
    /// moves: the fields this fills in were reserved at fixed offsets exactly so that sealing
    /// a file does not change where anything in it lives.
    /// </remarks>
    public static void Seal(Span<byte> file, ReadOnlySpan<byte> key)
    {
        if (file.Length < TcbFormat.HeaderSize)
            throw new TabbitDefectException("A table file too short to hold a header cannot be encrypted.");

        // From the key check to the end: everything that is not the plaintext header.
        var payload = file[TcbFormat.KeyCheckOffset..];

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(payload, digest);

        var nonce = digest[..TcbFormat.NonceSize];

        nonce.CopyTo(file.Slice(TcbFormat.NonceOffset, TcbFormat.NonceSize));

        file[TcbFormat.FlagsOffset] |= TcbFormat.FlagEncrypted;
        file[TcbFormat.CipherOffset] = TcbFormat.CipherChaCha20;

        ChaCha20.Apply(key, nonce, payload);
    }

    /// <summary>
    /// The reverse, for the tests that check a file round-trips and for reading one back.
    /// </summary>
    /// <remarks>
    /// What comes back is the file the encoder produced, byte for byte: the fields this layer
    /// filled in are returned to the zeros they had before it ran. So a sealed file and a
    /// plain export of the same table differ in exactly the bytes the envelope wrote, which
    /// is what makes a round trip assertable rather than approximately assertable.
    /// </remarks>
    public static byte[] Open(ReadOnlySpan<byte> sealedFile, ReadOnlySpan<byte> key)
    {
        if (sealedFile.Length < TcbFormat.HeaderSize)
            throw new TabbitDefectException("A file too short to hold a header cannot be decrypted.");

        if ((sealedFile[TcbFormat.FlagsOffset] & TcbFormat.FlagEncrypted) == 0)
            throw new TabbitException("The file is not encrypted.");

        if (sealedFile[TcbFormat.CipherOffset] != TcbFormat.CipherChaCha20)
        {
            throw new TabbitException(
                $"Cipher {sealedFile[TcbFormat.CipherOffset]} is not one this build knows.");
        }

        var plaintext = sealedFile.ToArray();
        var nonce = plaintext.AsSpan(TcbFormat.NonceOffset, TcbFormat.NonceSize).ToArray();

        ChaCha20.Apply(key, nonce, plaintext.AsSpan(TcbFormat.KeyCheckOffset));

        if (!plaintext.AsSpan(TcbFormat.KeyCheckOffset, TcbFormat.Magic.Length)
                      .SequenceEqual(TcbFormat.Magic))
        {
            throw new TabbitException(
                "The file did not decrypt to a table. The key is not the one it was written with.");
        }

        plaintext[TcbFormat.FlagsOffset] &= unchecked((byte)~TcbFormat.FlagEncrypted);
        plaintext[TcbFormat.CipherOffset] = TcbFormat.CipherNone;
        plaintext.AsSpan(TcbFormat.NonceOffset, TcbFormat.NonceSize).Clear();

        return plaintext;
    }

    /// <summary>
    /// A new key, drawn from the operating system's random source, in the form a recipe reads.
    /// </summary>
    /// <remarks>
    /// From the cryptographic generator rather than a general one. The distinction is the
    /// whole of a key's value: a number that can be predicted from when it was made is not a
    /// secret, however many bytes long it is.
    ///
    /// One command for both keys, because both are thirty-two random bytes and a second
    /// command would differ from this one only in the sentence it prints.
    /// </remarks>
    public static string NewKey()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(ChaCha20.KeySize)).ToLowerInvariant();

    /// <summary>
    /// The two keys a recipe entry asks for, either of which may be null.
    /// </summary>
    /// <remarks>
    /// Never the keys themselves. A recipe is committed and passed around, and a key written
    /// into one is a key in the history of a repository from then on. What the recipe holds is
    /// the name of an environment variable or the path of a file, and which of the two it is
    /// is the user's decision about where a secret lives on their machines.
    ///
    /// Both are read in one call so that the rules that involve both of them have one place to
    /// live, and so that a missing key stops the run before a directory is half written.
    /// </remarks>
    public static void KeysFor(
        BinaryRecipe recipe, out byte[]? encryption, out byte[]? mac)
    {
        encryption = KeyFrom(
            recipe.EncryptionKeyVariable, recipe.EncryptionKeyFile,
            "encryption", "EncryptionKeyVariable", "EncryptionKeyFile");

        mac = KeyFrom(
            recipe.MacKeyVariable, recipe.MacKeyFile,
            "MAC", "MacKeyVariable", "MacKeyFile");

        // One secret used as two primitives is not a known weakness, and avoiding it costs
        // nothing but a second variable - which is the whole argument for refusing here.
        if (encryption != null && mac != null && encryption.AsSpan().SequenceEqual(mac))
        {
            throw new TabbitException(
                "A binary export uses the same key for encryption and for the MAC. "
                + "Make a second key with `--new-encryption-key`, so that one secret is not "
                + "doing the work of two.");
        }
    }

    /// <summary>The key one pair of recipe settings names, or null when they name none.</summary>
    private static byte[]? KeyFrom(
        string variable, string filename, string purpose, string variableSetting, string fileSetting)
    {
        bool fromEnvironment = !string.IsNullOrEmpty(variable);
        bool fromFile = !string.IsNullOrEmpty(filename);

        if (fromEnvironment && fromFile)
        {
            throw new TabbitException(
                $"A binary export names both `{variableSetting}` and `{fileSetting}`. "
                + $"Name one, so there is no question which {purpose} key the files were written with.");
        }

        if (!fromEnvironment && !fromFile)
            return null;

        if (fromEnvironment)
        {
            string? text = Environment.GetEnvironmentVariable(variable);

            if (string.IsNullOrEmpty(text))
            {
                throw new TabbitException(
                    $"The binary export asks for the {purpose} key in environment variable "
                    + $"`{variable}`, which is not set.");
            }

            return Parse(text.Trim(), $"environment variable `{variable}`", purpose);
        }

        string path = Path.GetFullPath(filename);

        if (!File.Exists(path))
        {
            throw new TabbitException(
                $"The binary export asks for the {purpose} key in `{path}`, which does not exist.");
        }

        return Parse(File.ReadAllText(path).Trim(), $"key file `{path}`", purpose);
    }

    /// <summary>
    /// A key as 64 hexadecimal characters.
    /// </summary>
    /// <remarks>
    /// Hexadecimal rather than raw bytes because a key has to survive being pasted into an
    /// environment variable, a secret store and a shell, and because a file of 32 arbitrary
    /// bytes is one a text editor will silently damage.
    /// </remarks>
    private static byte[] Parse(string text, string origin, string purpose)
    {
        if (text.Length != ChaCha20.KeySize * 2)
        {
            throw new TabbitException(
                $"The {purpose} key in {origin} is {text.Length} characters. "
                + $"It has to be {ChaCha20.KeySize * 2} hexadecimal characters, for {ChaCha20.KeySize} bytes.");
        }

        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            throw new TabbitException($"The {purpose} key in {origin} is not hexadecimal.");
        }
    }
}
