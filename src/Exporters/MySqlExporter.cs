using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MySqlConnector;
using Tabbit.Models;
using Tabbit.Recipe;
using Serilog;

using ValueType = Tabbit.Models.ValueType;
using Tabbit.Targets;
using Tabbit.Messages;

namespace Tabbit.Exporters;

/// <summary>
/// MySQL target. One table per table, recreated on each run.
/// </summary>
public class MySqlRecipe : DatabaseRecipe
{
}

/// <summary>
/// Loads the cooked tables into MySQL.
///
/// Each table is written to a shadow table and only then renamed over the live one.
/// MySQL cannot roll back DDL, so a straightforward drop-and-recreate would leave
/// the database with no table at all if the load failed halfway; a multi-pair
/// `RENAME TABLE` is atomic and gives readers the same guarantee.
/// </summary>
[TabbitTarget("mysql", TargetKind.Export, Order = 30)]
public class MySqlExporter : DatabaseExporterBase<MySqlRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    protected override string TargetName => "MySQL";

    private const int InsertBatchRows = 500;


    protected override void ExportTo(DatabaseRecipe recipe, Model model)
    {
        string connectionString = ConnectionString.Resolve(recipe.ConnectionString, RecipeSection);

        Log.Debug($"Connecting to MySQL `{ConnectionString.Redact(connectionString)}`");

        using var connection = new MySqlConnection(connectionString);
        connection.Open();

        var loaded = new List<string>();

        try
        {
            foreach (var table in model.Tables)
            {
                string name = StorageName(recipe, table);

                CreateShadowTable(connection, name, table);
                InsertRows(connection, name + ShadowSuffix, table);

                loaded.Add(name);
            }

            foreach (var name in loaded)
                SwapIn(connection, name);
        }
        catch
        {
            DropShadowTables(connection, model, recipe);
            throw;
        }
    }

    private void CreateShadowTable(MySqlConnection connection, string name, Table table)
    {
        string shadow = name + ShadowSuffix;

        Execute(connection, $"DROP TABLE IF EXISTS {Quote(shadow)}");

        var definitions = Columns(table)
            .Select(sf => $"  {Quote(ColumnName(sf))} {ColumnType(sf)}")
            .ToList();

        // The primary index is the table's key everywhere else, so it is the
        // primary key here too.
        var indexColumn = Columns(table).FirstOrDefault(sf => sf.IsIndexer);
        if (indexColumn is not null)
            definitions.Add($"  PRIMARY KEY ({Quote(ColumnName(indexColumn))})");

        var sql = new StringBuilder();
        sql.Append($"CREATE TABLE {Quote(shadow)} (\n");
        sql.Append(string.Join(",\n", definitions));
        sql.Append("\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

        Execute(connection, sql.ToString());
    }

    private void InsertRows(MySqlConnection connection, string shadow, Table table)
    {
        if (table.Data.Count == 0)
            return;

        var columns = Columns(table);
        string columnList = string.Join(", ", columns.Select(sf => Quote(ColumnName(sf))));

        // Batched multi-row inserts in one transaction: a statement per row makes
        // round trips dominate on a table of any size.
        using var transaction = connection.BeginTransaction();

        for (int offset = 0; offset < table.Data.Count; offset += InsertBatchRows)
        {
            var batch = table.Data.Skip(offset).Take(InsertBatchRows).ToList();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            var sql = new StringBuilder();
            sql.Append($"INSERT INTO {Quote(shadow)} ({columnList}) VALUES ");

            for (int r = 0; r < batch.Count; r++)
            {
                if (r > 0) sql.Append(", ");
                sql.Append('(');

                for (int c = 0; c < columns.Count; c++)
                {
                    if (c > 0) sql.Append(", ");

                    string parameter = $"@p{r}_{c}";
                    sql.Append(parameter);
                    command.Parameters.AddWithValue(parameter, CellValue(batch[r], columns[c]));
                }

                sql.Append(')');
            }

            command.CommandText = sql.ToString();
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Puts the freshly loaded table in place of the live one.
    ///
    /// One multi-pair RENAME, because it is atomic: readers see either the old
    /// table or the new one, never a missing one.
    /// </summary>
    private void SwapIn(MySqlConnection connection, string name)
    {
        string shadow = name + ShadowSuffix;
        string retired = name + "__tabbit_old";

        Execute(connection, $"DROP TABLE IF EXISTS {Quote(retired)}");

        if (TableExists(connection, name))
        {
            Execute(connection,
                $"RENAME TABLE {Quote(name)} TO {Quote(retired)}, {Quote(shadow)} TO {Quote(name)}");
            Execute(connection, $"DROP TABLE IF EXISTS {Quote(retired)}");
        }
        else
        {
            Execute(connection, $"RENAME TABLE {Quote(shadow)} TO {Quote(name)}");
        }

        Log.Debug($"Swapped MySQL table `{name}` into place");
    }

    private void DropShadowTables(MySqlConnection connection, Model model,
                                  DatabaseRecipe recipe)
    {
        // Best effort. The load already failed and that exception is the one worth
        // reporting, so a cleanup problem must not replace it.
        try
        {
            foreach (var table in model.Tables)
                Execute(connection, $"DROP TABLE IF EXISTS {Quote(StorageName(recipe, table) + ShadowSuffix)}");
        }
        catch (Exception ex)
        {
            Log.Warning(Message.Of(ExportMessages.LogMysqlCleanupFailed,
                ("Detail", ex.Message)).In(MessageCatalog.Current));
        }
    }

    private bool TableExists(MySqlConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name = @name";
        command.Parameters.AddWithValue("@name", name);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Quotes an identifier with backticks.
    ///
    /// Names come from the workbook. The cooker has already required them to be
    /// valid identifiers, but quoting also covers the ones that collide with a
    /// reserved word.
    /// </summary>
    private static string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";

    private static string ColumnType(SerialField sf)
    {
        // Arrays of either kind become JSON: a relational column cannot hold a
        // varying number of values, and JSON stays queryable where text would not.
        if (IsJsonColumn(sf))
            return "JSON NOT NULL";

        switch (ElementTypeOf(sf))
        {
            case ValueType.String: return "TEXT NOT NULL";
            case ValueType.Bool: return "TINYINT(1) NOT NULL";
            case ValueType.Int32: return "INT NOT NULL";
            case ValueType.Int64: return "BIGINT NOT NULL";
            case ValueType.Float: return "FLOAT NOT NULL";
            case ValueType.Double: return "DOUBLE NOT NULL";
            case ValueType.DateTime: return "DATETIME(6) NOT NULL";

            // Ticks, so the exact value survives: MySQL's TIME cannot span the
            // range a .NET TimeSpan can.
            case ValueType.TimeSpan: return "BIGINT NOT NULL";

            case ValueType.Uuid: return "CHAR(36) NOT NULL";

            // Stored as the integer it resolves to.
            case ValueType.Enum: return "INT NOT NULL";

            default:
                throw new TabbitException(null,
                    Message.Of(ExportMessages.MySqlTypeUnmapped,
                        ("Type", sf.Type), ("Column", ColumnName(sf))));
        }
    }
}
