using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tabbit.Helpers;

/// <summary>
/// Reads a recipe's time zone setting: the name of a zone, or a fixed offset from UTC.
/// </summary>
/// <remarks>
/// Both forms are accepted because they answer different questions. A name carries the
/// region's history - when its clocks moved and by how much - which is what a sheet holding
/// past dates needs. A fixed offset carries none of that, which is right for sheets whose
/// authors work to one offset and never to a summer one, and it asks nothing of the machine
/// doing the conversion: a name has to be in the time zone database installed there, and a
/// container image is entitled not to have one.
///
/// An offset becomes a zone with no adjustment rules rather than a second kind of value, so
/// everything downstream holds one type and neither form gets its own conversion path.
/// </remarks>
public static class TimeZones
{
    /// <summary>
    /// How far from UTC an offset may be, which is the range a real zone spans.
    /// </summary>
    private static readonly TimeSpan OffsetLimit = TimeSpan.FromHours(14);

    /// <summary>How many zone names an unrecognized setting is answered with.</summary>
    private const int SuggestionLimit = 6;

    /// <summary>The recipe-wide setting.</summary>
    public static TimeZoneInfo? OfRecipe(string? text)
        => Resolve(text, "Recipe setting `TimeZone`");

    /// <summary>One source entry's setting, which wins over the recipe's for its sheets.</summary>
    /// <param name="section">Recipe path of the entry, for messages.</param>
    public static TimeZoneInfo? OfEntry(string? text, string section)
        => Resolve(text, $"Recipe `{section}` setting `TimeZone`");

    /// <summary>The forced setting from the command line, which wins over both.</summary>
    public static TimeZoneInfo? OfCommandLine(string? text)
        => Resolve(text, "Command line `--time-zone`");

    /// <summary>
    /// The zone a setting names, or null when it says nothing.
    /// </summary>
    /// <param name="text">What was written.</param>
    /// <param name="subject">
    /// Which setting this is, as a message says it. Named by the caller rather than worked
    /// out here, because the three places a zone is written are three different things to
    /// go and fix.
    /// </param>
    private static TimeZoneInfo? Resolve(string? text, string subject)
    {
        string value = (text ?? "").Trim();

        // Blank is "the recipe does not say", not an error: it is what a recipe written
        // before this setting existed holds, and what deleting the line leaves behind.
        if (value.Length == 0)
            return null;

        if (LooksLikeOffset(value))
            return FixedOffset(value, subject);

        try
        {
            // Both id families, because .NET reads either on either platform: a Windows
            // machine answers `Asia/Seoul` and a Linux one answers `Korea Standard Time`.
            // Which family a recipe uses is its own business.
            return TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new TabbitException(Unknown(value, subject));
        }
        catch (InvalidTimeZoneException problem)
        {
            // The name exists and its data does not load. Nothing a recipe can fix, so the
            // message says where the fault is rather than offering spellings.
            throw new TabbitException(
                $"{subject} is `{value}`, and this machine's time zone data for it cannot "
                + $"be read. ({problem.Message})");
        }
    }

    /// <summary>Whether a setting is meant as an offset rather than as a name.</summary>
    /// <remarks>
    /// A leading sign, or the one-letter spelling of UTC. Deciding by the first character
    /// rather than by whether the name lookup fails is what lets a malformed offset be
    /// answered as a malformed offset - `+9:5` is not a zone anybody misspelled.
    /// </remarks>
    private static bool LooksLikeOffset(string value)
        => value[0] == '+' || value[0] == '-'
           || value.Equals("Z", StringComparison.OrdinalIgnoreCase);

    /// <summary>A zone that is one offset from UTC and stays there.</summary>
    private static TimeZoneInfo FixedOffset(string value, string subject)
    {
        if (value.Equals("Z", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        if (!TryOffset(value, out TimeSpan offset))
        {
            throw new TabbitException(
                $"{subject} is `{value}`, which is not an offset from UTC. An offset is "
                + "written `+09:00`, `-05:30`, `+0900` or `+09`, and `Z` is UTC itself. A "
                + "region's name works too, as `Asia/Seoul`.");
        }

        if (offset > OffsetLimit || offset < -OffsetLimit)
        {
            throw new TabbitException(
                $"{subject} is `{value}`, which is further from UTC than any place on "
                + $"earth. Offsets run from `-14:00` to `+14:00`.");
        }

        if (offset == TimeSpan.Zero)
            return TimeZoneInfo.Utc;

        // Named by the offset it is. The id reaches nothing but a message, because a zone
        // with no adjustment rules has nothing else to say about itself.
        string id = (offset < TimeSpan.Zero ? "-" : "+")
                    + offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);

        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
    }

    /// <summary>
    /// Reads `+09:00`, `-05:30`, `+0900`, `+930` and `+09`.
    /// </summary>
    /// <remarks>
    /// Written out rather than handed to TimeSpan.ParseExact, which reads `-05:30` as a
    /// duration of five and a half hours and then loses the sign for a caller that has to
    /// ask separately. The sign is the half of an offset that matters most.
    /// </remarks>
    private static bool TryOffset(string value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        int sign = value[0] == '-' ? -1 : 1;
        string body = value.Substring(1);

        if (body.Length == 0 || !body.All(c => char.IsAsciiDigit(c) || c == ':'))
            return false;

        string hourText;
        string minuteText;

        int colon = body.IndexOf(':');
        if (colon >= 0)
        {
            if (body.IndexOf(':', colon + 1) >= 0)
                return false;

            hourText = body.Substring(0, colon);
            minuteText = body.Substring(colon + 1);

            if (hourText.Length is 0 or > 2 || minuteText.Length != 2)
                return false;
        }
        else if (body.Length <= 2)
        {
            hourText = body;
            minuteText = "0";
        }
        else if (body.Length <= 4)
        {
            // `+0930` and `+930`, which is how an offset arrives from anything that wrote it
            // for a machine rather than for a reader.
            hourText = body.Substring(0, body.Length - 2);
            minuteText = body.Substring(body.Length - 2);
        }
        else
        {
            return false;
        }

        if (!int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
        {
            return false;
        }

        if (minutes > 59)
            return false;

        offset = sign * (TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes));
        return true;
    }

    /// <summary>What is wrong with a name, and the names that are nearly it.</summary>
    /// <remarks>
    /// Suggestions come from the machine's own list rather than from a table here, because
    /// that list is the one the lookup consults. Matched on containment: the spelling that
    /// gets written is `Seoul` or `seoul`, and both reach `Asia/Seoul` this way while
    /// neither is an edit distance from it.
    /// </remarks>
    private static string Unknown(string value, string subject)
    {
        var suggestions = Nearest(value);

        string nearby = suggestions.Count == 0
            ? ""
            : " Did you mean " + string.Join(", ", suggestions.Select(id => $"`{id}`")) + "?";

        return $"{subject} is `{value}`, which is not a time zone this machine knows."
               + nearby
               + " A zone is named as `Asia/Seoul` or `Korea Standard Time`, and an offset is"
               + " written `+09:00` for sheets whose authors keep one offset all year.";
    }

    /// <summary>Zone ids that hold what was written, however it was capitalized.</summary>
    private static List<string> Nearest(string value)
    {
        string needle = value.Replace('_', ' ').Replace('/', ' ').Trim();

        if (needle.Length < 2)
            return new List<string>();

        try
        {
            return TimeZoneInfo.GetSystemTimeZones()
                .Select(zone => zone.Id)
                .Where(id => id.Replace('_', ' ').Replace('/', ' ')
                               .Contains(needle, StringComparison.OrdinalIgnoreCase))
                .Take(SuggestionLimit)
                .ToList();
        }
        catch (Exception)
        {
            // A machine with no time zone data at all. The message it is for still stands
            // on its own, and failing while composing an error would replace it.
            return new List<string>();
        }
    }
}
