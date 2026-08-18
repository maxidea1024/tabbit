using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tabbit.Importers.Xlsx;

/// <summary>
/// The record stream a binary workbook's parts are made of.
/// </summary>
/// <remarks>
/// A record is `[type][length][body]`, where the type and the length are 7-bit
/// variable-length integers. Reading one means knowing where the next begins, and nothing
/// else - a body this program has no use for is skipped by its length.
///
/// Shared by the two things that read these parts: the defined names in
/// <see cref="BinaryDefinedNames"/>, and the row repair in <see cref="XlsbRowRepair"/>.
/// </remarks>
internal static class XlsbRecords
{
    /// <summary>Record types this program reads, by their number in MS-XLSB.</summary>
    public const int RowHeader = 0;
    public const int CellBlank = 1;
    public const int CellRk = 2;
    public const int CellError = 3;
    public const int CellBool = 4;
    public const int CellReal = 5;
    public const int CellSt = 6;
    public const int CellIsst = 7;
    public const int FormulaString = 8;
    public const int FormulaNum = 9;
    public const int FormulaBool = 10;
    public const int FormulaError = 11;
    public const int SharedStringItem = 19;

    /// <summary>Whether a record is a cell that carries a value, blanks excluded.</summary>
    /// <remarks>
    /// A blank cell record is a cell with formatting and nothing in it, which is why a row
    /// made only of them reads as empty - and why it does not count towards how far a row
    /// reaches.
    /// </remarks>
    public static bool IsValueCell(int type)
        => type is CellRk or CellError or CellBool or CellReal or CellSt or CellIsst
                or FormulaString or FormulaNum or FormulaBool or FormulaError;

    /// <summary>Reads a whole part into memory, which is what the walk below needs.</summary>
    /// <remarks>
    /// A part is deflated, so it cannot be seeked; and the walk needs to skip bodies, which
    /// on a stream would mean reading them anyway. Held whole rather than streamed for that
    /// reason - the largest sheet part of the sample set is 26 MB.
    /// </remarks>
    public static byte[] Read(Stream part, long sizeHint)
    {
        using var buffer = new MemoryStream(sizeHint > 0 && sizeHint < int.MaxValue ? (int)sizeHint : 0);
        part.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Walks the records of a part, handing each one its type and its body.</summary>
    public static IEnumerable<(int Type, ArraySegment<byte> Body)> Walk(byte[] bytes)
    {
        int at = 0;
        int end = bytes.Length;

        while (at < end)
        {
            int type = bytes[at++];
            if ((type & 0x80) != 0)
            {
                if (at >= end) yield break;
                type = (type & 0x7F) | ((bytes[at++] & 0x7F) << 7);
            }

            int length = 0;
            bool complete = false;
            for (int k = 0; k < 4 && at < end; k++)
            {
                int piece = bytes[at++];
                length |= (piece & 0x7F) << (7 * k);
                if ((piece & 0x80) == 0) { complete = true; break; }
            }

            if (!complete || length < 0 || at + length > end)
                yield break;

            yield return (type, new ArraySegment<byte>(bytes, at, length));
            at += length;
        }
    }

    public static uint U32(ArraySegment<byte> body, int at)
        => BitConverter.ToUInt32(body.Array!, body.Offset + at);

    /// <summary>The column a cell record is at. Every cell record begins with one.</summary>
    public static int ColumnOf(ArraySegment<byte> body)
        => (int)(U32(body, 0) & 0x3FFF);

    /// <summary>A length-prefixed UTF-16 string, which is how this format spells one.</summary>
    public static string WideString(ArraySegment<byte> body, int at)
    {
        uint characters = U32(body, at);
        int bytes = checked((int)characters * 2);

        if (at + 4 + bytes > body.Count)
            return "";

        return Encoding.Unicode.GetString(body.Array!, body.Offset + at + 4, bytes);
    }

    /// <summary>
    /// The number an `RK` holds, which is a double squeezed into four bytes.
    /// </summary>
    /// <remarks>
    /// Two flags decide how: one says the remaining 30 bits are an integer rather than the
    /// top half of a double, and the other says the result was multiplied by a hundred so
    /// that two decimal places fit in the integer form.
    /// </remarks>
    public static double Rk(uint value)
    {
        bool hundredths = (value & 1) != 0;
        bool integer = (value & 2) != 0;

        double number;
        if (integer)
        {
            int signed = (int)(value >> 2);

            // The 30-bit field is two's complement, so its top bit is the sign.
            if ((signed & 0x20000000) != 0)
                signed -= 0x40000000;

            number = signed;
        }
        else
        {
            number = BitConverter.Int64BitsToDouble((long)(value & 0xFFFFFFFC) << 32);
        }

        return hundredths ? number / 100.0 : number;
    }
}
