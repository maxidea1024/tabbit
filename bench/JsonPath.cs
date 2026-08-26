using System.Collections;
using System.Buffers.Text;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabbit.Bench;

/// <summary>
/// The JSON side of the benchmark: typed loading of the named and compact exports.
///
/// The generated reader only reads binary, so JSON needs the code a team shipping
/// JSON would have written themselves: a DTO class per table and
/// <c>JsonSerializer.Deserialize&lt;List&lt;Dto&gt;&gt;</c>. The DTO types are emitted at
/// startup by mirroring each generated <c>Record</c> - same field names, same types -
/// so they track regeneration instead of hardcoding this dataset's schema. Emission
/// happens once, outside every timed window, and System.Text.Json serializes an
/// emitted type exactly as it would a compiled one.
/// </summary>
internal static class JsonPath
{
    /// <summary>
    /// Everything the loaders need to know about one table, resolved once at startup.
    /// </summary>
    internal sealed class TablePlan
    {
        public string Name;                  // file base name, e.g. "Character"
        public Type TableType;               // generated accessor table class
        public PropertyInfo[] RecordProps;   // Record's values, in declaration order
        public Type DtoType;                 // emitted mirror of Record
        public Type ListType;                // List<Dto>
        public Func<object> CreateDto;       // compiled new Dto()
        public ColumnPlan[] Columns;         // per-field plan, same order as RecordProps
        public IndexPlan[] Indexes;          // one per RecordsByX dictionary on the table
    }

    internal sealed class ColumnPlan
    {
        public FieldInfo Field;
        public ColKind Kind;
        public Delegate Setter;              // Action<object, T> with T per Kind
    }

    internal enum ColKind
    {
        I32,        // int and enums: a JSON number
        I64,        // long: written as a JSON string by the exporter
        F32,        // float
        Bool,
        Str,
        I32Array,   // variable-length arrays nest as JSON arrays in both JSON forms
        F32Array,
    }

    internal sealed class IndexPlan
    {
        public string Name;                  // "RecordsById" and its like
        public MethodInfo Build;             // BuildIndex<TDto, TKey> closed over this index
        public Delegate GetKey;              // Func<TDto, TKey>
    }

    /// <summary>One loaded table: the rows plus the same lookups the binary reader builds.</summary>
    internal sealed class LoadedTable
    {
        public TablePlan Plan;
        public IList Rows;
        public object[] Indexes;
    }

    // AllowReadingFromString because the exporter writes 64-bit integers as strings -
    // the documented guard against JSON readers that only have a double. A JSON-shipping
    // team faces the same problem and this is the cheapest correct answer to it.
    internal static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    internal static TablePlan[] BuildPlans(string namedJsonDir)
    {
        var accessor = typeof(Sprout.Tables.Tables);
        var moduleBuilder = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("Tabbit.Bench.Dtos"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("dtos");

        var plans = new List<TablePlan>();
        foreach (var tableProp in accessor.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            var recordType = tableProp.PropertyType.GetNestedType("Record");
            if (recordType == null)
                continue;

            var plan = new TablePlan
            {
                Name = tableProp.Name,
                TableType = tableProp.PropertyType,
                RecordProps = recordType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .OrderBy(p => p.MetadataToken)
                    .ToArray(),
            };

            // The DTO's JSON names come from the file itself rather than from a copy of
            // the exporter's casing rules: the first row's keys are in field order, which
            // is also the Record's property order. A copied rule could drift; the file
            // cannot.
            string[] jsonNames = ReadFirstRowKeys(Path.Combine(namedJsonDir, plan.Name + ".json"));
            if (jsonNames != null && jsonNames.Length != plan.RecordProps.Length)
                throw new InvalidOperationException(
                    $"{plan.Name}: named JSON has {jsonNames.Length} keys but the record has {plan.RecordProps.Length} values.");

            plan.DtoType = EmitDto(moduleBuilder, plan, jsonNames);
            plan.ListType = typeof(List<>).MakeGenericType(plan.DtoType);
            plan.CreateDto = Expression.Lambda<Func<object>>(Expression.New(plan.DtoType)).Compile();
            plan.Columns = plan.RecordProps
                .Select(p => BuildColumn(plan.DtoType.GetField(p.Name)))
                .ToArray();
            plan.Indexes = BuildIndexPlans(plan);

            plans.Add(plan);
        }

        return plans.ToArray();
    }

    private static string[] ReadFirstRowKeys(string filename)
    {
        var reader = new Utf8JsonReader(File.ReadAllBytes(filename));
        reader.Read();                       // outer StartArray
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;                     // empty table; names never get used

        var keys = new List<string>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            keys.Add(reader.GetString());
            reader.Skip();
        }

        return keys.ToArray();
    }

    private static Type EmitDto(ModuleBuilder moduleBuilder, TablePlan plan, string[] jsonNames)
    {
        var jsonNameCtor = typeof(JsonPropertyNameAttribute).GetConstructor(new[] { typeof(string) });
        var typeBuilder = moduleBuilder.DefineType(
            "Dto_" + plan.Name, TypeAttributes.Public | TypeAttributes.Class);

        for (int i = 0; i < plan.RecordProps.Length; i++)
        {
            var prop = plan.RecordProps[i];
            var field = typeBuilder.DefineField(prop.Name, prop.PropertyType, FieldAttributes.Public);
            field.SetCustomAttribute(new CustomAttributeBuilder(
                jsonNameCtor, new object[] { jsonNames == null ? prop.Name : jsonNames[i] }));
        }

        return typeBuilder.CreateType();
    }

    private static ColumnPlan BuildColumn(FieldInfo field)
    {
        Type t = field.FieldType;
        ColKind kind =
            t == typeof(int) ? ColKind.I32 :
            t.IsEnum ? ColKind.I32 :
            t == typeof(long) ? ColKind.I64 :
            t == typeof(float) ? ColKind.F32 :
            t == typeof(bool) ? ColKind.Bool :
            t == typeof(string) ? ColKind.Str :
            t == typeof(int[]) ? ColKind.I32Array :
            t == typeof(float[]) ? ColKind.F32Array :
            throw new NotSupportedException($"{field.DeclaringType.Name}.{field.Name}: the benchmark has no plan for {t}.");

        Type valueType = kind switch
        {
            ColKind.I32 => typeof(int),
            ColKind.I64 => typeof(long),
            ColKind.F32 => typeof(float),
            ColKind.Bool => typeof(bool),
            ColKind.Str => typeof(string),
            ColKind.I32Array => typeof(int[]),
            _ => typeof(float[]),
        };

        // (object dto, T value) => ((Dto)dto).Field = (FieldType)value
        var dto = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(valueType);
        var assign = Expression.Assign(
            Expression.Field(Expression.Convert(dto, field.DeclaringType), field),
            valueType == field.FieldType ? value : Expression.Convert(value, field.FieldType));
        var setter = Expression
            .Lambda(typeof(Action<,>).MakeGenericType(typeof(object), valueType), assign, dto, value)
            .Compile();

        return new ColumnPlan { Field = field, Kind = kind, Setter = setter };
    }

    private static IndexPlan[] BuildIndexPlans(TablePlan plan)
    {
        var indexes = new List<IndexPlan>();
        foreach (var dictProp in plan.TableType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!dictProp.Name.StartsWith("RecordsBy", StringComparison.Ordinal))
                continue;

            string keyName = dictProp.Name.Substring("RecordsBy".Length);
            var dtoField = plan.DtoType
                .GetFields()
                .Single(f => string.Equals(f.Name, keyName, StringComparison.OrdinalIgnoreCase));

            var dto = Expression.Parameter(plan.DtoType);
            var getKey = Expression
                .Lambda(typeof(Func<,>).MakeGenericType(plan.DtoType, dtoField.FieldType),
                    Expression.Field(dto, dtoField), dto)
                .Compile();

            indexes.Add(new IndexPlan
            {
                Name = dictProp.Name,
                Build = typeof(JsonPath)
                    .GetMethod(nameof(BuildIndex), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(plan.DtoType, dtoField.FieldType),
                GetKey = getKey,
            });
        }

        return indexes.ToArray();
    }

    private static object BuildIndex<TDto, TKey>(IList rows, Func<TDto, TKey> getKey)
    {
        var index = new Dictionary<TKey, TDto>(rows.Count);
        foreach (TDto row in rows)
            index[getKey(row)] = row;

        return index;
    }

    /// <summary>
    /// Loads every table from one of the JSON exports, with the same shape of work the
    /// generated binary reader does: one task per file, rows materialized as typed
    /// objects, one dictionary per declared index.
    /// </summary>
    internal static async Task<LoadedTable[]> LoadAllAsync(TablePlan[] plans, string dir, bool compact)
    {
        // One task per file with the parse on the continuation, which is exactly how the
        // generated ReadAllAsync fans out - neither side gets extra threads.
        var tasks = plans
            .Select(plan => LoadOneAsync(plan, dir, compact))
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private static async Task<LoadedTable> LoadOneAsync(TablePlan plan, string dir, bool compact)
    {
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(dir, plan.Name + ".json"));

        IList rows = compact
            ? ParseCompact(plan, bytes)
            : (IList)JsonSerializer.Deserialize(bytes, plan.ListType, Options);

        var indexes = new object[plan.Indexes.Length];
        for (int i = 0; i < indexes.Length; i++)
            indexes[i] = plan.Indexes[i].Build.Invoke(null, new object[] { rows, plan.Indexes[i].GetKey });

        return new LoadedTable { Plan = plan, Rows = rows, Indexes = indexes };
    }

    /// <summary>
    /// Reads the compact export: an array of arrays, values in field order. This is the
    /// positional reader a team shipping compact JSON has to write - nothing in the file
    /// says which value is which, so the code carries the order.
    /// </summary>
    private static IList ParseCompact(TablePlan plan, byte[] bytes)
    {
        var rows = (IList)Activator.CreateInstance(plan.ListType);
        var columns = plan.Columns;
        var scratchInts = new List<int>();
        var scratchFloats = new List<float>();

        var reader = new Utf8JsonReader(bytes);
        reader.Read();                       // outer StartArray
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            object dto = plan.CreateDto();
            foreach (var column in columns)
            {
                reader.Read();
                switch (column.Kind)
                {
                    case ColKind.I32:
                        ((Action<object, int>)column.Setter)(dto, reader.GetInt32());
                        break;

                    case ColKind.I64:
                        ((Action<object, long>)column.Setter)(dto, ReadLongFromString(ref reader));
                        break;

                    case ColKind.F32:
                        ((Action<object, float>)column.Setter)(dto, reader.GetSingle());
                        break;

                    case ColKind.Bool:
                        ((Action<object, bool>)column.Setter)(dto, reader.GetBoolean());
                        break;

                    case ColKind.Str:
                        ((Action<object, string>)column.Setter)(dto, reader.GetString());
                        break;

                    case ColKind.I32Array:
                        scratchInts.Clear();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            scratchInts.Add(reader.GetInt32());
                        ((Action<object, int[]>)column.Setter)(dto, scratchInts.ToArray());
                        break;

                    case ColKind.F32Array:
                        scratchFloats.Clear();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            scratchFloats.Add(reader.GetSingle());
                        ((Action<object, float[]>)column.Setter)(dto, scratchFloats.ToArray());
                        break;
                }
            }

            reader.Read();                   // row EndArray
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new InvalidOperationException(
                    $"{plan.Name}: a row carries more values than the record has - the positional plan is stale.");

            rows.Add(dto);
        }

        return rows;
    }

    private static long ReadLongFromString(ref Utf8JsonReader reader)
    {
        // The exporter writes longs as strings (doc/exports.md, "JSON의 64비트 정수").
        // Digits never need unescaping, so the raw UTF-8 span parses directly.
        ReadOnlySpan<byte> span = reader.HasValueSequence
            ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence)
            : reader.ValueSpan;

        if (!Utf8Parser.TryParse(span, out long value, out int consumed) || consumed != span.Length)
            throw new FormatException($"Not a 64-bit integer: {reader.GetString()}");

        return value;
    }
}
