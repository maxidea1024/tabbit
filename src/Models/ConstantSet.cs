using Newtonsoft.Json;
using System.Collections.Generic;

namespace Tabbit.Models;

/// <summary>
/// A group of named constants declared with a `~~const:Name~~` marker.
///
/// Emitted as compile-time constants rather than loaded data - a static class in C#,
/// a class of static readonly members in TypeScript, a struct of `static inline
/// const` in C++ - so tuning values can be referenced without a table lookup.
/// </summary>
public class ConstantSet
{
    /// <summary>
    /// One named constant.
    /// </summary>
    public class Constant
    {
        /// <summary>Cell the constant was declared in.</summary>
        [JsonIgnore]
        public required Location Location { get; set; }

        /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
        public required string Name { get; set; }

        /// <summary>Name exactly as written in the sheet.</summary>
        public required string RawName { get; set; }

        /// <summary>
        /// Type as written in the sheet, or the enum's name when the type is `enum`.
        /// </summary>
        public required string TypeName { get; set; }

        /// <summary>Resolved type.</summary>
        public ValueType Type { get; set; }

        /// <summary>The enum declaration, when <see cref="Type"/> is Enum. Null otherwise.</summary>
        public required Enum Enum { get; set; }

        /// <summary>
        /// The value cell's text, kept alongside the parsed value so a generator can
        /// show what the author actually wrote.
        /// </summary>
        public required string? ValueString { get; set; }

        /// <summary>
        /// The parsed value, boxed. Its runtime type follows <see cref="Type"/>; an
        /// enum constant holds the label's integer.
        /// </summary>
        public required object? Value { get; set; }

        /// <summary>Description from the sheet, emitted as a doc comment.</summary>
        public required string Comment { get; set; }
    }

    /// <summary>Cell holding the entity marker that declared this set.</summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>Target side filtering option.</summary>
    public TargetSide TargetSide { get; set; }

    /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
    public required string Name { get; set; }

    /// <summary>Name exactly as written in the sheet.</summary>
    public required string RawName { get; set; }

    /// <summary>The constants, in declaration order.</summary>
    public List<Constant> Constants { get; set; } = new List<Constant>();

    /// <summary>Description from the sheet, emitted as a doc comment.</summary>
    public required string Comment { get; set; }

    /// <summary>Whether a constant of this name exists in the set.</summary>
    public bool ContainsConstant(string constantName) => FindConstant(constantName) is not null;

    /// <summary>
    /// Finds a constant, or throws naming the cell that asked for it.
    /// </summary>
    public Constant GetConstant(string constantName, Location callerLocation)
    {
            var result = FindConstant(constantName)
                ?? throw new TabbitException(callerLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ConstantNotFound,
                        ("Name", constantName), ("Set", Name)));
        return result;
    }

    /// <summary>Finds a constant by name, or null.</summary>
    public Constant? FindConstant(string constantName) => Constants.Find(x => x.Name == constantName);
}
