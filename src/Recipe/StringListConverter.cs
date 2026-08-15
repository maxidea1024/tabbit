using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tabbit.Recipe;

/// <summary>
/// Reads a list of strings written either as an array or as one semicolon-separated string.
/// </summary>
/// <remarks>
/// Both forms because both are the natural one somewhere. `"ExcludeSheets": "*Notes*"` is a
/// setting with a value; a list of sixty-eight sheet names is a list, and wants a line each
/// so that it can be commented in groups and read in a diff.
///
/// Writing always produces the array, which is what `--new-recipe` shows.
/// </remarks>
public sealed class StringListConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(List<string>);

    public override object? ReadJson(
        JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var result = new List<string>();

        var token = JToken.Load(reader);

        switch (token.Type)
        {
            case JTokenType.Null:
                return result;

            case JTokenType.String:
                AddSplit(result, token.Value<string>()!);
                return result;

            case JTokenType.Array:
                foreach (var element in (JArray)token)
                {
                    if (element.Type == JTokenType.Null)
                        continue;

                    // Split here too, so an array whose entries each hold several
                    // semicolon-separated names reads the way the string form does.
                    AddSplit(result, element.Value<string>()!);
                }
                return result;
        }

        throw new TabbitException(
            $"Expected a string or an array of strings, and found {token.Type}. " +
            $"(at {token.Path})");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();

        foreach (var item in (List<string>?)value ?? new List<string>())
            writer.WriteValue(item);

        writer.WriteEndArray();
    }

    private static void AddSplit(List<string> target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var part in text.Split(';'))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
                target.Add(trimmed);
        }
    }
}
