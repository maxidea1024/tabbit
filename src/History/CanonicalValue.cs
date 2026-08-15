using System;
using System.Globalization;
using System.Text;

using ValueType = Tabbit.Models.ValueType;
using Field = Tabbit.Models.Field;

namespace Tabbit.History;

/// <summary>
/// One cell's value as the text the history stores, compares and shows.
///
/// Text rather than a typed value, for the same reason the conformance corpus compares
/// text: two snapshots agreeing about a value is the whole question, and a string both
/// sides derive by one rule is the least ambiguous way to say so. It is also what the
/// diff shows a designer, so it has to read as the value they typed.
///
/// Every rendering here is culture-invariant and round-trippable. A build on a machine
/// whose locale writes `0,1` must produce the same history as one that writes `0.1`, or
/// every float in the project appears to change the first time someone in another
/// office runs a conversion.
///
/// The type rules match <see cref="Exporters.BinaryExporter"/> exactly, including the
/// one that is not obvious: a reference cell holds the target row's primary index, an
/// int, whatever type the field resolved to.
/// </summary>
public static class CanonicalValue
{
    /// <summary>
    /// The canonical text of a cell, or null when the cell holds nothing.
    /// </summary>
    public static string? Of(object? value, Field field)
    {
        if (value is null)
            return null;

        // A delimited array is one cell holding several values. Serial arrays are not
        // this: those are separate columns, each arriving here as its own scalar.
        if (value is Array elements)
            return OfArray(elements, ElementTypeOf(field));

        return OfScalar(value!, ElementTypeOf(field));
    }

    /// <summary>
    /// The canonical text of a single value of a known type.
    /// </summary>
    public static string? OfScalar(object value, ValueType type)
    {
        if (value is null)
            return null;

        switch (type)
        {
            case ValueType.String:
                return (string)value;

            case ValueType.Bool:
                return (bool)value ? "true" : "false";

            case ValueType.Int32:
            case ValueType.Enum:

            // Stored as the target row's primary index, which is always an int32.
            case ValueType.ForeignRecord:
                return ((int)value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture);

            // "R" is shortest-round-trippable on .NET Core 3.0 and later, so the text
            // reconstructs the exact stored value - and a float is rendered at float
            // precision rather than showing the digits widening to a double invents.
            case ValueType.Float:
                return ((float)value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, as everywhere else in this codebase: exact, and with no formatting
            // for two snapshots to disagree about.
            case ValueType.DateTime:
                return ((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return ((Guid)value!).ToString("D").ToLowerInvariant();

            default:
                throw new TabbitException(
                    $"The history cannot render a value of type `{type}`.");
        }
    }

    /// <summary>
    /// A delimited array cell, as a JSON array.
    ///
    /// JSON rather than the sheet's own delimiter, because the delimiter is
    /// configurable and can appear inside a string element - so `a;b` as one element
    /// and `a`, `b` as two would store identically and a diff between them would show
    /// nothing. It also reads correctly in the report, which the sheet text would not
    /// once escaped.
    /// </summary>
    private static string OfArray(Array elements, ValueType elementType)
    {
        bool quoted = elementType == ValueType.String || elementType == ValueType.Uuid;

        var text = new StringBuilder("[");

        for (int i = 0; i < elements.Length; i++)
        {
            if (i > 0)
                text.Append(',');

            string? element = OfScalar(elements.GetValue(i)!, elementType);

            if (element is null)
                text.Append("null");
            else if (quoted)
                Quote(text, element);
            else
                text.Append(element);
        }

        return text.Append(']').ToString();
    }

    /// <summary>
    /// The type one value of this field has.
    ///
    /// A reference resolves to the type of whatever it points at, but the cell still
    /// holds an index - so the resolved type is not what is read here.
    /// </summary>
    private static ValueType ElementTypeOf(Field field)
        => field.IsRef ? ValueType.Int32 : field.ElementType;

    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;

                default:
                    if (c < 0x20)
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        text.Append(c);
                    break;
            }
        }

        text.Append('"');
    }
}
