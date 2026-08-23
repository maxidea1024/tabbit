using System.Collections.Generic;
using Tabbit.Cooking;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Schema;

/// <summary>
/// What a member holds where a row wrote nothing.
/// </summary>
/// <remarks>
/// **A default answers what a blank cell means, so it is the row's value.** Once it is
/// applied the cell has a value in every sense the rest of the tool uses: the required check
/// passes, the presence bitmap says present, and a consumer reads the number rather than the
/// type's empty one. Anything less would be a default that only some of the tool believed in.
///
/// **It is applied where the written text is still in hand**, which is the conversion in
/// <see cref="SchemaFieldTypes"/>. A column left for a declaration to type is carried as a
/// string until then, so its blank cell already reads as an empty string that a row wrote -
/// and a later pass could no longer tell it from one somebody typed nothing into.
///
/// **Two shapes are refused for saying two things about one blank.** `int? = 5` says both
/// that a row may leave this out and that leaving it out means five - and the second answer
/// makes the first unreachable. A default on an index column is worse: every row that leaves
/// it blank would share one key, which is what an absent index is already refused for.
///
/// **And one consequence is worth knowing rather than special-casing.** A member with a
/// default always has a value, so an element of a record array holding one is never empty -
/// which means `TrimTrailingArrayElements` will not trim it. That follows from what a default
/// is, and a sheet that wants its arrays trimmed does not declare defaults on the struct they
/// hold. Section 11 stage four of the design.
/// </remarks>
internal static class SchemaDefaults
{
    /// <summary>
    /// Refuses the shapes that say two things about one blank cell, once per declaration.
    /// </summary>
    public static void Check(SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        foreach (var declared in declarations.Structs.Values)
        {
            foreach (var member in declared.Fields)
            {
                if (member.DefaultValue is null)
                    continue;

                if (member.Type.IsOptional || member.Type.ElementsAreOptional)
                {
                    diagnostics.Error(member.Location, Message.Of(
                        SchemaMessages.DefaultAndOptional,
                        ("Struct", declared.Name),
                        ("Member", member.Name),
                        ("Type", member.Type.ToString()),
                        ("Written", member.DefaultValue)));
                }
            }
        }
    }

    /// <summary>
    /// The value a member's declared default stands for, or null when it declares none.
    /// </summary>
    /// <remarks>
    /// Read once per column rather than once per cell: the literal is the same string every
    /// time and parsing it is the same answer. A literal the member's type cannot read is
    /// reported against the declaration, which is where it is written.
    /// </remarks>
    public static object? Read(
        CookingContext context,
        SchemaField member,
        SchemaFieldTypes.Resolved resolved,
        Diagnostics diagnostics)
    {
        if (member.DefaultValue is null)
            return null;

        // A reference's default is a key, and which type that is depends on the target - so
        // it stays text for the pass that resolves it, exactly as a written cell does.
        if (resolved.RefTables is not null)
            return member.DefaultValue;

        var declaredEnum = resolved.Type is Models.ValueType.Enum or Models.ValueType.EnumArray
            ? context.Model.FindEnum(resolved.TypeName)
            : null;

        try
        {
            return context.ParseValue(
                resolved.Type, declaredEnum, member.DefaultValue, member.Location);
        }
        catch (TabbitException problem)
        {
            // `Detail` is the parser's own sentence. The frame around it is translatable and
            // what it quotes stays as it arrived.
            diagnostics.Error(member.Location, Message.Of(
                SchemaMessages.DefaultUnreadable,
                ("Member", member.Name),
                ("Type", member.Type.ToString()),
                ("Written", member.DefaultValue),
                ("Detail", problem.Message)));

            return null;
        }
    }
}
