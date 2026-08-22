using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tabbit.Cooking;

/// <summary>
/// The colour names a cell may write, and the palettes they come from.
/// </summary>
/// <remarks>
/// One palette is built in - `css`, the named colours of CSS Color Module Level 4 - and it is
/// the only one a bare name is looked up in. Every other palette is named in front of the
/// colour (`material.blue.500`), so **two palettes can never disagree about a name**: adding
/// one cannot change what `red` means in a sheet that already converts.
///
/// Entries are kept as 8-bit sRGB because that is what a palette's source is - a hex code -
/// and it is the representation `color32` takes without arithmetic. A `color` column divides
/// by 255. Keeping them as floats instead would round `color32` a second time.
///
/// spec/composite-value-types.md section 4.4.
/// </remarks>
public sealed class ColorPalettes
{
    /// <summary>The palette a bare colour name is looked up in.</summary>
    public const string DefaultPaletteName = "css";

    private readonly Dictionary<string, IReadOnlyDictionary<string, uint>> _palettes;

    private ColorPalettes(Dictionary<string, IReadOnlyDictionary<string, uint>> palettes)
        => _palettes = palettes;

    /// <summary>The built-in palette alone, for the runs that declared none of their own.</summary>
    /// <remarks>
    /// Built on first use rather than in a field initializer, because the table it wraps is a
    /// static field further down this file and those run in declaration order - an initializer
    /// here would capture it before it was parsed.
    /// </remarks>
    public static ColorPalettes BuiltInOnly => _builtInOnly ??= new ColorPalettes(
        new Dictionary<string, IReadOnlyDictionary<string, uint>>(System.StringComparer.OrdinalIgnoreCase)
        {
            [DefaultPaletteName] = CssColors,
        });

    private static ColorPalettes? _builtInOnly;

    /// <summary>
    /// The built-in palette plus the ones a recipe named, each already read from its file.
    /// </summary>
    /// <remarks>
    /// A recipe may not replace `css`: a sheet that says `red` is saying what the web says,
    /// and letting an entry redefine it would make the same cell mean different colours in
    /// two builds of the same workbook.
    /// </remarks>
    public static ColorPalettes With(IReadOnlyDictionary<string, IReadOnlyDictionary<string, uint>> extra)
    {
        var palettes = new Dictionary<string, IReadOnlyDictionary<string, uint>>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            [DefaultPaletteName] = CssColors,
        };

        foreach (var (name, entries) in extra)
        {
            if (string.Equals(name, DefaultPaletteName, System.StringComparison.OrdinalIgnoreCase))
            {
                throw new TabbitException(
                    $"A recipe declares a palette called `{DefaultPaletteName}`, which is the "
                    + "built-in one. A bare colour name is looked up there, so replacing it "
                    + "would change what an existing sheet's `red` means. Give it another name.");
            }

            palettes[name] = entries;
        }

        return new ColorPalettes(palettes);
    }

    /// <summary>The palette names this run knows, for a report that has to list them.</summary>
    public IEnumerable<string> Names => _palettes.Keys.OrderBy(name => name);

    /// <summary>
    /// Looks a written colour name up, qualified or bare.
    /// </summary>
    /// <param name="rgba">
    /// The colour as four 8-bit components, when the name was found.
    /// </param>
    /// <param name="problem">
    /// Why the name did not resolve, phrased as a whole sentence, or null when it did.
    /// </param>
    /// <remarks>
    /// A missing palette and a missing colour are separate reports. They are different
    /// mistakes - one is a recipe that does not declare the file, the other is a typo in a
    /// cell - and a single "unknown colour" would send the author to the wrong place.
    /// </remarks>
    public bool TryLookup(string written, out int[] rgba, out string? problem)
    {
        rgba = System.Array.Empty<int>();
        problem = null;

        string paletteName = DefaultPaletteName;
        string colorName = written;

        int dot = written.IndexOf('.');
        if (dot > 0)
        {
            paletteName = written.Substring(0, dot);
            colorName = written.Substring(dot + 1);

            if (!_palettes.ContainsKey(paletteName))
            {
                problem = $"No palette called `{paletteName}` is declared. "
                        + $"This run knows {string.Join(", ", Names.Select(name => $"`{name}`"))}; "
                        + "a recipe adds one with a `Palettes` entry.";
                return false;
            }
        }

        var palette = _palettes[paletteName];

        if (!palette.TryGetValue(colorName, out uint packed))
        {
            problem = dot > 0
                ? $"Palette `{paletteName}` has no colour called `{colorName}`."
                : $"`{written}` is not a CSS colour name. Write the colour as `#RRGGBB`, as a "
                  + "tuple, or name the palette it comes from (`material.blue.500`).";

            return false;
        }

        rgba = new[]
        {
            (int)((packed >> 24) & 0xFF),
            (int)((packed >> 16) & 0xFF),
            (int)((packed >> 8) & 0xFF),
            (int)(packed & 0xFF),
        };

        return true;
    }

    /// <summary>
    /// Reads a palette file: a JSON object of colour name to `#RRGGBB` or `#RRGGBBAA`.
    /// </summary>
    /// <remarks>
    /// JSON because a recipe is JSON and an author adding a palette is already in that
    /// format. The values are hex rather than tuples for the reason the whole table is 8-bit:
    /// a palette's source is a hex code.
    /// </remarks>
    public static IReadOnlyDictionary<string, uint> ReadFile(string paletteName, string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new TabbitException(
                $"Palette `{paletteName}` names the file `{path}`, which does not exist.");
        }

        Newtonsoft.Json.Linq.JObject document;

        try
        {
            document = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new TabbitException(
                $"Palette `{paletteName}` (`{path}`) is not readable as JSON. ({ex.Message})");
        }

        var result = new Dictionary<string, uint>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var property in document.Properties())
        {
            string? written = property.Value.Type == Newtonsoft.Json.Linq.JTokenType.String
                ? property.Value.ToString()
                : null;

            if (written is null || !TryReadHex(written, out uint packed))
            {
                throw new TabbitException(
                    $"Palette `{paletteName}` (`{path}`) gives `{property.Name}` the value "
                    + $"`{property.Value}`, which is not a `#RRGGBB` or `#RRGGBBAA` colour.");
            }

            result[property.Name] = packed;
        }

        return result;
    }

    /// <summary>
    /// Reads `#RGB`, `#RGBA`, `#RRGGBB` or `#RRGGBBAA` into packed 8-bit RGBA.
    /// </summary>
    /// <remarks>
    /// `0x` is accepted alongside `#` for the six and eight digit forms, because sheets that
    /// carried colours in an integer column before this type existed wrote them that way.
    /// The short forms are `#` only: `0x39C` is a number in every other column of the sheet.
    /// </remarks>
    public static bool TryReadHex(string written, out uint packed)
    {
        packed = 0;

        string digits;

        if (written.StartsWith("#", System.StringComparison.Ordinal))
        {
            digits = written.Substring(1);
        }
        else if (written.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        {
            digits = written.Substring(2);

            if (digits.Length is not (6 or 8))
                return false;
        }
        else
        {
            return false;
        }

        if (digits.Length is not (3 or 4 or 6 or 8))
            return false;

        if (!digits.All(System.Uri.IsHexDigit))
            return false;

        // The short form repeats each digit rather than padding it, so `#F00` is `#FF0000`
        // and the two ends of the range stay where they are: `F` is 255, not 240.
        if (digits.Length is 3 or 4)
            digits = string.Concat(digits.Select(digit => new string(digit, 2)));

        if (digits.Length == 6)
            digits += "FF";

        packed = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// The named colours of CSS Color Module Level 4, plus the `transparent` keyword.
    /// </summary>
    /// <remarks>
    /// Written as one string rather than a dictionary literal so the list can be read against
    /// the specification's own table in one pass. Parsed once, at first use.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, uint> CssColors = ParseTable(@"
        transparent 00000000
        aliceblue F0F8FF antiquewhite FAEBD7 aqua 00FFFF aquamarine 7FFFD4
        azure F0FFFF beige F5F5DC bisque FFE4C4 black 000000
        blanchedalmond FFEBCD blue 0000FF blueviolet 8A2BE2 brown A52A2A
        burlywood DEB887 cadetblue 5F9EA0 chartreuse 7FFF00 chocolate D2691E
        coral FF7F50 cornflowerblue 6495ED cornsilk FFF8DC crimson DC143C
        cyan 00FFFF darkblue 00008B darkcyan 008B8B darkgoldenrod B8860B
        darkgray A9A9A9 darkgreen 006400 darkgrey A9A9A9 darkkhaki BDB76B
        darkmagenta 8B008B darkolivegreen 556B2F darkorange FF8C00 darkorchid 9932CC
        darkred 8B0000 darksalmon E9967A darkseagreen 8FBC8F darkslateblue 483D8B
        darkslategray 2F4F4F darkslategrey 2F4F4F darkturquoise 00CED1 darkviolet 9400D3
        deeppink FF1493 deepskyblue 00BFFF dimgray 696969 dimgrey 696969
        dodgerblue 1E90FF firebrick B22222 floralwhite FFFAF0 forestgreen 228B22
        fuchsia FF00FF gainsboro DCDCDC ghostwhite F8F8FF gold FFD700
        goldenrod DAA520 gray 808080 green 008000 greenyellow ADFF2F
        grey 808080 honeydew F0FFF0 hotpink FF69B4 indianred CD5C5C
        indigo 4B0082 ivory FFFFF0 khaki F0E68C lavender E6E6FA
        lavenderblush FFF0F5 lawngreen 7CFC00 lemonchiffon FFFACD lightblue ADD8E6
        lightcoral F08080 lightcyan E0FFFF lightgoldenrodyellow FAFAD2 lightgray D3D3D3
        lightgreen 90EE90 lightgrey D3D3D3 lightpink FFB6C1 lightsalmon FFA07A
        lightseagreen 20B2AA lightskyblue 87CEFA lightslategray 778899 lightslategrey 778899
        lightsteelblue B0C4DE lightyellow FFFFE0 lime 00FF00 limegreen 32CD32
        linen FAF0E6 magenta FF00FF maroon 800000 mediumaquamarine 66CDAA
        mediumblue 0000CD mediumorchid BA55D3 mediumpurple 9370DB mediumseagreen 3CB371
        mediumslateblue 7B68EE mediumspringgreen 00FA9A mediumturquoise 48D1CC mediumvioletred C71585
        midnightblue 191970 mintcream F5FFFA mistyrose FFE4E1 moccasin FFE4B5
        navajowhite FFDEAD navy 000080 oldlace FDF5E6 olive 808000
        olivedrab 6B8E23 orange FFA500 orangered FF4500 orchid DA70D6
        palegoldenrod EEE8AA palegreen 98FB98 paleturquoise AFEEEE palevioletred DB7093
        papayawhip FFEFD5 peachpuff FFDAB9 peru CD853F pink FFC0CB
        plum DDA0DD powderblue B0E0E6 purple 800080 rebeccapurple 663399
        red FF0000 rosybrown BC8F8F royalblue 4169E1 saddlebrown 8B4513
        salmon FA8072 sandybrown F4A460 seagreen 2E8B57 seashell FFF5EE
        sienna A0522D silver C0C0C0 skyblue 87CEEB slateblue 6A5ACD
        slategray 708090 slategrey 708090 snow FFFAFA springgreen 00FF7F
        steelblue 4682B4 tan D2B48C teal 008080 thistle D8BFD8
        tomato FF6347 turquoise 40E0D0 violet EE82EE wheat F5DEB3
        white FFFFFF whitesmoke F5F5F5 yellow FFFF00 yellowgreen 9ACD32
    ");

    /// <summary>How many colours the built-in palette holds, for the gate that counts them.</summary>
    public static int BuiltInColorCount => CssColors.Count;

    private static IReadOnlyDictionary<string, uint> ParseTable(string table)
    {
        var words = table.Split(
            new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        var result = new Dictionary<string, uint>(System.StringComparer.OrdinalIgnoreCase);

        for (int at = 0; at + 1 < words.Length; at += 2)
        {
            string digits = words[at + 1];

            // Six digits are opaque; the one eight-digit entry is `transparent`.
            if (digits.Length == 6)
                digits += "FF";

            result[words[at]] = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return result;
    }
}
