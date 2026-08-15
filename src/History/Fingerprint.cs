using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tabbit.History;

/// <summary>
/// Builds one content hash out of a sequence of components.
///
/// Every component is framed by its byte length before its bytes, which is the whole
/// reason this exists rather than a `string.Join` and a hash of the result. Joining
/// with a delimiter makes a value containing the delimiter indistinguishable from two
/// values, so a row holding the single string `a;b` would hash the same as a row
/// holding `a` and `b` - and the history would report no change where a designer had
/// split a column. A length frame cannot be forged by the content.
///
/// Framing also separates a missing value from an empty one. Both are common in a
/// sheet and they are not the same edit, so <see cref="AddAbsent"/> writes a length no
/// string can produce.
///
/// SHA-256, not MD5. <see cref="Manifest"/> uses MD5 to notice that a generated file
/// changed, where a collision costs a skipped copy. These hashes decide whether a
/// value is already in the store, so a collision would silently return the wrong
/// value's history.
/// </summary>
public sealed class Fingerprint : IDisposable
{
    /// <summary>Frame length written for a component that is absent rather than empty.</summary>
    private const int AbsentLength = -1;

    /// <summary>Below this, the UTF-8 encode goes on the stack. Above it, a pooled buffer.</summary>
    private const int StackLimit = 256;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    private bool _completed;

    /// <summary>Adds one component. A null is recorded as absent, not as empty.</summary>
    public Fingerprint Add(string? text)
    {
        if (text is null)
            return AddAbsent();

        int byteCount = Encoding.UTF8.GetByteCount(text);
        WriteLength(byteCount);

        if (byteCount <= StackLimit)
        {
            Span<byte> buffer = stackalloc byte[StackLimit];
            _hash.AppendData(buffer.Slice(0, Encoding.UTF8.GetBytes(text, buffer)));
        }
        else
        {
            // Pooled rather than allocated: this runs once per cell, and a project's
            // sheets hold millions of them.
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                _hash.AppendData(rented.AsSpan(0, Encoding.UTF8.GetBytes(text, rented)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return this;
    }

    /// <summary>Adds a component that is not there at all, distinctly from an empty one.</summary>
    public Fingerprint AddAbsent()
    {
        WriteLength(AbsentLength);
        return this;
    }

    /// <summary>Adds a number, rendered the same way on every machine.</summary>
    public Fingerprint Add(int value) => Add(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Adds a flag.</summary>
    public Fingerprint Add(bool value) => Add(value ? "1" : "0");

    /// <summary>
    /// Folds in a hash computed elsewhere, which is how a table's hash is built from
    /// its rows' without holding every cell of every row at once.
    /// </summary>
    public Fingerprint AddDigest(string hex) => Add(hex);

    /// <summary>
    /// The hash, lower-case hex. The instance is spent afterwards.
    /// </summary>
    public string Complete()
    {
        if (_completed)
            throw new InvalidOperationException("This fingerprint has already been completed.");

        _completed = true;

        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose() => _hash.Dispose();

    private void WriteLength(int length)
    {
        Span<byte> frame = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);

        _hash.AppendData(frame);
    }
}
