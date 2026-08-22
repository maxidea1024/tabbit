using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Tabbit.Recipe;

/// <summary>
/// Resolves a dotted recipe path such as `Sources.Xlsx` into a reader for that list.
///
/// Used by the source registry, which lets a source declare its recipe section in an
/// attribute and reads it on the source's behalf. Doing the reading here rather than in
/// the source is what stops one from naming a section in its attribute and reading a
/// different one - which compiled fine and showed up only as an error message pointing
/// where the entry was not.
///
/// Targets used to be read this way too. They are not any more: a target's entries all
/// come from the recipe's `Targets` list, which needs no path because the entry names
/// its own target.
/// </summary>
internal static class RecipeSectionReader
{
    private const BindingFlags MemberFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Builds the reader, checking as it goes that every step of the path exists and
    /// that the end of it is a list of <paramref name="entryType"/>.
    ///
    /// Resolved once at startup, so a section that does not exist or holds the wrong
    /// element type stops the run immediately instead of surfacing later as a section
    /// name in a message that nobody can find in their recipe.
    /// </summary>
    /// <param name="section">Dotted path from <see cref="RecipeModel"/>.</param>
    /// <param name="entryType">The element type the section must hold.</param>
    /// <param name="ownerType">The declaring component, named in errors.</param>
    public static Func<RecipeModel, IEnumerable> Build(string section, Type entryType, Type ownerType)
    {
        var chain = new List<MemberInfo>();
        var current = typeof(RecipeModel);

        foreach (var part in section.Split('.'))
        {
            // Fields as well as properties, because the recipe model has held both and
            // Newtonsoft serializes either, so nothing forces the two into agreement.
            MemberInfo? member = current!.GetProperty(part, MemberFlags);
            Type? next = (member as PropertyInfo)?.PropertyType;

            if (member is null)
            {
                member = current.GetField(part, MemberFlags);
                next = (member as FieldInfo)?.FieldType;
            }

            if (member is null)
            {
                    throw new TabbitDefectException(
                        $"`{ownerType.Name}` declares recipe section `{section}`, " +
                        $"but `{current.Name}` has no `{part}`.");
            }

            chain.Add(member);
            current = next;
        }

        if (!typeof(IEnumerable<>).MakeGenericType(entryType).IsAssignableFrom(current))
        {
                throw new TabbitDefectException(
                    $"`{ownerType.Name}` declares recipe section `{section}`, but that is " +
                    $"`{current!.Name}` rather than a list of `{entryType.Name}`.");
        }

        return recipe =>
        {
            object? value = recipe;

            foreach (var member in chain)
            {
                value = member is PropertyInfo property
                    ? property.GetValue(value)
                    : ((FieldInfo)member).GetValue(value);

                // A recipe that omits a whole group leaves it null rather than empty.
                if (value is null)
                    return Array.Empty<object>();
            }

            return (IEnumerable)value;
        };
    }
}
