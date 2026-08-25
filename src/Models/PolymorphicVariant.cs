namespace Tabbit.Models;

/// <summary>
/// One variant of an abstract type, as the model carries it.
/// </summary>
/// <remarks>
/// Name and number, and nothing else: what a variant's members are is answered by the columns
/// that carry them - <see cref="Field.VariantsDeclaringThis"/> - because a member several
/// variants declare is one column and not one per variant.
///
/// **No enum is generated from this list.** The variant is a declared type, so the type is the
/// discriminator; the number is what the file carries and most languages never show it.
/// spec/polymorphism.md section 7.1.
/// </remarks>
public sealed class PolymorphicVariant
{
    /// <summary>The variant's declared name, Pascal cased.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The number the file carries for it - written as `@N`, or its place among its siblings
    /// when the set numbers none.
    /// </summary>
    /// <remarks>
    /// Fixed rather than positional wherever a sheet wrote it, because a number tied to
    /// declaration order makes a deleted variant read as its neighbour - with no error, since
    /// the value is still one the reader knows. spec/polymorphism.md section 5.1.1.
    /// </remarks>
    public required int Discriminator { get; init; }
}
