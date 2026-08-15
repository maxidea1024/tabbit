using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Tabbit.Models;
using Tabbit.Recipe;
using Serilog;

using ValueType = Tabbit.Models.ValueType;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// PostgreSQL target. One table per table, recreated on each run.
/// </summary>
public class PostgreSqlRecipe : DatabaseRecipe
{
    /// <summary>Schema the tables are created in.</summary>
    public string Schema { get; set; } = "public";
}

/// <summary>
/// Loads the cooked tables into PostgreSQL.
///
/// PostgreSQL rolls back DDL, so the whole export - creating every shadow table,
/// filling it, and swapping it over the live one - runs inside a single
/// transaction. Either the database ends up with every table updated or with none
/// of them touched, which is a stronger guarantee than the other targets can offer.
/// </summary>
[TabbitTarget("postgresql", TargetKind.Export, Order = 40)]
public class PostgreSqlExporter : DatabaseExporterBase<PostgreSqlRecipe>
{
    protected override string TargetName => "PostgreSQL";

    private string _schema = "public";


    protected override void ExportTo(DatabaseRecipe recipe, Model model)
    {
        var postgres = (PostgreSqlRecipe)recipe;
        _schema = string.IsNullOrWhiteSpace(postgres.Schema) ? "public" : postgres.Schema;

        string connectionString = ConnectionString.Resolve(recipe.ConnectionString, RecipeSection);

        Log.Debug($"Connecting to PostgreSQL `{ConnectionString.Redact(connectionString)}`");

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        // One transaction for the entire export, DDL included. Nothing needs
        // cleaning up on failure because nothing was ever visible.
        using var transaction = connection.BeginTransaction();

        Execute(connection, transaction, $"CREATE SCHEMA IF NOT EXISTS {Quote(_schema)}");

        foreach (var table in model.Tables)
        {
            string name = StorageName(recipe, table);

            CreateShadowTable(connection, transaction, name, table);
            CopyRows(connection, transaction, name + ShadowSuffix, table);
        }

        foreach (var table in model.Tables)
            SwapIn(connection, transaction, StorageName(recipe, table));

        transaction.Commit();
    }

    private void CreateShadowTable(NpgsqlConnection connection, NpgsqlTransaction transaction,
                                   string name, Table table)
    {
        string shadow = name + ShadowSuffix;

        Execute(connection, transaction, $"DROP TABLE IF EXISTS {Qualified(shadow)}");

        var definitions = Columns(table)
            .Select(sf => $"  {Quote(ColumnName(sf))} {ColumnType(sf)}")
            .ToList();

        var indexColumn = Columns(table).FirstOrDefault(sf => sf.IsIndexer);
        if (indexColumn is not null)
            definitions.Add($"  PRIMARY KEY ({Quote(ColumnName(indexColumn))})");

        var sql = new StringBuilder();
        sql.Append($"CREATE TABLE {Qualified(shadow)} (\n");
        sql.Append(string.Join(",\n", definitions));
        sql.Append("\n)");

        Execute(connection, transaction, sql.ToString());
    }

    /// <summary>
    /// Fills a shadow table using COPY, PostgreSQL's bulk load path.
    ///
    /// Faster than INSERT by a wide margin on the row counts a game's data tables
    /// reach, and it is the reason to prefer Npgsql's binary importer over building
    /// statements.
    /// </summary>
    private void CopyRows(NpgsqlConnection connection, NpgsqlTransaction transaction,
                          string shadow, Table table)
    {
        if (table.Data.Count == 0)
            return;

        var columns = Columns(table);
        string columnList = string.Join(", ", columns.Select(sf => Quote(ColumnName(sf))));

        using var writer = connection.BeginBinaryImport(
            $"COPY {Qualified(shadow)} ({columnList}) FROM STDIN (FORMAT BINARY)");

        foreach (var row in table.Data)
        {
            writer.StartRow();

            foreach (var sf in columns)
                WriteValue(writer, sf, row);
        }

        writer.Complete();
    }

    private void WriteValue(NpgsqlBinaryImporter writer, SerialField sf, List<Cell> row)
    {
        if (IsJsonColumn(sf))
        {
            writer.Write((string)CellValue(row, sf), NpgsqlDbType.Jsonb);
            return;
        }

        object value = CellValue(row, sf);

        switch (ElementTypeOf(sf))
        {
            case ValueType.String: writer.Write((string)value, NpgsqlDbType.Text); break;
            case ValueType.Bool: writer.Write((bool)value, NpgsqlDbType.Boolean); break;
            case ValueType.Int32: writer.Write((int)value, NpgsqlDbType.Integer); break;
            case ValueType.Int64: writer.Write((long)value, NpgsqlDbType.Bigint); break;
            case ValueType.Float: writer.Write((float)value, NpgsqlDbType.Real); break;
            case ValueType.Double: writer.Write((double)value, NpgsqlDbType.Double); break;

            // Timestamp without time zone: the sheet says nothing about an offset,
            // and inventing UTC would misrepresent the data.
            case ValueType.DateTime: writer.Write((DateTime)value, NpgsqlDbType.Timestamp); break;

            // Ticks as bigint. PostgreSQL's interval would round-trip, but only to
            // microsecond resolution, losing the low two digits of a .NET tick.
            case ValueType.TimeSpan: writer.Write((long)value, NpgsqlDbType.Bigint); break;

            case ValueType.Uuid: writer.Write((Guid)value, NpgsqlDbType.Uuid); break;

            case ValueType.Enum:
            case ValueType.ForeignRecord:
                writer.Write((int)value, NpgsqlDbType.Integer);
                break;

            default:
                throw new TabbitException(
                    $"PostgreSQL exporter cannot map type `{sf.Type}` of column `{ColumnName(sf)}`.");
        }
    }

    /// <summary>
    /// Replaces the live table with the shadow one. Transactional here, so a
    /// reader never observes the intermediate state.
    /// </summary>
    private void SwapIn(NpgsqlConnection connection, NpgsqlTransaction transaction, string name)
    {
        Execute(connection, transaction, $"DROP TABLE IF EXISTS {Qualified(name)} CASCADE");
        Execute(connection, transaction,
            $"ALTER TABLE {Qualified(name + ShadowSuffix)} RENAME TO {Quote(name)}");

        Log.Debug($"Swapped PostgreSQL table `{name}` into place");
    }

    private static void Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private string Qualified(string name) => $"{Quote(_schema)}.{Quote(name)}";

    /// <summary>
    /// Quotes an identifier with double quotes, which also keeps PostgreSQL from
    /// folding it to lower case.
    /// </summary>
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string ColumnType(SerialField sf)
    {
        // jsonb rather than json: it is indexable and normalizes on the way in.
        if (IsJsonColumn(sf))
            return "jsonb NOT NULL";

        switch (ElementTypeOf(sf))
        {
            case ValueType.String: return "text NOT NULL";
            case ValueType.Bool: return "boolean NOT NULL";
            case ValueType.Int32: return "integer NOT NULL";
            case ValueType.Int64: return "bigint NOT NULL";
            case ValueType.Float: return "real NOT NULL";
            case ValueType.Double: return "double precision NOT NULL";
            case ValueType.DateTime: return "timestamp NOT NULL";
            case ValueType.TimeSpan: return "bigint NOT NULL";
            case ValueType.Uuid: return "uuid NOT NULL";
            case ValueType.Enum: return "integer NOT NULL";
            case ValueType.ForeignRecord: return "integer NOT NULL";

            default:
                throw new TabbitException(
                    $"PostgreSQL exporter cannot map type `{sf.Type}` of column `{ColumnName(sf)}`.");
        }
    }
}
