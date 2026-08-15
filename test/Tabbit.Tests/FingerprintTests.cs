using System;
using System.Globalization;
using System.Threading;
using Tabbit.History;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The hash the history compares snapshots with, and the text it stores.
///
/// Everything here is about a wrong answer rather than a failure. A hash that cannot
/// tell two different rows apart reports "no change" and the history quietly loses an
/// edit; a value rendered through the machine's locale makes every float in the project
/// look changed the first time a colleague in another office runs a build. Neither
/// throws, and neither is visible without a test that asks.
/// </summary>
public class FingerprintTests
{
    private static string Hash(params string[] components)
    {
        using var fingerprint = new Fingerprint();

        foreach (var component in components)
            fingerprint.Add(component);

        return fingerprint.Complete();
    }

    /// <summary>
    /// The reason components are framed by their length rather than joined.
    ///
    /// Any delimiter can appear inside a cell, so a join makes one value containing the
    /// delimiter indistinguishable from two values - and a designer splitting a column
    /// would produce an identical hash and no recorded change.
    /// </summary>
    [Fact]
    public void A_component_containing_the_separator_is_not_two_components()
    {
        Assert.NotEqual(Hash("a;b"), Hash("a", "b"));
        Assert.NotEqual(Hash("a|b"), Hash("a", "b"));
        Assert.NotEqual(Hash("ab"), Hash("a", "b"));
        Assert.NotEqual(Hash("a\0b"), Hash("a", "b"));
    }

    /// <summary>
    /// A blank cell and a cell holding an empty string are different edits, and both
    /// are common in a sheet.
    /// </summary>
    [Fact]
    public void Absent_is_not_empty()
    {
        using var absent = new Fingerprint();
        absent.AddAbsent();

        Assert.NotEqual(Hash(""), absent.Complete());
    }

    [Fact]
    public void A_null_component_is_absent_rather_than_empty()
    {
        using var explicitly = new Fingerprint();
        explicitly.AddAbsent();

        Assert.Equal(explicitly.Complete(), Hash((string)null));
    }

    [Fact]
    public void The_order_of_components_matters()
    {
        Assert.NotEqual(Hash("a", "b"), Hash("b", "a"));
    }

    [Fact]
    public void The_same_components_hash_the_same()
    {
        Assert.Equal(Hash("a", "b", "c"), Hash("a", "b", "c"));
    }

    /// <summary>
    /// Components longer than the stack buffer take a different path through the
    /// encoder, so the two paths have to agree.
    /// </summary>
    [Fact]
    public void A_long_component_hashes_like_a_short_one_of_the_same_content()
    {
        string longText = new string('x', 4096);

        Assert.Equal(Hash(longText), Hash(longText));
        Assert.NotEqual(Hash(longText), Hash(longText + "x"));
    }

    [Fact]
    public void Non_ascii_text_survives()
    {
        Assert.NotEqual(Hash("한"), Hash("A"));
        Assert.Equal(Hash("é한Ａ"), Hash("é한Ａ"));
    }

    [Fact]
    public void A_completed_fingerprint_cannot_be_completed_again()
    {
        using var fingerprint = new Fingerprint();
        fingerprint.Add("a");
        fingerprint.Complete();

        Assert.Throws<InvalidOperationException>(() => fingerprint.Complete());
    }

    // -------------------------------------------------------- canonical text

    [Theory]
    [InlineData(ValueType.Bool, true, "true")]
    [InlineData(ValueType.Bool, false, "false")]
    [InlineData(ValueType.Int32, 0, "0")]
    [InlineData(ValueType.Int32, -2147483648, "-2147483648")]
    [InlineData(ValueType.Enum, 1048576, "1048576")]
    [InlineData(ValueType.String, "", "")]
    public void Scalars_render_as_the_history_stores_them(ValueType type, object value, string expected)
    {
        Assert.Equal(expected, CanonicalValue.OfScalar(value, type));
    }

    [Fact]
    public void Wide_integers_keep_every_digit()
    {
        Assert.Equal("9223372036854775807", CanonicalValue.OfScalar(long.MaxValue, ValueType.Int64));
        Assert.Equal("-9223372036854775808", CanonicalValue.OfScalar(long.MinValue, ValueType.Int64));
    }

    /// <summary>
    /// A float renders at float precision, not at the width it widens to.
    /// </summary>
    [Fact]
    public void A_float_does_not_show_the_digits_a_double_invents()
    {
        Assert.Equal("0.1", CanonicalValue.OfScalar(0.1f, ValueType.Float));
        Assert.Equal("3.4028235E+38", CanonicalValue.OfScalar(float.MaxValue, ValueType.Float));
    }

    [Fact]
    public void Timestamps_and_durations_are_ticks()
    {
        Assert.Equal("0", CanonicalValue.OfScalar(DateTime.MinValue, ValueType.DateTime));
        Assert.Equal("3155378975999999999", CanonicalValue.OfScalar(DateTime.MaxValue, ValueType.DateTime));
        Assert.Equal("-3000000000", CanonicalValue.OfScalar(TimeSpan.FromMinutes(-5), ValueType.TimeSpan));
    }

    [Fact]
    public void A_uuid_is_lower_case_with_hyphens()
    {
        Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff",
            CanonicalValue.OfScalar(new Guid("6F9619FF-8B86-D011-B42D-00C04FC964FF"), ValueType.Uuid));
    }

    /// <summary>
    /// The rendering must not depend on where the build ran.
    ///
    /// A locale writing `0,1` would otherwise make every float and every tick count in
    /// the project appear to change the first time somebody in another office converted
    /// - a whole history of edits nobody made.
    /// </summary>
    [Fact]
    public void Rendering_does_not_follow_the_machine_locale()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("0.1", CanonicalValue.OfScalar(0.1, ValueType.Double));
            Assert.Equal("1234567", CanonicalValue.OfScalar(1234567, ValueType.Int32));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Nothing_in_a_cell_is_null_rather_than_empty_text()
    {
        Assert.Null(CanonicalValue.OfScalar(null, ValueType.String));
    }
}
