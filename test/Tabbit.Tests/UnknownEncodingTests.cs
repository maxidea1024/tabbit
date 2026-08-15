using Tabbit.Binary;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A reader meeting an encoding it cannot decode stops, and names the column.
/// </summary>
/// <remarks>
/// This is the gate that makes the format's promise about schema changes hold in the
/// direction nobody plans for. A column a build does not know is skipped by its declared
/// byte length and costs nothing; a column it *does* know, laid out a way it has never heard
/// of, is the case where reading on would produce values rather than an error. So the check
/// is on the pair - this element, that encoding - and anything outside the table is refused
/// by name rather than attempted.
///
/// The gate the format specified from the beginning and that nothing asserted until now: the
/// refusal existed in all thirteen runtimes and no test had ever seen one fire.
/// </remarks>
public class UnknownEncodingTests
{
    /// <summary>An encoding number past the ones the format defines is refused.</summary>
    /// <remarks>
    /// Not by an explicit "greater than thirteen" test in the reader, but because the pair
    /// table has no entry for it. That is deliberate: a number added to the format in a later
    /// version fails here for the same reason a nonsense one does, and neither reads.
    /// </remarks>
    [Fact]
    public void An_encoding_number_the_format_does_not_define_is_refused()
    {
        var column = Column(TcbTable.ElementI32, TcbTable.KindScalar, encoding: 14);

        var failure = Assert.Throws<TcbException>(
            () => TcbTable.CheckColumn(column, "Table.Field", TcbTable.KindScalar, 1, false,
                TcbTable.ElementI32));

        Assert.Contains("Table.Field", failure.Message);
        Assert.Contains("14", failure.Message);
    }

    /// <summary>
    /// An encoding the format defines, on an element it does not apply to, is refused too.
    /// </summary>
    /// <remarks>
    /// The more dangerous of the two, because every byte of it is a number this reader knows
    /// how to read. A front-coded dictionary over an eight-byte element would decode into
    /// something, and what it decoded into would be wrong rather than absent.
    /// </remarks>
    [Theory]
    [InlineData(TcbTable.ElementI64, TcbTable.EncodingDictFront)]
    [InlineData(TcbTable.ElementI64, TcbTable.EncodingDictSegment)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingDelta)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingWhole)]
    [InlineData(TcbTable.ElementUuid, TcbTable.EncodingDict)]
    [InlineData(TcbTable.ElementI32, TcbTable.EncodingWhole)]
    [InlineData(TcbTable.ElementBool, TcbTable.EncodingDelta)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementUuid, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementF64, TcbTable.EncodingBitpack)]
    public void An_encoding_that_does_not_apply_to_the_element_is_refused(byte element, byte encoding)
    {
        var column = Column(element, TcbTable.KindScalar, encoding);

        var failure = Assert.Throws<TcbException>(
            () => TcbTable.CheckColumn(column, "Table.Field", TcbTable.KindScalar, 1, false, element));

        Assert.Contains("Table.Field", failure.Message);
    }

    /// <summary>The pairs the format does define are accepted.</summary>
    /// <remarks>
    /// The other half of the gate. A check that refused everything would pass the tests above
    /// and make the format unreadable, so what it lets through is asserted as well - the
    /// v104 and v105 additions included, since those are the ones a reader could plausibly
    /// still be refusing. The bit-width layout reaches all four integer elements, which is
    /// the half of it that is easy to get wrong by opening it for `bool` alone.
    /// </remarks>
    [Theory]
    [InlineData(TcbTable.ElementBool, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementVarint, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementI32, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementI64, TcbTable.EncodingBitpack)]
    [InlineData(TcbTable.ElementF64, TcbTable.EncodingWhole)]
    [InlineData(TcbTable.ElementF32, TcbTable.EncodingWhole)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingDictSegment)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingDictSegmentRle)]
    [InlineData(TcbTable.ElementString, TcbTable.EncodingDictFrontRle)]
    [InlineData(TcbTable.ElementI32, TcbTable.EncodingDeltaRle)]
    public void The_pairs_the_format_defines_are_accepted(byte element, byte encoding)
    {
        var column = Column(element, TcbTable.KindScalar, encoding);

        TcbTable.CheckColumn(column, "Table.Field", TcbTable.KindScalar, 1, false, element);
    }

    /// <summary>
    /// An array column takes the composed encoding, and nothing else beyond raw.
    /// </summary>
    /// <remarks>
    /// The descriptor carries only the outer encoding, so this is as far as a descriptor
    /// check can go - what the elements use is stated inside the block and checked as it is
    /// read. A dictionary written directly onto an array column, with no composition around
    /// it, is refused here.
    /// </remarks>
    [Theory]
    [InlineData(TcbTable.EncodingRaw, true)]
    [InlineData(TcbTable.EncodingArray, true)]
    [InlineData(TcbTable.EncodingDict, false)]
    [InlineData(TcbTable.EncodingDictFront, false)]
    [InlineData(TcbTable.EncodingRle, false)]
    public void An_array_column_takes_raw_or_the_composed_encoding(byte encoding, bool accepted)
    {
        var column = Column(TcbTable.ElementString, TcbTable.KindVarArray, encoding);

        if (accepted)
        {
            TcbTable.CheckColumn(column, "Table.Field", TcbTable.KindVarArray, 0, false,
                TcbTable.ElementString);

            return;
        }

        Assert.Throws<TcbException>(
            () => TcbTable.CheckColumn(column, "Table.Field", TcbTable.KindVarArray, 0, false,
                TcbTable.ElementString));
    }

    private static TcbColumn Column(byte element, byte kind, byte encoding) => new TcbColumn
    {
        Tag = 1,
        Element = element,
        Kind = kind,
        Encoding = encoding,
        Nullable = false,
        Count = kind == TcbTable.KindVarArray ? 0 : 1,
        ByteLength = 0,
    };
}
