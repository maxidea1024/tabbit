using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The database exporters, run against real servers.
///
/// Real engines rather than mocks, because what these exporters get right or wrong
/// is engine behaviour: MySQL's atomic multi-pair RENAME, PostgreSQL's transactional
/// DDL, Mongo's renameCollection, Redis MULTI/EXEC. A mock would confirm the code
/// makes the calls it makes, which is not the question.
///
/// Each test queries the server itself instead of believing the exporter's log, and
/// the whole set runs twice over to check the second run replaces rather than
/// duplicates - the case a single run cannot reveal.
/// </summary>
[Collection("databases")]
public class DatabaseExportTests
{
    private const string Scenario = "databases";

    private static void RunExport()
    {
        DatabaseFixture.EnsureRunning();

        var result = TabbitRunner.Convert(Scenario, DatabaseFixture.ConverterEnvironment);

        Assert.True(result.Succeeded,
            $"Database export failed.{Environment.NewLine}{result.Describe()}");
    }

    // ------------------------------------------------------------- MySQL

    [Fact]
    public void MySql_receives_every_table_with_correct_values()
    {
        RunExport();

        using var connection = new MySqlConnection(DatabaseFixture.MySqlConnectionString);
        connection.Open();

        // The shadow table must be gone: it is an implementation detail of the swap
        // and leaving it behind would be visible to anyone browsing the schema.
        Assert.False(MySqlTableExists(connection, "tb_Item__tabbit_new"),
            "A shadow table was left behind after the swap.");
        Assert.True(MySqlTableExists(connection, "tb_Item"));

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, category_id, price FROM `tb_Item` ORDER BY `index`";

        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
            Assert.True(reader.GetInt32(1) > 0);   // resolved reference index
            Assert.True(reader.GetInt32(2) > 0);   // price
        }

        Assert.Equal(new[] { "Short Sword", "Leather Armor", "Small Potion" }, names);
    }

    /// <summary>
    /// Arrays cannot live in a scalar column, so both array kinds become JSON.
    /// </summary>
    [Fact]
    public void MySql_stores_arrays_as_json()
    {
        RunExport();

        using var connection = new MySqlConnection(DatabaseFixture.MySqlConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tags, slot_array FROM `tb_ArrayTypes` WHERE `index` = 1";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        var tags = JsonArrayAsText(reader.GetString(0));
        Assert.Equal(new[] { "red", "green", "blue" }, tags);

        // A serial field lands in JSON too, so both array kinds read back the same way.
        Assert.Equal(new[] { "1", "2" }, JsonArrayAsText(reader.GetString(1)));
    }

    /// <summary>
    /// The primary index becomes the primary key, which is what makes a lookup by
    /// index cheap on the database side as well.
    /// </summary>
    [Fact]
    public void MySql_makes_the_index_field_the_primary_key()
    {
        RunExport();

        using var connection = new MySqlConnection(DatabaseFixture.MySqlConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.key_column_usage " +
            "WHERE table_schema = DATABASE() AND table_name = 'tb_Item' AND constraint_name = 'PRIMARY'";

        Assert.Equal("index", (string)command.ExecuteScalar());
    }

    private static bool MySqlTableExists(MySqlConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name = @name";
        command.Parameters.AddWithValue("@name", name);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    // -------------------------------------------------------- PostgreSQL

    [Fact]
    public void PostgreSql_receives_every_table_with_correct_types()
    {
        RunExport();

        using var connection = new NpgsqlConnection(DatabaseFixture.PostgreSqlConnectionString);
        connection.Open();

        using var command = new NpgsqlCommand(
            "SELECT string_field, big_int_field, uuid_field, datetime_field " +
            "FROM tabbit.\"tb_TestFieldTypes\" WHERE \"index\" = 1", connection);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        Assert.Equal("first", reader.GetString(0));

        // A17: the writer used to truncate 64 bit values to 32 bits. Stored as a
        // real bigint here, so the full value has to survive.
        Assert.Equal(9007199254740993L, reader.GetInt64(1));

        Assert.Equal(Guid.Parse("7b7d9f6a-1e2c-4c1a-9a5f-2b6d0c3e4f51"), reader.GetGuid(2));
        Assert.Equal(new DateTime(2022, 1, 24, 10, 30, 0), reader.GetDateTime(3));
    }

    /// <summary>
    /// PostgreSQL rolls back DDL, so the exporter does the whole load and swap in one
    /// transaction. Nothing else in the suite covers a target with that property.
    /// </summary>
    [Fact]
    public void PostgreSql_leaves_no_shadow_table_behind()
    {
        RunExport();

        using var connection = new NpgsqlConnection(DatabaseFixture.PostgreSqlConnectionString);
        connection.Open();

        using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = 'tabbit' AND table_name LIKE '%__tabbit_new'", connection);

        Assert.Equal(0L, (long)command.ExecuteScalar());
    }

    [Fact]
    public void PostgreSql_stores_arrays_as_jsonb()
    {
        RunExport();

        using var connection = new NpgsqlConnection(DatabaseFixture.PostgreSqlConnectionString);
        connection.Open();

        // Queried through jsonb operators rather than as text: if the column were
        // plain text this would fail, so it also proves the column type.
        using var command = new NpgsqlCommand(
            "SELECT jsonb_array_length(tags) FROM tabbit.\"tb_ArrayTypes\" WHERE \"index\" = 1", connection);

        Assert.Equal(3, (int)command.ExecuteScalar());
    }

    // ----------------------------------------------------------- MongoDB

    [Fact]
    public void Mongo_receives_documents_keyed_by_the_index_field()
    {
        RunExport();

        var database = new MongoClient(DatabaseFixture.MongoConnectionString)
            .GetDatabase("tabbit_test");

        var items = database.GetCollection<BsonDocument>("tb_Item");

        // The primary index doubles as _id, so a lookup uses the identity index
        // Mongo maintains anyway instead of a second one.
        var second = items.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstOrDefault();

        Assert.NotNull(second);
        Assert.Equal("Leather Armor", second["name"].AsString);
    }

    [Fact]
    public void Mongo_stores_arrays_as_bson_arrays()
    {
        RunExport();

        var database = new MongoClient(DatabaseFixture.MongoConnectionString)
            .GetDatabase("tabbit_test");

        var row = database.GetCollection<BsonDocument>("tb_ArrayTypes")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();

        Assert.Equal(new[] { "red", "green", "blue" },
            row["tags"].AsBsonArray.Select(v => v.AsString).ToArray());

        // An empty cell is an empty array, and that has to survive to the database
        // rather than becoming null or a missing field.
        var empty = database.GetCollection<BsonDocument>("tb_ArrayTypes")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", 3)).First();

        Assert.Empty(empty["tags"].AsBsonArray);
    }

    [Fact]
    public void Mongo_leaves_no_shadow_collection_behind()
    {
        RunExport();

        var database = new MongoClient(DatabaseFixture.MongoConnectionString)
            .GetDatabase("tabbit_test");

        var names = database.ListCollectionNames().ToList();

        Assert.DoesNotContain(names, n => n.EndsWith("__tabbit_new"));
        Assert.Contains("tb_Item", names);
    }

    // -------------------------------------------------------------- Redis

    [Fact]
    public void Redis_receives_one_hash_per_row_plus_an_index_set()
    {
        RunExport();

        using var connection = ConnectionMultiplexer.Connect(DatabaseFixture.RedisConnectionString);
        var database = connection.GetDatabase(0);

        Assert.Equal("Leather Armor", (string)database.HashGet("tb_Item:2", "name"));

        // The index set is what makes "every row of this table" answerable without
        // a key scan.
        var indexed = database.SetMembers("tb_Item:index").Select(v => (string)v).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "1", "2", "3" }, indexed);
    }

    [Fact]
    public void Redis_leaves_no_shadow_keys_behind()
    {
        RunExport();

        using var connection = ConnectionMultiplexer.Connect(
            new ConfigurationOptions
            {
                EndPoints = { DatabaseFixture.RedisConnectionString },
                AllowAdmin = true,
            });

        var server = connection.GetServer(connection.GetEndPoints().First());

        Assert.Empty(server.Keys(0, pattern: "*__tabbit_new*").ToList());
    }

    // ------------------------------------------------------- idempotence

    /// <summary>
    /// Running twice must replace, not accumulate.
    ///
    /// This is the case a single run cannot show, and the one that would bite in a
    /// build pipeline where the exporter runs on every commit. Rows removed from the
    /// sheet have to disappear from the store too.
    /// </summary>
    [Fact]
    public void Running_twice_replaces_rather_than_duplicates()
    {
        RunExport();
        RunExport();

        using var mysql = new MySqlConnection(DatabaseFixture.MySqlConnectionString);
        mysql.Open();
        using var command = mysql.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `tb_Item`";
        Assert.Equal(3L, Convert.ToInt64(command.ExecuteScalar()));

        var mongo = new MongoClient(DatabaseFixture.MongoConnectionString).GetDatabase("tabbit_test");
        Assert.Equal(3L, mongo.GetCollection<BsonDocument>("tb_Item").CountDocuments(FilterDefinition<BsonDocument>.Empty));

        using var redis = ConnectionMultiplexer.Connect(DatabaseFixture.RedisConnectionString);
        Assert.Equal(3L, redis.GetDatabase(0).SetLength("tb_Item:index"));
    }

    /// <summary>
    /// An unset environment variable is an error rather than an empty substitution,
    /// because connecting with a blank password fails later and less clearly.
    /// </summary>
    [Fact]
    public void Missing_connection_secret_is_reported_clearly()
    {
        DatabaseFixture.EnsureRunning();

        // Deliberately without the password in the environment.
        var result = TabbitRunner.Convert(Scenario);

        Assert.False(result.Succeeded, "Export succeeded without the connection secret.");
        Assert.Contains(DatabaseFixture.PasswordVariable, result.StdOut);
        Assert.Contains("not set", result.StdOut);
    }

    /// <summary>
    /// Elements of a JSON array as text, for reading back MySQL's JSON columns.
    ///
    /// Rendered as text rather than typed, so one helper serves the string array and
    /// the integer one.
    /// </summary>
    private static string[] JsonArrayAsText(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement
            .EnumerateArray()
            .Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String
                ? e.GetString()
                : e.GetRawText())
            .ToArray();
    }
}

[CollectionDefinition("databases", DisableParallelization = true)]
public class DatabaseCollection
{
}
