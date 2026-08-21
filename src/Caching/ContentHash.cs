using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tabbit.History;

namespace Tabbit.Caching;

/// <summary>
/// The hashes the cache compares.
/// </summary>
/// <remarks>
/// SHA-256 rather than the MD5 <see cref="Manifest"/> uses. A manifest hash decides whether
/// a generated file needs copying, where a collision costs one skipped copy; these decide
/// whether a whole conversion can be skipped, where a collision means shipping the previous
/// build's data under this build's name.
///
/// Streamed rather than read whole. A real project's workbook is tens of megabytes and a
/// source directory is a gigabyte of them, so hashing by reading every file into an array
/// would peak at the size of the largest one for no reason.
/// </remarks>
internal static class ContentHash
{
    /// <summary>Nothing at all, distinct from the hash of an empty file.</summary>
    public const string None = "";

    /// <summary>Hash of a file's contents, lower-case hex.</summary>
    public static string OfFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024,
            FileOptions.SequentialScan);

        using var sha = SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>Hash of a string's UTF-8 bytes, lower-case hex.</summary>
    public static string OfText(string text)
    {
        using var sha = SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>
    /// One hash over several components, each framed by its length.
    /// </summary>
    /// <remarks>
    /// Through <see cref="Fingerprint"/>, which frames every component by its byte length
    /// so that a value containing the separator cannot be mistaken for two values - the
    /// reasoning is written out there. A key built by joining with a delimiter would treat
    /// two different configurations as one.
    /// </remarks>
    public static string OfParts(params string?[] parts)
    {
        using var fingerprint = new Fingerprint();

        foreach (var part in parts)
            fingerprint.Add(part);

        return fingerprint.Complete();
    }

    /// <summary>
    /// One hash over a sequence of names, in the order given.
    /// </summary>
    /// <remarks>
    /// For a directory listing, where what is being hashed is the answer to "which files are
    /// here" - so the order has to be settled by the caller rather than by the filesystem,
    /// which does not agree across platforms.
    /// </remarks>
    public static string OfNames(IEnumerable<string> names, out int count)
    {
        using var fingerprint = new Fingerprint();

        count = 0;

        foreach (var name in names)
        {
            fingerprint.Add(name);
            count++;
        }

        return fingerprint.Complete();
    }
}
