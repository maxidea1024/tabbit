namespace Tabbit.Models;

/// <summary>
/// What a string column is for, beyond holding characters.
/// </summary>
/// <remarks>
/// A role is not a type. Every value here is a `string` on the wire, in the generated code
/// and in every exported file - the role changes nothing about what is carried, only what
/// else is done with it on the way past.
///
/// That separation is the point. Making these `ValueType` members instead would put a new
/// case in thirteen code generators, the binary format, the database exporters and the
/// schema baseline, all to arrive at the same `string` each of them already emits - and a
/// generator that missed the case would produce something plausible rather than fail. A
/// role is read by the code that acts on it and ignored by everything else, so a column
/// that changes from `string` to `text` does not move a single output byte.
/// </remarks>
public enum StringRole
{
    /// <summary>An ordinary string. Nothing beyond the value is done with it.</summary>
    None = 0,

    /// <summary>
    /// A string shown to a person, gathered for translation.
    ///
    /// <see cref="Field.RoleGroup"/> names the set it is gathered into.
    /// </summary>
    Text = 1,

    /// <summary>
    /// A string naming a file that has to exist.
    /// </summary>
    /// <remarks>
    /// <see cref="Field.RoleGroup"/> names the kind - `icon`, `sfx` - which selects the
    /// folders the recipe pointed at. The kind exists because different kinds live in
    /// different places, and a name that is a valid icon is not thereby a valid sound.
    ///
    /// This tool still does not know what an asset is. It knows that a recipe named some
    /// folders and that a value should match a file in one of them; what a `.uasset` is,
    /// what a texture is, and whether the file is any good are all questions it leaves alone.
    /// </remarks>
    Asset = 2,
}
