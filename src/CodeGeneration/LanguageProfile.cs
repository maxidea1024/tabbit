using System;
using System.Collections.Generic;
using Tabbit.Models;
using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// How one output language spells the things Tabbit generates.
///
/// The three generators each carried the same switch over <see cref="ValueType"/> -
/// twenty-seven arms of pure table data, in three places, in three shapes. Adding a
/// language meant writing a fourth. Here it is a table, which is also what a template
/// can read once the generators move to templates.
///
/// Only what is genuinely declarative lives here. Enum and foreign-record types are
/// not in the table because both name something from the model and each language
/// qualifies them its own way, so each generator keeps those two arms. What a
/// generator does with a type - the file layout, the reader calls, the comment syntax -
/// stays in the generator.
/// </summary>
public sealed class LanguageProfile
{
    private readonly HashSet<string> _reservedMemberNames;

    public LanguageProfile(
        string id,
        IReadOnlyDictionary<ValueType, string> scalarTypes,
        string arrayFormat,
        string memberNameEscape,
        IReadOnlyDictionary<ValueType, string> readCalls,
        params string[] reservedMemberNames)
    {
        Id = id;
        ScalarTypes = scalarTypes;
        ArrayFormat = arrayFormat;
        MemberNameEscape = memberNameEscape;
        ReadCalls = readCalls;

        // Ordinal: every language here is case-sensitive about its keywords.
        _reservedMemberNames = new HashSet<string>(reservedMemberNames, StringComparer.Ordinal);
    }

    /// <summary>Matches the target id, so an error message names what the recipe asked for.</summary>
    public string Id { get; }

    /// <summary>
    /// The name of each scalar type in this language.
    ///
    /// Enum and ForeignRecord are deliberately absent; see the type remarks.
    /// </summary>
    public IReadOnlyDictionary<ValueType, string> ScalarTypes { get; }

    /// <summary>
    /// How an array of an already-rendered element type is written, with `{0}` standing
    /// for the element - `{0}[]`, `std::vector&lt;{0}&gt;`.
    /// </summary>
    public string ArrayFormat { get; }

    /// <summary>
    /// Which call on the emitted reader reads each scalar type, with `{0}` standing for the
    /// destination where the language passes one.
    /// </summary>
    /// <remarks>
    /// This was a copy of the same switch, one per generator, each with a `default:` that
    /// throws. So adding a value type meant ten edits and forgetting one still compiled - it
    /// surfaced at runtime in whoever's project reached that field first.
    ///
    /// Here it is ten entries in one file, and `LanguageProfileTests` requires every scalar
    /// type to have one for every language that has a table at all. Adding a type fails that
    /// test naming the languages, in the same file and the same failure that already asks for
    /// the type's name.
    ///
    /// Most languages ignore the `{0}`: their reader returns the value, so the call is an
    /// expression. C's fills an out-parameter, so its entries pass the address.
    ///
    /// Null for C++ and Unreal, whose readers resolve by overload - one `Read` per engine
    /// type rather than a method per name - so there is no per-type call to table. That is a
    /// property of those readers, not an omission, which is why it is null rather than empty.
    ///
    /// Enum and ForeignRecord are absent for the same reason they are absent from
    /// <see cref="ScalarTypes"/>: both name something from the model and each language
    /// qualifies them its own way, so each generator keeps those two arms.
    /// </remarks>
    public IReadOnlyDictionary<ValueType, string> ReadCalls { get; }

    /// <summary>
    /// The reader call for a scalar type, or an error naming the language and the type.
    /// </summary>
    /// <param name="destination">
    /// Where the value goes, for the languages whose reader fills an out-parameter. Ignored
    /// by the rest, whose entries have no placeholder.
    /// </param>
    public string ReadCall(ValueType type, string? destination = null)
    {
        if (ReadCalls is null)
                throw new TabbitDefectException($"The {Id} reader resolves reads by overload, so it has no call table.");

        var element = ValueTypes.ElementOf(type);

        if (ReadCalls.TryGetValue(element, out string? call))
            return string.Format(call, destination);

            throw new TabbitDefectException($"The {Id} generator cannot read type `{type}`.");
    }

    /// <summary>
    /// The name of a scalar type, or an error naming the language and the type.
    ///
    /// Takes an array type as readily as a scalar one and answers for its element,
    /// because every caller renders an array by naming the element and wrapping it.
    /// </summary>
    public string ScalarTypeName(ValueType type)
    {
        var element = ValueTypes.ElementOf(type);

        if (ScalarTypes.TryGetValue(element, out string? name))
            return name;

            throw new TabbitDefectException($"The {Id} generator cannot render type `{type}`.");
    }

    /// <summary>Wraps an already-rendered element type as an array.</summary>
    public string ArrayOf(string elementTypeName) => string.Format(ArrayFormat, elementTypeName);

    /// <summary>
    /// How a reserved name is made usable, with `{0}` standing for the name.
    /// </summary>
    public string MemberNameEscape { get; }

    /// <summary>
    /// Names this language will not accept for a member, after the generator's casing
    /// has been applied.
    ///
    /// After the casing, which is why the three lists differ so much in size: C# renders
    /// members PascalCase and every C# keyword is lowercase, so none of them can survive
    /// into one. TypeScript renders them camelCase but accepts a reserved word as a
    /// member name, so only the handful that are genuinely special appear. C++ renders
    /// them snake_case and accepts nothing, so it has the full keyword list.
    ///
    /// The repository used to hold two lists that nothing read - a C# one whose note
    /// claimed escaping made the problem moot, and a TypeScript one - and no list at all
    /// for C++, the one language that needed it. The reserved-words fixture is what
    /// decides these contents now: the suite compiles its output in all three languages.
    /// </summary>
    public IReadOnlyCollection<string> ReservedMemberNames => _reservedMemberNames;

    /// <summary>
    /// A member name this language will accept, given the cased name.
    ///
    /// Leaves anything usable exactly as it was, so only the colliding names change.
    /// </summary>
    public string MemberName(string casedName)
    {
        return _reservedMemberNames.Contains(casedName)
            ? string.Format(MemberNameEscape, casedName)
            : casedName;
    }

    // ------------------------------------------------------------ profiles

    /// <summary>
    /// C++17. Fixed-width integer names from &lt;cstdint&gt; rather than `int` and
    /// `long long`, whose widths are not fixed by the language; the date, duration and
    /// uuid types come from the emitted reader header.
    /// </summary>
    public static readonly LanguageProfile Cpp = new LanguageProfile(
        "cpp",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "std::string" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "std::int32_t" },
            { ValueType.Int64, "std::int64_t" },
            { ValueType.Float, "float" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "tabbit::DateTime" },
            { ValueType.TimeSpan, "tabbit::TimeSpan" },
            { ValueType.Uuid, "tabbit::Uuid" },
        },
        "std::vector<{0}>",

        // A prefix, not the idiomatic trailing underscore, because the accessor already
        // uses a trailing underscore for its private members: a table called Template
        // would give the method `template_` and the field `template__`, and any
        // identifier containing a double underscore is reserved to the implementation.
        // Escaping the method but not the field instead makes the two collide.
        "tb_{0}",

        // No read-call table: the C++ reader has one `read` overload per type, so what a
        // field reads with does not depend on which type it is.
        null!,
        // https://en.cppreference.com/w/cpp/keyword - the whole list, because a C++
        // member name is snake_case and every keyword is lowercase, so all of them
        // survive the casing.
        "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit",
        "atomic_noexcept", "auto", "bitand", "bitor", "bool", "break", "case", "catch",
        "char", "char8_t", "char16_t", "char32_t", "class", "co_await", "co_return",
        "co_yield", "compl", "concept", "const", "const_cast", "consteval", "constexpr",
        "constinit", "continue", "decltype", "default", "delete", "do", "double",
        "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float",
        "for", "friend", "goto", "if", "inline", "int", "long", "mutable", "namespace",
        "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq",
        "private", "protected", "public", "reflexpr", "register", "reinterpret_cast",
        "requires", "return", "short", "signed", "sizeof", "static", "static_assert",
        "static_cast", "struct", "switch", "synchronized", "template", "this",
        "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
        "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor",
        "xor_eq");

    /// <summary>
    /// C99.
    ///
    /// Fixed-width names from &lt;stdint.h&gt;, as in C++ and for the same reason: `int`
    /// and `long` have no width the language guarantees.
    ///
    /// A string is `const char*` and points into the arena its table owns, so it is
    /// valid until that table is freed and no caller has anything to release. The
    /// reader refuses a value holding an embedded NUL rather than handing back the part
    /// before it - C cannot carry one in a `const char*`, and half a string returned as
    /// the whole of it is exactly the failure this format's readers exist to avoid.
    ///
    /// datetime and timespan are int64 ticks. C has nothing that holds 0001-01-01, and
    /// time_t is a resolution and a range decided by the platform.
    ///
    /// The array format is the element pointer. A variable length array carries a count
    /// beside it, which the generator declares - there is nowhere in the type to put it.
    /// </summary>
    public static readonly LanguageProfile C = new LanguageProfile(
        "c",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "const char*" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int32_t" },
            { ValueType.Int64, "int64_t" },
            { ValueType.Float, "float" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "int64_t" },
            { ValueType.TimeSpan, "int64_t" },
            { ValueType.Uuid, "tb_uuid" },
        },
        "{0}*",

        // A trailing underscore. A leading one would be reserved to the implementation
        // at file scope, and a double one is reserved everywhere.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "tb_read_string(reader, {0})" },
            { ValueType.Bool, "tb_read_bool(reader, {0})" },
            { ValueType.Int32, "tb_read_i32_as(reader, column->element, {0})" },
            { ValueType.Int64, "tb_read_i64_as(reader, column->element, {0})" },
            { ValueType.Float, "tb_read_float(reader, {0})" },
            { ValueType.Double, "tb_read_f64_as(reader, column->element, {0})" },
            { ValueType.DateTime, "tb_read_datetime(reader, {0})" },
            { ValueType.TimeSpan, "tb_read_timespan(reader, {0})" },
            { ValueType.Uuid, "tb_read_uuid(reader, {0})" },
        },

        // C11 keywords. Members are snake_case and every keyword is lowercase, so all
        // of them survive the casing - the same reason C++ carries the full list.
        "alignas", "alignof", "auto", "bool", "break", "case", "char", "complex",
        "const", "continue", "default", "do", "double", "else", "enum", "extern",
        "false", "float", "for", "generic", "goto", "if", "imaginary", "inline", "int",
        "long", "noreturn", "register", "restrict", "return", "short", "signed",
        "sizeof", "static", "static_assert", "struct", "switch", "thread_local",
        "true", "typedef", "typeof", "union", "unsigned", "void", "volatile", "while",
        "_Alignas", "_Alignof", "_Atomic", "_Bool", "_Complex", "_Generic",
        "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local",

        // And the C++ keywords that are not C keywords, which is not fussiness. The
        // generated header wraps itself in `extern "C"`, so it says it can be included
        // from C++ - and a member called `class` or `delete` makes that a lie the C
        // compiler is in no position to catch. The reserved-words fixture has exactly
        // those two, and the C build was green while the header was unusable from the
        // language it advertised.
        "and", "and_eq", "asm", "bitand", "bitor", "catch", "char8_t", "char16_t",
        "char32_t", "class", "co_await", "co_return", "co_yield", "compl", "concept",
        "const_cast", "consteval", "constexpr", "constinit", "decltype", "delete",
        "dynamic_cast", "explicit", "export", "friend", "mutable", "namespace", "new",
        "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private",
        "protected", "public", "reinterpret_cast", "requires", "static_cast",
        "template", "this", "throw", "try", "typeid", "typename", "using", "virtual",
        "wchar_t", "xor", "xor_eq");

    /// <summary>
    /// PHP 8.1 and later.
    ///
    /// int for both int32 and int64, and that is safe where TypeScript and Dart needed
    /// a wider type: PHP's integer is a full 64 bits on any 64 bit build, so 2^53+1
    /// survives. What is not safe is `unpack('P')`, which hands back an unsigned
    /// interpretation PHP cannot hold past 2^63 and turns into a float - the reader
    /// assembles the value from two halves instead.
    ///
    /// float for both float and double: PHP has no single-precision type, so a float32
    /// read widens as it does in Python, Ruby and Dart.
    ///
    /// datetime and timespan are ticks. DateTimeImmutable carries microseconds, not
    /// ticks, and a sheet reaches 0001-01-01 and TimeSpan's full range.
    ///
    /// The array format is a bare `array`: PHP's type declarations have no element
    /// type, so what an array holds is said in a docblock the generator writes.
    /// </summary>
    public static readonly LanguageProfile Php = new LanguageProfile(
        "php",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "string" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int" },
            { ValueType.Int64, "int" },
            { ValueType.Float, "float" },
            { ValueType.Double, "float" },
            { ValueType.DateTime, "int" },
            { ValueType.TimeSpan, "int" },
            { ValueType.Uuid, "Uuid" },
        },
        "array",

        // Never used: the list below is empty. PHP has accepted a reserved word as a
        // property or method name since 7.0, so a field called `class` needs nothing
        // done to it - and renaming one would change the generated API for no reason.
        // The reserved-words fixture is what turns that from an argument into a fact:
        // the suite runs its output through the real interpreter.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "$reader->readString()" },
            { ValueType.Bool, "$reader->readBool()" },
            { ValueType.Int32, "$reader->readI32As($column['element'])" },
            { ValueType.Int64, "$reader->readI64As($column['element'])" },
            { ValueType.Float, "$reader->readFloat()" },
            { ValueType.Double, "$reader->readF64As($column['element'])" },
            { ValueType.DateTime, "$reader->readDateTimeTicks()" },
            { ValueType.TimeSpan, "$reader->readTimespanTicks()" },
            { ValueType.Uuid, "$reader->readUuid()" },
        }
        );

    /// <summary>
    /// C#. The three framework types are fully qualified so a generated file needs no
    /// `using System` and cannot collide with a namespace the consumer already has.
    /// </summary>
    public static readonly LanguageProfile CSharp = new LanguageProfile(
        "csharp",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "string" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int" },
            { ValueType.Int64, "long" },
            { ValueType.Float, "float" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "System.DateTime" },
            { ValueType.TimeSpan, "System.TimeSpan" },
            { ValueType.Uuid, "System.Guid" },
        },
        "{0}[]",

        // `@class` is what C# itself offers for a name that is also a keyword, and it is a
        // name rather than a rename: the member is still called `class` to anything reading
        // it by reflection or by name.
        "@{0}",

        // No read-call table: the C# reader has one `Read(out T)` overload per type.
        null!,

        // Every C# keyword, all of them lower case.
        //
        // This list used to be empty, with a comment saying why: members are rendered
        // PascalCase, so `class` arrives as `Class` and nothing can collide. That was true
        // and it stays true - at Pascal case not one entry below is ever reached, which is
        // why filling it in changes no output. It was also an argument that held only while
        // the spelling was fixed, and a list that is empty for a reason nobody can see from
        // the list is a trap for whoever changes that.
        //
        // Contextual keywords are left out. `value`, `var`, `record` and the rest are
        // keywords only where the grammar expects one, and a member named `Value` is
        // ordinary C# - escaping it would be noise in every generated file. What the
        // grammar reserves outright is what is here.
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while");

    /// <summary>
    /// Go.
    ///
    /// The least eventful of the profiles: int64 is int64, float32 is float32, and a
    /// uint32 shifts the way varint decoding wants. Nothing has to be worked around.
    ///
    /// datetime and timespan are int64 ticks rather than time.Time and time.Duration,
    /// and that is not a matter of taste. Both of those count nanoseconds in an int64,
    /// which spans about 1678 to 2262 for an instant and about 292 years for a duration.
    /// The corpus holds 0001-01-01 and TimeSpan.MaxValue, and both overflow. Ticks are
    /// exact for everything a sheet can hold; the reader offers Time and Duration for a
    /// caller who knows their range.
    /// </summary>
    public static readonly LanguageProfile Go = new LanguageProfile(
        "go",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "string" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int32" },
            { ValueType.Int64, "int64" },
            { ValueType.Float, "float32" },
            { ValueType.Double, "float64" },
            { ValueType.DateTime, "int64" },
            { ValueType.TimeSpan, "int64" },
            { ValueType.Uuid, "tabbit.UUID" },
        },
        "[]{0}",

        // Never used, as with C#: Go exports a name by capitalizing it and every Go
        // keyword is lowercase, so none can survive into an exported member.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.ReadString()" },
            { ValueType.Bool, "reader.ReadBool()" },
            { ValueType.Int32, "reader.ReadI32As(column.Element)" },
            { ValueType.Int64, "reader.ReadI64As(column.Element)" },
            { ValueType.Float, "reader.ReadFloat32()" },
            { ValueType.Double, "reader.ReadF64As(column.Element)" },
            { ValueType.DateTime, "reader.ReadDateTimeTicks()" },
            { ValueType.TimeSpan, "reader.ReadDurationTicks()" },
            { ValueType.Uuid, "reader.ReadUUID()" },
        }
        );

    /// <summary>
    /// Rust.
    ///
    /// As uneventful as Go on the numbers: i64 is i64, f32 is f32, and the shifts
    /// behave. datetime and timespan are i64 ticks for a different reason than Go's -
    /// std has no date type at all, and the values a sheet can hold reach 0001-01-01
    /// and 9999-12-31, which most crates' types cannot express either.
    ///
    /// Unlike Go and C#, Rust does need escaping: members are snake_case and every Rust
    /// keyword is lowercase, so a field called `Type` becomes `type` and stops the
    /// compiler.
    /// </summary>
    public static readonly LanguageProfile Rust = new LanguageProfile(
        "rust",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "i32" },
            { ValueType.Int64, "i64" },
            { ValueType.Float, "f32" },
            { ValueType.Double, "f64" },
            { ValueType.DateTime, "i64" },
            { ValueType.TimeSpan, "i64" },
            { ValueType.Uuid, "tabbit::Uuid" },
        },
        "Vec<{0}>",

        // A trailing underscore rather than a raw identifier. `r#type` is the idiomatic
        // escape but does not work for all of them - `crate`, `self`, `super` and `Self`
        // cannot be raw - and one rule that always holds beats two that nearly do.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.read_string()?" },
            { ValueType.Bool, "reader.read_bool()?" },
            { ValueType.Int32, "reader.read_i32_as(column.element)?" },
            { ValueType.Int64, "reader.read_i64_as(column.element)?" },
            { ValueType.Float, "reader.read_f32()?" },
            { ValueType.Double, "reader.read_f64_as(column.element)?" },
            { ValueType.DateTime, "reader.read_datetime_ticks()?" },
            { ValueType.TimeSpan, "reader.read_duration_ticks()?" },
            { ValueType.Uuid, "reader.read_uuid()?" },
        },

        // https://doc.rust-lang.org/reference/keywords.html - strict, reserved and the
        // weak ones. Members are snake_case, so the lowercase ones are what they meet;
        // `Self` is here because enum labels and enum type names are PascalCase, and it
        // is the one Rust keyword that is. A sheet with a label called `Self` used to
        // generate `Self = 1`, which is not an identifier Rust accepts anywhere.
        "Self",
        "as", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern",
        "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move",
        "mut", "pub", "ref", "return", "self", "static", "struct", "super", "trait",
        "true", "type", "unsafe", "use", "where", "while", "async", "await", "abstract",
        "become", "box", "do", "final", "macro", "override", "priv", "try", "typeof",
        "unsized", "virtual", "yield", "gen", "union");

    /// <summary>
    /// Python.
    ///
    /// The scalar names are only used for documentation - Python is not annotated here -
    /// but the entries record what a value becomes, and two are worth stating.
    ///
    /// float is `float`, which is a double: Python has no single-precision type, so a
    /// float32 read widens. The value is exactly the one stored, held in a wider type,
    /// and printing it shows digits the original 32 bits never carried - which is why
    /// the conformance comparison narrows before comparing.
    ///
    /// datetime and timespan are ticks. `datetime` cannot hold a tick, only a
    /// microsecond, and `timedelta` tops out near 2,700,000 days where TimeSpan reaches
    /// about 29,000 years.
    /// </summary>
    public static readonly LanguageProfile Python = new LanguageProfile(
        "python",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "str" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int" },
            { ValueType.Int64, "int" },
            { ValueType.Float, "float" },
            { ValueType.Double, "float" },
            { ValueType.DateTime, "int" },
            { ValueType.TimeSpan, "int" },
            { ValueType.Uuid, "tabbit.Uuid" },
        },
        "list[{0}]",

        // A trailing underscore, which is what PEP 8 prescribes for exactly this.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.read_string()" },
            { ValueType.Bool, "reader.read_bool()" },
            { ValueType.Int32, "reader.read_i32_as(column.element)" },
            { ValueType.Int64, "reader.read_i64_as(column.element)" },
            { ValueType.Float, "reader.read_float()" },
            { ValueType.Double, "reader.read_f64_as(column.element)" },
            { ValueType.DateTime, "reader.read_datetime_ticks()" },
            { ValueType.TimeSpan, "reader.read_duration_ticks()" },
            { ValueType.Uuid, "reader.read_uuid()" },
        },

        // https://docs.python.org/3/reference/lexical_analysis.html#keywords, plus the
        // soft keywords. Members are snake_case and nearly every keyword is lowercase.
        "False", "None", "True", "and", "as", "assert", "async", "await", "break",
        "class", "continue", "def", "del", "elif", "else", "except", "finally", "for",
        "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
        "pass", "raise", "return", "try", "while", "with", "yield", "match", "case",
        "type");

    /// <summary>
    /// Java.
    ///
    /// The first language with no unsigned types, which is where the format's varint
    /// decoding goes wrong if nobody is watching: a byte with its high bit set is
    /// negative and must be masked before it is shifted, and undoing the zig-zag fold
    /// needs the unsigned shift rather than the arithmetic one. Both live in the
    /// reader; nothing about the type table shows it.
    ///
    /// datetime and timespan are ticks, as everywhere but C# and C++. Instant and
    /// Duration could hold these values, but the conversion is lossy coming back and a
    /// caller passing the value through should not pay for it.
    /// </summary>
    public static readonly LanguageProfile Java = new LanguageProfile(
        "java",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "boolean" },
            { ValueType.Int32, "int" },
            { ValueType.Int64, "long" },
            { ValueType.Float, "float" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "long" },
            { ValueType.TimeSpan, "long" },
            { ValueType.Uuid, "TcbReader.Uuid" },
        },
        "{0}[]",

        // A trailing underscore. Java has no escape for an identifier that lands on a
        // keyword, so the name has to change.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.readString()" },
            { ValueType.Bool, "reader.readBool()" },
            { ValueType.Int32, "reader.readI32As(column.element)" },
            { ValueType.Int64, "reader.readI64As(column.element)" },
            { ValueType.Float, "reader.readFloat()" },
            { ValueType.Double, "reader.readF64As(column.element)" },
            { ValueType.DateTime, "reader.readDateTimeTicks()" },
            { ValueType.TimeSpan, "reader.readDurationTicks()" },
            { ValueType.Uuid, "reader.readUuid()" },
        },

        // https://docs.oracle.com/javase/specs/jls/se21/html/jls-3.html#jls-3.9 - the
        // keywords and the three literals, all reserved as identifiers.
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char",
        "class", "const", "continue", "default", "do", "double", "else", "enum",
        "extends", "final", "finally", "float", "for", "goto", "if", "implements",
        "import", "instanceof", "int", "interface", "long", "native", "new", "package",
        "private", "protected", "public", "return", "short", "static", "strictfp",
        "super", "switch", "synchronized", "this", "throw", "throws", "transient",
        "try", "void", "volatile", "while", "true", "false", "null");

    /// <summary>
    /// Unreal C++.
    ///
    /// The engine's own types rather than the standard library's, because a generated
    /// row is a USTRUCT and the header tool only understands what the engine declares.
    ///
    /// double is here and is a trap worth naming: UE4's header tool rejects `double` as
    /// a UPROPERTY outright, and UE5 accepts it. The generator writes the member either
    /// way and leaves the UPROPERTY off it, which works on both - the field is read and
    /// usable from C++, and only Blueprint cannot see it.
    /// </summary>
    public static readonly LanguageProfile Unreal = new LanguageProfile(
        "unreal",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "FString" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int32" },
            { ValueType.Int64, "int64" },
            { ValueType.Float, "float" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "FDateTime" },
            { ValueType.TimeSpan, "FTimespan" },
            { ValueType.Uuid, "FGuid" },
        },
        "TArray<{0}>",

        // Never used: members are PascalCase and every C++ keyword is lowercase, the
        // same reason C# and Go escape nothing.
        "{0}_",

        // No read-call table: the Unreal reader has one `Read` overload per engine type.
        null!
        );

    /// <summary>
    /// Kotlin.
    ///
    /// Same JVM traps as Java in the reader - a signed byte to mask, an unsigned shift
    /// to undo the zig-zag - and one thing Java does not have: backticks, which escape
    /// an identifier that lands on a keyword instead of forcing the name to change.
    /// </summary>
    public static readonly LanguageProfile Kotlin = new LanguageProfile(
        "kotlin",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "Boolean" },
            { ValueType.Int32, "Int" },
            { ValueType.Int64, "Long" },
            { ValueType.Float, "Float" },
            { ValueType.Double, "Double" },
            { ValueType.DateTime, "Long" },
            { ValueType.TimeSpan, "Long" },
            { ValueType.Uuid, "Uuid" },
        },
        "MutableList<{0}>",

        // Backticks, which is what Kotlin provides for exactly this.
        "`{0}`",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.readString()" },
            { ValueType.Bool, "reader.readBool()" },
            { ValueType.Int32, "reader.readI32As(column.element)" },
            { ValueType.Int64, "reader.readI64As(column.element)" },
            { ValueType.Float, "reader.readFloat()" },
            { ValueType.Double, "reader.readF64As(column.element)" },
            { ValueType.DateTime, "reader.readDateTimeTicks()" },
            { ValueType.TimeSpan, "reader.readDurationTicks()" },
            { ValueType.Uuid, "reader.readUuid()" },
        },

        // https://kotlinlang.org/docs/keyword-reference.html - the hard keywords, which
        // are the ones an identifier cannot be without them.
        "as", "break", "class", "continue", "do", "else", "false", "for", "fun", "if",
        "in", "interface", "is", "null", "object", "package", "return", "super", "this",
        "throw", "true", "try", "typealias", "typeof", "val", "var", "when", "while");

    /// <summary>
    /// Swift.
    ///
    /// Widths are spelled out - `Int32` rather than `Int` - because nine of the thirteen
    /// languages before it do and because a reference key that lost its width is a defect
    /// this repository has already had once. spec/reference-key-types.md.
    ///
    /// The reader's names are all under `Tcb`, so this table carries that prefix where a
    /// type is named. A file copied into somebody else's module cannot put thirty constants
    /// called `magic` and `headerSize` at its top level.
    /// </summary>
    public static readonly LanguageProfile Swift = new LanguageProfile(
        "swift",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "Bool" },
            { ValueType.Int32, "Int32" },
            { ValueType.Int64, "Int64" },
            { ValueType.Float, "Float" },
            { ValueType.Double, "Double" },
            { ValueType.DateTime, "Int64" },
            { ValueType.TimeSpan, "Int64" },
            { ValueType.Uuid, "Tcb.Uuid" },
        },
        "[{0}]",

        // Backticks, the same escape Kotlin has: the name stays what the sheet called it.
        "`{0}`",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.readString()" },
            { ValueType.Bool, "reader.readBool()" },
            { ValueType.Int32, "reader.readI32As(column.element)" },
            { ValueType.Int64, "reader.readI64As(column.element)" },
            { ValueType.Float, "reader.readFloat()" },
            { ValueType.Double, "reader.readF64As(column.element)" },
            { ValueType.DateTime, "reader.readDateTimeTicks()" },
            { ValueType.TimeSpan, "reader.readDurationTicks()" },
            { ValueType.Uuid, "reader.readUuid()" },
        },

        // https://docs.swift.org/swift-book - the keywords used in declarations, in
        // statements and in expressions. Swift lets a member be named after one of these
        // in backticks, so the escape above is what this list feeds.
        "Any", "as", "associatedtype", "await", "break", "case", "catch", "class",
        "continue", "default", "defer", "deinit", "do", "else", "enum", "extension",
        "fallthrough", "false", "fileprivate", "for", "func", "guard", "if", "import",
        "in", "init", "inout", "internal", "is", "let", "nil", "operator", "precedencegroup",
        "private", "protocol", "public", "repeat", "rethrows", "return", "self", "Self",
        "static", "struct", "subscript", "super", "switch", "throw", "throws", "true",
        "try", "typealias", "var", "where", "while");

    /// <summary>
    /// Ruby.
    ///
    /// The names are documentation only - Ruby is not annotated here - but they record
    /// what a value becomes. Integer is arbitrary precision, so the 2^53 boundary costs
    /// nothing; Float is a double, so a float32 read widens as it does in Python.
    /// </summary>
    public static readonly LanguageProfile Ruby = new LanguageProfile(
        "ruby",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "Boolean" },
            { ValueType.Int32, "Integer" },
            { ValueType.Int64, "Integer" },
            { ValueType.Float, "Float" },
            { ValueType.Double, "Float" },
            { ValueType.DateTime, "Integer" },
            { ValueType.TimeSpan, "Integer" },
            { ValueType.Uuid, "Tabbit::Uuid" },
        },
        "Array",

        // A trailing underscore. Ruby has no escape for an identifier that lands on a
        // keyword, so the name has to change.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.read_string" },
            { ValueType.Bool, "reader.read_bool" },
            { ValueType.Int32, "reader.read_i32_as(column.element)" },
            { ValueType.Int64, "reader.read_i64_as(column.element)" },
            { ValueType.Float, "reader.read_float" },
            { ValueType.Double, "reader.read_f64_as(column.element)" },
            { ValueType.DateTime, "reader.read_datetime_ticks" },
            { ValueType.TimeSpan, "reader.read_duration_ticks" },
            { ValueType.Uuid, "reader.read_uuid" },
        },

        // https://docs.ruby-lang.org/en/master/keywords_rdoc.html
        "BEGIN", "END", "alias", "and", "begin", "break", "case", "class", "def",
        "defined?", "do", "else", "elsif", "end", "ensure", "false", "for", "if", "in",
        "module", "next", "nil", "not", "or", "redo", "rescue", "retry", "return",
        "self", "super", "then", "true", "undef", "unless", "until", "when", "while",
        "yield", "__FILE__", "__LINE__", "__ENCODING__");

    /// <summary>
    /// Dart.
    ///
    /// int64 and both tick counts are BigInt, not int. Dart's int is 64 bits on the VM
    /// and a double on the web, where it carries 53 - and a value past that does not
    /// fail there, it comes back changed. The same call the TypeScript profile makes.
    ///
    /// float is double, as in Python and Ruby: Dart has no single-precision type.
    /// </summary>
    public static readonly LanguageProfile Dart = new LanguageProfile(
        "dart",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "String" },
            { ValueType.Bool, "bool" },
            { ValueType.Int32, "int" },
            { ValueType.Int64, "BigInt" },
            { ValueType.Float, "double" },
            { ValueType.Double, "double" },
            { ValueType.DateTime, "BigInt" },
            { ValueType.TimeSpan, "BigInt" },
            { ValueType.Uuid, "Uuid" },
        },
        "List<{0}>",

        // A trailing underscore. Dart has no escape either, and a leading one would
        // make the member private to its library.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.readString()" },
            { ValueType.Bool, "reader.readBool()" },
            { ValueType.Int32, "reader.readI32As(column.element)" },
            { ValueType.Int64, "reader.readI64As(column.element)" },
            { ValueType.Float, "reader.readFloat()" },
            { ValueType.Double, "reader.readF64As(column.element)" },
            { ValueType.DateTime, "reader.readDateTimeTicks()" },
            { ValueType.TimeSpan, "reader.readDurationTicks()" },
            { ValueType.Uuid, "reader.readUuid()" },
        },

        // https://dart.dev/language/keywords - the reserved words, which are the ones
        // an identifier cannot be. The built-in and contextual ones are legal.
        "assert", "break", "case", "catch", "class", "const", "continue", "default",
        "do", "else", "enum", "extends", "false", "final", "finally", "for", "if", "in",
        "is", "new", "null", "rethrow", "return", "super", "switch", "this", "throw",
        "true", "try", "var", "void", "while", "with",

        // And the built-in type names, which are not keywords at all - they are
        // ordinary identifiers, which is exactly the problem. A field named `int`
        // shadows the type inside its own class, so `int int = 0;` does not compile
        // and neither does any `int` declaration after it. Only the lower-case ones
        // can be reached: a member name is camelCase, so `String` arrives as `string`
        // and collides with nothing.
        "bool", "double", "dynamic", "int", "num");

    /// <summary>
    /// TypeScript.
    ///
    /// Two entries here are not the obvious ones, and both are about values arriving
    /// wrong rather than failing to arrive:
    ///
    /// int64 is `bigint`, not `number`. A double carries 53 bits of mantissa, so a
    /// 64-bit value past 2^53 comes back quietly changed - the same class of corruption
    /// the binary writer itself once had, and just as invisible.
    ///
    /// datetime, timespan and uuid are `string`. TypeScript reads the JSON export and
    /// JSON has none of the three, so each arrives as text. Declaring `Date` would
    /// oblige the generated reader to parse on load - work a consumer may not want, on
    /// a value it may only pass through - and there is nothing to parse a duration or a
    /// uuid into at all. The text is exactly what was exported.
    /// </summary>
    public static readonly LanguageProfile Typescript = new LanguageProfile(
        "typescript",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "string" },
            { ValueType.Bool, "boolean" },
            { ValueType.Int32, "number" },
            { ValueType.Int64, "bigint" },
            { ValueType.Float, "number" },
            { ValueType.Double, "number" },
            { ValueType.DateTime, "string" },
            { ValueType.TimeSpan, "string" },
            { ValueType.Uuid, "string" },
        },
        "{0}[]",

        // A trailing underscore is safe here: TypeScript's private members carry a
        // leading one, so the two conventions cannot combine into anything illegal.
        "{0}_",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader.readString()" },
            { ValueType.Bool, "reader.readBool()" },
            { ValueType.Int32, "reader.readI32As(column.element)" },
            { ValueType.Int64, "reader.readI64As(column.element)" },
            { ValueType.Float, "reader.readFloat()" },
            { ValueType.Double, "reader.readF64As(column.element)" },
            { ValueType.DateTime, "reader.readDateTime()" },
            { ValueType.TimeSpan, "reader.readTimeSpan()" },
            { ValueType.Uuid, "reader.readUuid()" },
        },

        // Not the reserved words. TypeScript accepts `class`, `function`, `delete` and
        // the rest as member names, and escaping them would rename the generated API
        // for no reason. These three are the ones a class genuinely cannot declare:
        // `constructor` because an accessor may not be called that - which is exactly
        // what the compiler said about the reserved-words fixture, TS1341 - and the
        // other two because they are how an object's own machinery is reached.
        "constructor", "prototype", "__proto__");

    /// <summary>
    /// Lua - LuaJIT 2.1 and Lua 5.3+.
    ///
    /// The scalar names are lua-language-server annotation types rather than
    /// declarations: Lua declares nothing, and the annotations are where a generated
    /// field's type is written down. int64 is `integer`, which both supported runtimes
    /// hold losslessly - 5.3+ natively, LuaJIT as FFI cdata - and which is why plain
    /// Lua 5.1 is not a target. spec/lua-language-support.md.
    ///
    /// The escape does not rename: a keyword-named field keeps its name as a table key
    /// and the generated code reaches it with bracket syntax, `row["end"]`. The escape
    /// format spells that bracket form; the generator decides per position whether the
    /// dotted or the bracketed access applies.
    /// </summary>
    public static readonly LanguageProfile Lua = new LanguageProfile(
        "lua",
        new Dictionary<ValueType, string>
        {
            { ValueType.String, "string" },
            { ValueType.Bool, "boolean" },
            { ValueType.Int32, "integer" },
            { ValueType.Int64, "integer" },
            { ValueType.Float, "number" },
            { ValueType.Double, "number" },
            { ValueType.DateTime, "integer" },
            { ValueType.TimeSpan, "integer" },
            { ValueType.Uuid, "string" },
        },
        "{0}[]",

        // Bracket-string access, which is Lua's way of keeping a keyword-named key.
        "[\"{0}\"]",

        new Dictionary<ValueType, string>
        {
            { ValueType.String, "reader:readString()" },
            { ValueType.Bool, "reader:readBool()" },
            { ValueType.Int32, "reader:readI32As(column.element)" },
            { ValueType.Int64, "reader:readI64As(column.element)" },
            { ValueType.Float, "reader:readF32()" },
            { ValueType.Double, "reader:readF64As(column.element)" },
            { ValueType.DateTime, "reader:readDateTimeTicks()" },
            { ValueType.TimeSpan, "reader:readDurationTicks()" },
            { ValueType.Uuid, "reader:readUuid()" },
        },

        // https://www.lua.org/manual/5.4/manual.html#3.1 - every keyword is lowercase,
        // so a camelCase member lands on one exactly when the sheet's name is a single
        // lowercase word.
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function",
        "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then",
        "true", "until", "while");
}
