using System;
using System.Collections.Generic;
using Tabbit.History;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// How a cell's text becomes a value-pool id, and what happens when it cannot.
///
/// One expression, and worth a file of its own because of what it decides. NULL in the
/// change log means the cell held nothing. The version this replaced used NULL for that
/// *and* for a lookup that missed, so a value that went astray was reported as an
/// emptied cell - by every query afterwards, for ever, with nothing anywhere saying
/// otherwise.
///
/// That is the failure this project is built against: not a crash, a different answer.
/// </summary>
public class ValuePoolTests
{
    private static readonly Dictionary<string, long> Pool =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            { "1500", 7 },
            { "", 9 },
        };

    /// <summary>
    /// A cell that held nothing is NULL, which is what the column means.
    /// </summary>
    [Fact]
    public void A_cell_that_held_nothing_is_null()
    {
        Assert.Equal(DBNull.Value, HistoryStore.ValueId(Pool, null));
    }

    /// <summary>
    /// And a cell holding the empty string is not the same thing - it has an id, because
    /// the empty string is a value somebody typed.
    /// </summary>
    [Fact]
    public void A_cell_holding_an_empty_string_is_not_null()
    {
        Assert.Equal(9L, HistoryStore.ValueId(Pool, ""));
    }

    [Fact]
    public void A_cell_with_a_value_gets_its_id()
    {
        Assert.Equal(7L, HistoryStore.ValueId(Pool, "1500"));
    }

    /// <summary>
    /// A value the pool does not hold is refused rather than written as NULL.
    ///
    /// Sabotage this one - return DBNull.Value instead of throwing - and nothing else in
    /// the suite notices, which is exactly how it survived.
    /// </summary>
    [Fact]
    public void A_value_the_pool_lost_is_refused_rather_than_blanked()
    {
        var ex = Assert.Throws<TabbitException>(() => HistoryStore.ValueId(Pool, "2500"));

        Assert.Equal(Tabbit.History.RecordMessages.ValuePoolIdMissing, ex.MessageId);
        Assert.Contains("2500", ex.Message);
    }

    /// <summary>
    /// The message names enough of the value to recognise it and no more. A cell can hold
    /// a paragraph of a designer's prose, and a log line is not the place for it.
    /// </summary>
    [Fact]
    public void A_long_value_is_shortened_in_the_message()
    {
        string long_ = new string('x', 500);

        var ex = Assert.Throws<TabbitException>(() => HistoryStore.ValueId(Pool, long_));

        Assert.Equal(Tabbit.History.RecordMessages.ValuePoolIdMissing, ex.MessageId);
        Assert.Contains("...", ex.Message);
        Assert.True(ex.Message.Length < 400, $"The message is {ex.Message.Length} characters long.");
    }
}
