using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tabbit.Sources;
using Tabbit.Targets;

namespace Tabbit.Recipe;

/// <summary>
/// Writes the starting recipe that `--new-recipe` produces.
///
/// It used to serialize a default <see cref="RecipeModel"/>, which meant every list came
/// out as `[]`. That names the sections but not the settings, so the reader learns that
/// `Exports.Binary` exists and nothing about what belongs in it - the note left on the
/// option said as much.
///
/// So each list gets one entry with its defaults filled in, and the file opens with the
/// registered source and target ids. Every field is then visible with the value it would
/// take, and an entry left as-is is inert: a blank path or connection string is how a
/// target is switched off.
///
/// The entries are produced by walking the model rather than from a template, so a
/// setting added to the model appears here without anyone remembering to add it.
/// </summary>
internal static class RecipeSkeleton
{
    /// <summary>Prefix of the embedded starting recipes.</summary>
    private const string TemplatePrefix = "Tabbit.Recipes.";

    /// <summary>
    /// The one the header shows in its example line.
    /// </summary>
    /// <remarks>
    /// Named rather than "whichever sorts first", which was `ci` - a template for a
    /// situation nobody is in on their first day.
    /// </remarks>
    private const string ExampleTemplate = "unity";

    /// <summary>
    /// The template names `--template` accepts, in the order they are offered.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than listed, so a template added to src/recipes is
    /// offered without anyone remembering to name it here.
    /// </remarks>
    public static IReadOnlyList<string> TemplateNames
        => typeof(RecipeSkeleton).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(TemplatePrefix, StringComparison.Ordinal))
            .Select(name => name.Substring(TemplatePrefix.Length).Replace(".jsonc", ""))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Writes one of the worked starting recipes.
    /// </summary>
    /// <remarks>
    /// The reflected skeleton below shows every setting a target takes, which answers "what
    /// can I write" and not "what should I write" - and starting from a page holding every
    /// option at its default is its own kind of blank page.
    ///
    /// So there are templates: a recipe for a situation, with the settings that situation
    /// needs, each carrying a comment saying what it is for and when to change it. Somebody
    /// shipping a Unity client should be able to change three paths and be done.
    /// </remarks>
    public static void WriteTemplateToFile(string filename, string template)
    {
        string resource = TemplatePrefix + template + ".jsonc";

        using var stream = typeof(RecipeSkeleton).Assembly.GetManifestResourceStream(resource);

        if (stream is null)
        {
            throw new TabbitException(
                $"There is no starting recipe called `{template}`. " +
                $"Use one of: {string.Join(", ", TemplateNames)} - or leave --template off " +
                "for one holding every setting at its default.");
        }

        using var reader = new StreamReader(stream);

        File.WriteAllText(filename, reader.ReadToEnd());
    }

    public static void WriteToFile(string filename)
    {
        var recipe = new RecipeModel();

        FillLists(recipe);

        string json = JsonConvert.SerializeObject(recipe, Formatting.Indented);

        File.WriteAllText(filename, Header() + json + Environment.NewLine);
    }

    private static string Header()
    {
        var header = new StringBuilder();

        header.AppendLine("// Tabbit recipe, created by --new-recipe.");
        header.AppendLine("//");
        header.AppendLine("// `//` comments are allowed anywhere in this file.");
        header.AppendLine("//");
        header.AppendLine("// Each list below holds one entry with its default settings, so that every option");
        header.AppendLine("// is visible. Fill in the ones you want and delete the rest - though an entry with");
        header.AppendLine("// a blank Path or ConnectionString is treated as switched off, so leaving one in");
        header.AppendLine("// place costs nothing.");
        header.AppendLine("//");
        header.AppendLine("// Output can also be listed by target name, which is the only form available to");
        header.AppendLine("// targets that have no section of their own:");
        header.AppendLine("//");
        header.AppendLine("//   \"Targets\": [ { \"Type\": \"csharp\", \"Path\": \"./out/cs\" } ]");
        header.AppendLine("//");
        header.AppendLine($"// Sources: {SourceRegistry.KnownIds}");
        header.AppendLine($"// Targets: {TargetRegistry.KnownIds}");
        header.AppendLine("//");
        header.AppendLine("// This file shows every setting at its default, which answers what a target takes");
        header.AppendLine("// and not what to write for a given situation. For that there are worked starting");
        header.AppendLine("// recipes, each commented with what its settings are for:");
        header.AppendLine("//");
        header.AppendLine($"//   tabbit --new-recipe my-recipe.json --template {ExampleTemplate}");
        header.AppendLine("//");
        header.AppendLine($"// Templates: {string.Join(", ", TemplateNames)}");
        header.AppendLine();

        return header.ToString();
    }

    /// <summary>
    /// Gives every empty entry list one default-constructed element, recursing through
    /// the recipe's groups.
    /// </summary>
    private static void FillLists(object owner)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        var members = new List<MemberInfo>();
        members.AddRange(owner.GetType().GetProperties(flags));
        members.AddRange(owner.GetType().GetFields(flags));

        foreach (var member in members)
        {
            var type = TypeOf(member);
            object? value = ValueOf(member, owner);

            if (value is null)
                continue;

            if (IsEntryList(type))
            {
                var elementType = type.GetGenericArguments()[0];

                // `Targets` holds raw JSON, because its element type is not known until
                // the entry's `Type` is read. An empty object here would be a `Targets`
                // entry naming no target, which the registry rejects - so the header
                // shows the shape instead.
                if (elementType == typeof(JObject))
                    continue;

                // A list of plain values - `IncludeSheets` and its like - is one setting
                // rather than a list of entries, and empty is what it means. Activator
                // also has nothing to construct for a string, so filling it would throw.
                if (elementType.IsPrimitive || elementType == typeof(string))
                    continue;

                var list = (IList)value;
                if (list.Count == 0)
                    list.Add(Activator.CreateInstance(elementType));

                continue;
            }

            if (IsRecipeGroup(type))
                FillLists(value);
        }
    }

    private static bool IsEntryList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    /// <summary>
    /// One of the recipe's own grouping objects - `Sources`, `Exports`, `CodeGenerations`
    /// - as opposed to a setting.
    /// </summary>
    private static bool IsRecipeGroup(Type type)
        => type.IsClass
           && type != typeof(string)
           && !type.IsGenericType
           && type.Assembly == typeof(RecipeModel).Assembly;

    private static Type TypeOf(MemberInfo member)
        => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

    private static object? ValueOf(MemberInfo member, object owner)
        => member is PropertyInfo property ? property.GetValue(owner) : ((FieldInfo)member).GetValue(owner);
}
