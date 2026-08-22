using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tabbit.Messages;

/// <summary>
/// A report named by its id, with the values that go in it, before any language is chosen.
/// </summary>
/// <remarks>
/// A type of its own rather than another string parameter, for two reasons. It cannot be
/// confused with the message text at a call site that has not been moved yet - the overloads
/// taking a string and the overloads taking one of these do not compete - and it makes the
/// migration visible: every call still passing a string is one still to do.
///
/// The values are named, not positional. A positional list would fix the order of the
/// sentence in every language, and Korean needs a different one often enough that the first
/// translation would have had to give up on it. Serilog's templates are already named, so
/// this is the notation the repository already reads.
/// </remarks>
public readonly struct Message
{
    private static readonly (string Name, object? Value)[] Nothing = [];

    private Message(string id, (string Name, object? Value)[] values)
    {
        Id = id;
        Values = values;
    }

    /// <summary>The id, prefix and all - `cook.unrecognized-type`.</summary>
    public string Id { get; }

    /// <summary>What fills the placeholders the catalog entry names.</summary>
    public IReadOnlyList<(string Name, object? Value)> Values { get; }

    /// <summary>Whether this is a real message rather than a default-constructed one.</summary>
    public bool IsSet => Id is not null;

    /// <summary>Names a report and the values that go in it.</summary>
    public static Message Of(string id, params (string Name, object? Value)[] values)
        => new Message(id, values ?? Nothing);

    /// <summary>
    /// The text of this message in one catalog, with the values put in.
    /// </summary>
    public string In(MessageCatalog catalog)
        => Fill(catalog.TextOf(Id), Values);

    /// <summary>
    /// Puts named values into a catalog entry's placeholders.
    /// </summary>
    /// <remarks>
    /// A placeholder nobody supplied a value for is left as it stands, so the report still
    /// reaches whoever needed it. That is a mismatch between a catalog entry and its call
    /// site, which is ours to fix, and `{Type}` sitting in the output says so in a way
    /// nobody can read past - the alternative, dropping it, would leave a sentence that
    /// reads fine and names the wrong thing.
    ///
    /// Values are formatted invariantly. The language of a sentence and the notation of a
    /// number are separate questions, and a run whose numbers follow the machine's locale
    /// produces output that differs between machines. spec/message-ids.md §10.
    ///
    /// `{{` and `}}` write a literal brace, which several messages need: the `text` target's
    /// settings are patterns full of `{group}` and `{namespace}`, and its own reports quote
    /// them. Without an escape those quotes would be read as placeholders and eaten - a
    /// message about a brace losing its braces. The notation is C#'s and Serilog's, so the
    /// escape reads the same in a catalog entry as in the code around it.
    /// </remarks>
    public static string Fill(string text, IReadOnlyList<(string Name, object? Value)> values)
    {
        if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0)
            return text;

        var built = new System.Text.StringBuilder(text.Length + 16);
        int at = 0;

        while (at < text.Length)
        {
            char here = text[at];

            // A doubled brace is one brace, and it is consumed rather than examined - so
            // `{{group}}` writes `{group}` and no name is looked up for it.
            if ((here == '{' || here == '}') && at + 1 < text.Length && text[at + 1] == here)
            {
                built.Append(here);
                at += 2;
                continue;
            }

            if (here != '{')
            {
                built.Append(here);
                at++;
                continue;
            }

            int close = text.IndexOf('}', at + 1);
            if (close < 0)
            {
                built.Append(text, at, text.Length - at);
                break;
            }

            string name = text.Substring(at + 1, close - at - 1);
            if (TryValue(values, name, out object? value))
                built.Append(Written(value));
            else
                built.Append(text, at, close - at + 1);

            at = close + 1;
        }

        return built.ToString();
    }

    private static bool TryValue(
        IReadOnlyList<(string Name, object? Value)> values, string name, out object? value)
    {
        for (int at = 0; at < values.Count; at++)
        {
            if (string.Equals(values[at].Name, name, StringComparison.Ordinal))
            {
                value = values[at].Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <remarks>
    /// A value may itself be a <see cref="Message"/>, and that is what keeps the composed
    /// reports from multiplying. The naming checks say things like "One field name is written
    /// 3 ways: … These normalize to the same name … rewrite the other 2 places." - four
    /// subjects, two consequences and a singular, which as whole sentences would be sixteen
    /// entries saying nearly the same thing. As one sentence with three nested phrases it is
    /// one entry and eight short ones, and every one of them is something a translator can
    /// read whole rather than a fragment they have to guess the shape of.
    ///
    /// Rendered against <see cref="MessageCatalog.Current"/>, the same catalog the sentence
    /// around it came from - a phrase in one language inside a sentence in another is the one
    /// outcome worth ruling out.
    /// </remarks>
    private static string Written(object? value)
        => value switch
        {
            null => "",
            string text => text,
            Message nested => nested.In(MessageCatalog.Current),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
}
