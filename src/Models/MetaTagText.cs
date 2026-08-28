using System;
using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// Reads the `tag=` bracket's text into the pairs a model holds.
/// </summary>
/// <remarks>
/// **The pairs mean nothing here.** What a project writes in them is that project's business:
/// this reads the shape and nothing else, so that a label written on a sheet arrives whole at
/// whatever decides to read it. spec/layout/tags.md section 6.
///
/// One place rather than one per notation, because a declaration's brackets and a sheet's
/// brackets write the same thing and two readers would agree until they did not.
/// </remarks>
public static class MetaTagText
{
    /// <summary>
    /// Adds `key=value` pairs, comma separated, to what is already there.
    /// </summary>
    /// <remarks>
    /// A word with no `=` is a key whose value is empty - a label that is only a name is a
    /// thing projects write, and refusing it would be this tool having an opinion about
    /// content it does not read. Split on the first `=` only, so a value may hold one.
    ///
    /// A key written twice takes the later value: the two writers are a type declaration and
    /// the column carrying it, and the column is the more specific of the two.
    ///
    /// **Case is not part of a tag.** The dictionaries handed here compare keys without it,
    /// because a tag is a word somebody typed into a cell and `WIP` and `wip` are the same
    /// word - the same rule the row tags are matched by.
    /// </remarks>
    public static void ReadInto(string written, IDictionary<string, string> into)
    {
        foreach (string part in written.Split(','))
        {
            string pair = part.Trim();

            if (pair.Length == 0)
                continue;

            int equals = pair.IndexOf('=');

            string key = (equals < 0 ? pair : pair.Substring(0, equals)).Trim();
            string value = equals < 0 ? "" : pair.Substring(equals + 1).Trim();

            if (key.Length != 0)
                into[key] = value;
        }
    }
}
