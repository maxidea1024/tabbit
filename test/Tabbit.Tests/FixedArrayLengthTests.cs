using Tabbit.Exporters;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What the shipped reader does with a fixed array's length now that the generated code no
/// longer states one.
/// </summary>
/// <remarks>
/// The generated side is pinned by <see cref="ArrayTypeTests"/>, which reads the emitted page
/// and finds `-1` in the check and `column.Count` in the loop. This asks the other half of the
/// question - what the runtime does when it is handed that -1 - of a file built here, because
/// a sheet cannot produce the case that matters: a column whose length is not the one the
/// reader was generated from.
///
/// One column owning the whole array is what makes the length data. A record group's is still
/// the generated shape, and the third test is the one that says so.
///
/// spec/nullable-array-elements.md.
/// </remarks>
public class FixedArrayLengthTests
{
    /// <summary>
    /// A file with one fixed array column: `count` elements per row, raw i32, no bitmap.
    /// </summary>
    /// <remarks>
    /// Hand-built rather than exported, because the point is a count the exporter would never
    /// write beside code generated from a different sheet. The block holds
    /// `rows × elements` values so that a reader taking the file at its word finds bytes for
    /// what it read; `count` and `elements` are separate parameters so a test can also write a
    /// count the block cannot honour.
    /// </remarks>
    private static byte[] FileWithFixedArray(int rows, int count, int elements)
    {
        var block = new TcbWriter();

        for (int row = 0; row < rows; row++)
            for (int at = 0; at < elements; at++)
                block.Write(row * 10 + at);

        var writer = new TcbWriter();

        TcbFormat.WriteHeader(writer);
        writer.WriteCounter32(rows);
        writer.WriteCounter32(1);

        writer.WriteCounter32(1);
        writer.Write(TcbFormat.Wire(TcbFormat.ElementI32, TcbFormat.KindFixedArray));
        writer.Write(TcbFormat.EncodingRaw);
        writer.WriteCounter32(count);
        writer.Write((uint)block.WrittenSpan.Length);
        writer.Write(block.WrittenSpan);

        return writer.WrittenSpan.ToArray();
    }

    private static Tabbit.Binary.TcbColumn OnlyColumn(byte[] file, out int rowCount)
    {
        var reader = new Tabbit.Binary.TcbReader(Tabbit.Binary.TcbTable.Open(file, null));

        return Tabbit.Binary.TcbTable.ReadHeader(reader, out rowCount)[0];
    }

    /// <summary>
    /// The length is the file's. A member that claims none is handed a column three long
    /// where the sheet it was generated from had two, and reads three.
    /// </summary>
    [Fact]
    public void A_member_that_claims_no_length_takes_the_file_s()
    {
        var column = OnlyColumn(FileWithFixedArray(rows: 2, count: 3, elements: 3), out int rows);

        Assert.Equal(2, rows);
        Assert.Equal(3, column.Count);

        // -1 is what the generated code emits for an array one column owns. The kind is still
        // the member's claim, which is why it is checked in the same call.
        Tabbit.Binary.TcbTable.CheckColumn(
            column, "Grown.Tag_array", Tabbit.Binary.TcbTable.KindFixedArray, -1, false,
            Tabbit.Binary.TcbTable.ElementI32);
    }

    /// <summary>
    /// The kind is not part of that: a scalar column is a different shape rather than a
    /// shorter array, so it is refused by name even though the member states no length.
    /// </summary>
    [Fact]
    public void Claiming_no_length_is_not_claiming_no_kind()
    {
        var column = OnlyColumn(FileWithFixedArray(rows: 1, count: 2, elements: 2), out _);

        var refused = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.CheckColumn(
                column, "Grown.Tag_array", Tabbit.Binary.TcbTable.KindScalar, -1, false,
                Tabbit.Binary.TcbTable.ElementI32));

        Assert.Contains("Grown.Tag_array", refused.Message);
    }

    /// <summary>
    /// A member that does state a length still holds the column to it. That is the record
    /// group's case - several columns fill one array, so the number they agree on is part of
    /// the generated shape and a file that disagrees is a schema change rather than data.
    /// </summary>
    [Fact]
    public void A_member_that_states_a_length_still_refuses_a_different_one()
    {
        var column = OnlyColumn(FileWithFixedArray(rows: 1, count: 3, elements: 3), out _);

        var refused = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.CheckColumn(
                column, "Loadout.Slot.Id", Tabbit.Binary.TcbTable.KindFixedArray, 2, false,
                Tabbit.Binary.TcbTable.ElementI32));

        Assert.Contains("Loadout.Slot.Id", refused.Message);
    }

    /// <summary>
    /// And the count is checked against the block before anything allocates for it: the read
    /// sizes its array from this number now, so a damaged file could otherwise ask for two
    /// billion elements per row and be given room for them.
    /// </summary>
    [Fact]
    public void A_count_the_block_cannot_hold_is_refused_with_the_header()
    {
        // One row, one element written, and a count claiming a thousand. A raw element costs
        // at least one byte, so four bytes cannot hold them.
        byte[] file = FileWithFixedArray(rows: 1, count: 1000, elements: 1);

        var refused = Assert.Throws<Tabbit.Binary.TcbException>(() => OnlyColumn(file, out _));

        Assert.Contains("1000 elements", refused.Message);
        Assert.Contains("4 bytes", refused.Message);
    }

    /// <summary>
    /// An empty table is not that case. It writes its columns' counts into blocks of no
    /// bytes, which is well-formed - and the check has to let it through or every table
    /// nobody has filled in yet stops loading.
    /// </summary>
    [Fact]
    public void An_empty_table_keeps_its_count()
    {
        var column = OnlyColumn(FileWithFixedArray(rows: 0, count: 3, elements: 3), out int rows);

        Assert.Equal(0, rows);
        Assert.Equal(3, column.Count);
    }
}
