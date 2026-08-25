using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Extensions;
using Tabbit.Messages;
using Tabbit.Models;
using Tabbit.Models.Raw;

namespace Tabbit.Schema;

/// <summary>
/// Every declaration a run read, gathered from all its files and checked as one.
/// </summary>
/// <remarks>
/// **Gathered before anything is checked, which is what makes order not matter.** A member
/// may be typed with a struct declared three files later, or in a file the recipe happens to
/// list first - the declarations resolve after all of them are in, which is exactly how the
/// tables already resolve references to one another. Section 4.6 of the design.
///
/// **Types are closed and references are open.** Every member's type has to be a built-in one
/// or something declared in these files; the tables a `foreign` names are not checked here at
/// all, because a table is not declared in these files and the pass that resolves references
/// is the one that knows about them. That split is what lets an editor read a set of schema
/// files on their own and know every type in them without opening a workbook.
/// </remarks>
public sealed class SchemaDeclarations
{
    // Case-insensitive, and that is not a convenience. A sheet's type cell reaches here
    // through a layout, and one of them lowers the cell before anything looks at it - so
    // `CEquip` arrives as `cequip` and an exact lookup would miss the struct it names. Two
    // declarations differing only in case are refused below, so nothing is ambiguous.
    private readonly Dictionary<string, SchemaStruct> _structs =
        new(System.StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, SchemaEnum> _enums =
        new(System.StringComparer.OrdinalIgnoreCase);

    // Keyed by the abstract struct's name, holding what extends it in the order the files
    // declared them. Built once every file is in, because the base of a variant in the first
    // file may be declared in the last one.
    private readonly Dictionary<string, List<SchemaStruct>> _variants =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Every struct, by the name generated code will spell it with.</summary>
    public IReadOnlyDictionary<string, SchemaStruct> Structs => _structs;

    /// <summary>Every enum, by the name generated code will spell it with.</summary>
    public IReadOnlyDictionary<string, SchemaEnum> Enums => _enums;

    /// <summary>Whether the recipe read any declarations at all.</summary>
    public bool IsEmpty => _structs.Count == 0 && _enums.Count == 0;

    /// <summary>The struct a type cell names, or null when it names something else.</summary>
    public SchemaStruct? FindStruct(string? written)
        => written is { Length: > 0 } && _structs.TryGetValue(written, out var found) ? found : null;

    /// <summary>The enum a type cell names, or null when it names something else.</summary>
    public SchemaEnum? FindEnum(string? written)
        => written is { Length: > 0 } && _enums.TryGetValue(written, out var found) ? found : null;

    /// <summary>The abstract struct a name refers to, or null when it is not one.</summary>
    public SchemaStruct? FindAbstract(string? written)
        => FindStruct(written) is { IsAbstract: true } found ? found : null;

    /// <summary>
    /// The structs that extend an abstract one, in the order they were declared.
    /// </summary>
    /// <remarks>
    /// Empty for a name nothing extends and for a name that is not abstract, which are the
    /// same answer to a caller: there is no set here.
    /// </remarks>
    public IReadOnlyList<SchemaStruct> VariantsOf(string? abstractName)
        => abstractName is { Length: > 0 } && _variants.TryGetValue(abstractName, out var found)
            ? found
            : [];

    /// <summary>
    /// The discriminator value a variant travels under.
    /// </summary>
    /// <remarks>
    /// The one written, or its position among its siblings when the set writes none - the same
    /// rule <see cref="SchemaStruct.TagOf"/> uses for members, and for the same reason. A set
    /// either numbers every variant or numbers none, which <see cref="LinkVariants"/> enforces,
    /// so the two halves of this expression never disagree within one set.
    /// </remarks>
    public int DiscriminatorOf(SchemaStruct variant)
    {
        if (variant.VariantDiscriminator > 0)
            return variant.VariantDiscriminator;

        var siblings = VariantsOf(variant.BaseName);

        for (int at = 0; at < siblings.Count; at++)
        {
            if (ReferenceEquals(siblings[at], variant))
                return at + 1;
        }

        return 0;
    }


    /// <summary>
    /// Parses every file and gathers what they declare, reporting a name declared twice.
    /// </summary>
    /// <remarks>
    /// Structs and enums share one set of names. They are both types a sheet's type cell may
    /// name, so two of them called the same thing leave that cell with nothing to mean -
    /// section 9.1 of the design asked the question and this is the answer.
    /// </remarks>
    public static SchemaDeclarations Read(
        IEnumerable<RawSchemaFile> files, Diagnostics diagnostics)
    {
        var gathered = new SchemaDeclarations();
        var taken = new Dictionary<string, Location>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var raw in files)
        {
            var parsed = SchemaParser.Parse(raw.Text, raw.Name, diagnostics);

            foreach (var declared in parsed.Structs)
            {
                if (gathered.Claim(taken, declared, diagnostics))
                    gathered._structs[declared.Name.ToPascalCase()] = declared;
            }

            foreach (var declared in parsed.Enums)
            {
                if (gathered.Claim(taken, declared, diagnostics))
                    gathered._enums[declared.Name.ToPascalCase()] = declared;
            }
        }

        gathered.LinkVariants(diagnostics);
        return gathered;
    }

    /// <summary>
    /// Puts every `extends` against the declaration it names, and checks the sets that result.
    /// </summary>
    /// <remarks>
    /// **Here rather than in <see cref="Resolve"/>, because none of it needs a workbook.** What
    /// a name extends, whether that name is abstract, and whether two variants claim one
    /// discriminator are all questions the schema files answer on their own - which is what
    /// lets an editor holding a set of `.tbs` files report them without a conversion running.
    ///
    /// A variant whose base does not resolve is left out of every set. It has already been
    /// reported, and putting it somewhere would make the numbering below depend on a mistake.
    /// </remarks>
    private void LinkVariants(Diagnostics diagnostics)
    {
        foreach (var declared in _structs.Values)
        {
            if (declared.BaseName is not { Length: > 0 } written)
                continue;

            string name = written.ToPascalCase();

            if (!_structs.TryGetValue(name, out var found))
            {
                diagnostics.Error(declared.BaseLocation ?? declared.Location, Message.Of(
                    _enums.ContainsKey(name)
                        ? SchemaMessages.BaseNotAbstract
                        : SchemaMessages.BaseUnknown,
                    ("Struct", declared.Name),
                    ("Base", written),
                    ("What", "an enum"),
                    ("Known", KnownAbstracts())));
                continue;
            }

            if (!found.IsAbstract)
            {
                diagnostics.Error(declared.BaseLocation ?? declared.Location, Message.Of(
                    SchemaMessages.BaseNotAbstract,
                    ("Struct", declared.Name),
                    ("Base", written),
                    ("What", "a plain `struct`")));
                continue;
            }

            if (!_variants.TryGetValue(found.Name.ToPascalCase(), out var set))
                _variants[found.Name.ToPascalCase()] = set = [];

            set.Add(declared);
        }

        foreach (var (name, set) in _variants)
            CheckVariantDiscriminators(name, set, diagnostics);
    }

    /// <summary>The abstract structs a mistyped `extends` could have meant.</summary>
    private string KnownAbstracts()
    {
        var named = _structs.Values
            .Where(declared => declared.IsAbstract)
            .Select(declared => declared.Name)
            .OrderBy(spelled => spelled, System.StringComparer.Ordinal)
            .ToList();

        return named.Count == 0 ? "(none declared)" : string.Join(" · ", named);
    }

    /// <summary>
    /// Checks that one set numbers every variant or numbers none, and that no number is used
    /// twice.
    /// </summary>
    private static void CheckVariantDiscriminators(
        string baseName, List<SchemaStruct> set, Diagnostics diagnostics)
    {
        // Any number at all makes the set a numbered one - not the first variant's. Reading
        // only the first took a set whose first variant carried no number for an unnumbered
        // set and left the rest unexamined, and that is the order that collides: the untagged
        // one takes 1 from its position while a tagged sibling takes 1 from its `@1`.
        // spec/polymorphism.md section 5.1.1.
        bool numbered = set.Any(variant => variant.VariantDiscriminator > 0);

        if (numbered)
        {
            foreach (var variant in set.Where(variant => variant.VariantDiscriminator <= 0))
            {
                diagnostics.Error(variant.Location, Message.Of(
                    SchemaMessages.VariantDiscriminatorsPartial,
                    ("Struct", variant.Name), ("Base", baseName)));
            }
        }

        var byTag = new Dictionary<int, SchemaStruct>();

        foreach (var variant in set.Where(variant => variant.VariantDiscriminator > 0))
        {
            if (byTag.TryGetValue(variant.VariantDiscriminator, out var first))
            {
                diagnostics.Error(variant.Location, Message.Of(
                    SchemaMessages.VariantDiscriminatorsCollide,
                    ("Struct", variant.Name),
                    ("Other", first.Name),
                    ("Base", baseName),
                    ("Tag", variant.VariantDiscriminator)));

                continue;
            }

            byTag[variant.VariantDiscriminator] = variant;
        }
    }

    private bool Claim(
        Dictionary<string, Location> taken,
        SchemaDeclaration declared,
        Diagnostics diagnostics)
    {
        string name = declared.Name.ToPascalCase();

        if (taken.TryGetValue(name, out var first))
        {
            diagnostics.Error(declared.Location, Message.Of(
                SchemaMessages.DeclaredTwice, ("Name", declared.Name), ("First", first)));
            return false;
        }

        taken[name] = declared.Location;
        return true;
    }

    /// <summary>
    /// Puts the declared enums into the model, before a sheet is read.
    /// </summary>
    /// <remarks>
    /// Before, and it has to be: a sheet's type cell may name one, and the check that a type
    /// name is recognized asks the model. An enum declared here and in a sheet under the same
    /// name is reported by the sheet's own duplicate check when the sheet gets there, which
    /// is the report that can point at the cell.
    ///
    /// Numbers count on from the last one written, and the first entry with none is zero -
    /// the rule every language with an enum uses, so nobody has to learn a second one. The
    /// recipe's `None` insertion then does what it does for a sheet's enums, which is
    /// nothing when an entry already carries zero.
    /// </remarks>
    public void DeclareEnums(Model model, Diagnostics diagnostics)
    {
        foreach (var (name, declared) in _enums.OrderBy(entry => entry.Key, System.StringComparer.Ordinal))
        {
            var built = new Models.Enum
            {
                Location = declared.Location,
                RawName = declared.Name,
                Name = name,
                Comment = declared.Comment,
                TargetSide = TargetSide.Both,
            };

            long next = 0;

            foreach (var entry in declared.Values)
            {
                long number = entry.Number ?? next;
                next = number + 1;

                if (number is < int.MinValue or > int.MaxValue)
                {
                    diagnostics.Error(entry.Location, Message.Of(
                        SchemaMessages.EnumNumberOutOfRange,
                        ("Enum", declared.Name), ("Entry", entry.Name), ("Number", number)));
                    continue;
                }

                built.Labels.Add(new Models.Enum.Label
                {
                    Location = entry.Location,
                    RawName = entry.Name,
                    Name = entry.Name.ToPascalCase(),
                    Value = (int)number,
                    Comment = entry.Comment,
                });
            }

            model.Enums.Add(built);
        }
    }

    /// <summary>
    /// Checks every member's type, once the sheets have been read too.
    /// </summary>
    /// <remarks>
    /// **Afterwards rather than before, because two of the four checks need the sheets.**
    /// Whether a name collides with a table is one, and whether a member was typed with an
    /// enum a sheet declared is the other - and that one is refused rather than allowed,
    /// because a set of schema files whose types can only be resolved by opening a workbook
    /// is a set no editor can read. Section 4.4 of the design.
    ///
    /// Nothing is kept. This pass answers whether the declarations are sound; what they mean
    /// to a table is settled where a type cell names one, and building a resolved copy here
    /// for nobody to read would be a second answer to keep in step with the first.
    /// </remarks>
    public void Resolve(Model model, CookingContext context, Diagnostics diagnostics)
    {
        if (IsEmpty)
            return;

        RefuseNamesTheSheetsAlreadyGave(model, diagnostics);
        RefuseEmptyVariantSets(model, diagnostics);

        foreach (var declared in _structs.Values)
        {
            foreach (var member in declared.Fields)
                CheckMemberType(declared, member, model, context, diagnostics);
        }

        RefuseCycles(diagnostics);
        RefuseWhatOneCellCannotHold(diagnostics);

        // Once per declaration rather than once per column that uses it: a struct three
        // tables share has one misspelt key, not three.
        SchemaMetadata.Check(this, diagnostics);
        SchemaDefaults.Check(this, diagnostics);
    }

    /// <summary>
    /// Refuses a declaration whose name a sheet has already given to a table or a set of
    /// constants.
    /// </summary>
    /// <remarks>
    /// Enums are left out on purpose: they were put into the model before the sheets were
    /// read, so a sheet declaring one of the same name meets the model's own duplicate check
    /// - and that one points at the cell, which this cannot.
    /// </remarks>
    private void RefuseNamesTheSheetsAlreadyGave(Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
            Collide(table.Name, "a table", table.Location, diagnostics);

        foreach (var constants in model.ConstantSets)
            Collide(constants.Name, "a set of constants", constants.Location, diagnostics);
    }

    /// <summary>
    /// Refuses an abstract struct nothing extends.
    /// </summary>
    /// <remarks>
    /// **Here rather than in <see cref="LinkVariants"/>, because a variant need not be a
    /// struct.** A table declares itself one, and the tables are not read until a workbook is
    /// open - so this is the earliest point that can tell an empty set from a set whose
    /// members are all tables. spec/polymorphism.md section 3.
    /// </remarks>
    private void RefuseEmptyVariantSets(Model model, Diagnostics diagnostics)
    {
        foreach (var declared in _structs.Values)
        {
            if (!declared.IsAbstract)
                continue;

            string name = declared.Name.ToPascalCase();

            if (VariantsOf(name).Count > 0)
                continue;

            diagnostics.Error(declared.Location, Message.Of(
                SchemaMessages.AbstractWithoutVariants, ("Struct", declared.Name)));
        }
    }

    private void Collide(string name, string what, Location? where, Diagnostics diagnostics)
    {
        SchemaDeclaration? declared =
            _structs.TryGetValue(name, out var asStruct) ? asStruct
            : _enums.TryGetValue(name, out var asEnum) ? asEnum
            : null;

        if (declared is null)
            return;

        diagnostics.Error(declared.Location, Message.Of(
            SchemaMessages.NameTakenBySheet,
            ("Name", declared.Name), ("What", what), ("Where", where)));
    }

    private void CheckMemberType(
        SchemaStruct declared,
        SchemaField member,
        Model model,
        CookingContext context,
        Diagnostics diagnostics)
    {
        var type = member.Type;

        switch (type.Form)
        {
            // The tables are not checked here. A schema file names one and declares none, so
            // whether it exists is the reference pass's question - which is also the pass
            // that can say which table names are available.
            case SchemaTypeForm.Foreign:
                return;

            // Read all the way into the declarations and refused here, by name. The wire needs
            // no change for either of them - a set is an array column and a map is two - so
            // what is missing is the container type in every generated language, and refusing
            // at this point means the notation will not have to be settled twice.
            // Section 4.7 of the design.
            case SchemaTypeForm.Container:
                diagnostics.Error(type.Location, Message.Of(
                    SchemaMessages.ContainerNotSupported,
                    ("Struct", declared.Name), ("Member", member.Name), ("Type", type.ToString())));
                return;
        }

        if (context.IsValidTypeName(type.Name) && type.Name is not ("enum" or "foreign"))
            return;

        string spelled = type.Name.ToPascalCase();

        // Refused where it is written rather than left to a later pass, because the notation
        // for it is settled and only what fills a row with it is missing. Value embedding is
        // stage 4 of spec/polymorphism.md; the reference path in stage 3 reaches the same
        // variants through the tables that extend this name.
        if (_structs.TryGetValue(spelled, out var named) && named.IsAbstract)
        {
            diagnostics.Error(type.Location, Message.Of(
                SchemaMessages.AbstractTypeNotEmbeddable,
                ("Struct", declared.Name), ("Member", member.Name), ("Type", named.Name)));
            return;
        }

        if (_structs.ContainsKey(spelled) || _enums.ContainsKey(spelled))
            return;

        // An enum a sheet declared. Refused with a report of its own rather than as an
        // unknown name, because the name does exist and saying it does not would send
        // somebody looking for a spelling mistake.
        if (model.ContainsEnum(type.Name))
        {
            diagnostics.Error(type.Location, Message.Of(
                SchemaMessages.TypeIsSheetEnum,
                ("Struct", declared.Name), ("Member", member.Name), ("Type", type.Name)));
            return;
        }

        diagnostics.Error(type.Location, Message.Of(
            SchemaMessages.TypeUnknown,
            ("Struct", declared.Name), ("Member", member.Name), ("Type", type.Name)));
    }

    /// <summary>
    /// Checks the shape of every struct that writes itself into one cell.
    /// </summary>
    /// <remarks>
    /// Once per declaration rather than once per column, because it is a fact about the
    /// struct: a member that is itself several values has no place in a positional cell
    /// wherever the struct is used. Section 7.3 of the design, which inherits the
    /// restriction from the composite value types rather than inventing it.
    /// </remarks>
    private void RefuseWhatOneCellCannotHold(Diagnostics diagnostics)
    {
        foreach (var declared in _structs.Values)
        {
            string? separator = declared.Meta.Value("sep");

            if (separator is null)
                continue;

            if (separator.Length != 1)
            {
                diagnostics.Error(declared.Meta.LocationOf("sep"), Message.Of(
                    SchemaMessages.SepNotOneCharacter,
                    ("Struct", declared.Name), ("Written", separator)));
            }

            foreach (var member in declared.LiveFields)
            {
                bool scalar = member.Type.Form == SchemaTypeForm.Named
                    && !member.Type.IsArray
                    && FindStruct(member.Type.Name) is null;

                // A reference is one value - the target's key - so it fits a component. What
                // does not is a member that is itself several: a record, an array, or a
                // container.
                if (scalar || (member.Type.Form == SchemaTypeForm.Foreign && !member.Type.IsArray))
                    continue;

                diagnostics.Error(member.Type.Location, Message.Of(
                    SchemaMessages.SepMemberNotScalar,
                    ("Struct", declared.Name),
                    ("Member", member.Name),
                    ("Type", member.Type.ToString())));
            }
        }
    }

    /// <summary>
    /// Refuses a struct that contains itself, however far around.
    /// </summary>
    /// <remarks>
    /// A struct is carried as one column per member, so containing itself has no end -
    /// `A.b.a.b` never stops being a longer column name. A relationship that loops is a
    /// reference and is written as one. Section 9.2 of the design.
    ///
    /// The report names the way round, because the shortest cycle in a set of declarations is
    /// rarely the pair somebody was editing.
    /// </remarks>
    private void RefuseCycles(Diagnostics diagnostics)
    {
        var open = new HashSet<string>(System.StringComparer.Ordinal);
        var settled = new HashSet<string>(System.StringComparer.Ordinal);
        var path = new List<string>();

        foreach (string name in _structs.Keys.OrderBy(key => key, System.StringComparer.Ordinal))
            Walk(name, open, settled, path, diagnostics);
    }

    private void Walk(
        string name,
        HashSet<string> open,
        HashSet<string> settled,
        List<string> path,
        Diagnostics diagnostics)
    {
        if (settled.Contains(name) || !_structs.TryGetValue(name, out var declared))
            return;

        if (!open.Add(name))
        {
            int from = path.IndexOf(name);
            var round = path.Skip(from < 0 ? 0 : from).Append(name);

            diagnostics.Error(declared.Location, Message.Of(
                SchemaMessages.StructCycle, ("Cycle", string.Join(" -> ", round))));

            return;
        }

        path.Add(name);

        foreach (var member in declared.LiveFields)
        {
            if (member.Type.Form == SchemaTypeForm.Named)
                Walk(member.Type.Name.ToPascalCase(), open, settled, path, diagnostics);
        }

        path.RemoveAt(path.Count - 1);
        open.Remove(name);
        settled.Add(name);
    }
}
