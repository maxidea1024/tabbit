using MySqlConnector;

namespace Tabbit.Tests;

/// <summary>
/// The database the history tests share.
///
/// A database of its own rather than the one the exporter tests load tables into: those
/// drop and recreate whatever they are pointed at, and a history that vanished halfway
/// through a test would fail in a way that has nothing to do with the history.
///
/// Tests are told apart by project key rather than by dropping the database between
/// them, so they can run in any order and the migration only happens once.
/// </summary>
internal static class HistoryTestBed
{
    public const string Database = "tabbit_history_test";

    /// <summary>Creates the database if it is not there, and returns how to reach it.</summary>
    public static string EnsureDatabase()
    {
        DatabaseFixture.EnsureRunning();

        using (var connection = new MySqlConnection(Server))
        {
            connection.Open();

            using var command = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{Database}` DEFAULT CHARACTER SET utf8mb4", connection);

            command.ExecuteNonQuery();
        }

        return ConnectionString;
    }

    public static string ConnectionString => DatabaseFixture.MySqlConnectionString
        .Replace("Database=tabbit_test", "Database=" + Database);

    private static string Server => DatabaseFixture.MySqlConnectionString
        .Replace("Database=tabbit_test", "Database=mysql");
}
