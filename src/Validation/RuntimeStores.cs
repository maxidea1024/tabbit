using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using MySqlConnector;
using Npgsql;
using Serilog;
using StackExchange.Redis;
using Tabbit.Exporters;

namespace Tabbit.Validation;

/// <summary>
/// A read-only query gateway over one external store.
/// </summary>
/// <remarks>
/// Read-only is a convenience rather than a guarantee, and saying so is the honest part: a rule
/// file is arbitrary C# and can open its own connection to anything. What this gives is a rule
/// that reads with two lines instead of twenty, and an account that only needs SELECT.
/// </remarks>
public sealed class SqlStore : ISqlStore
{
    private readonly string _name;
    private readonly string _connectionString;
    private readonly bool _postgres;
    private readonly int _timeoutSeconds;

    internal SqlStore(string name, string connectionString, bool postgres, int timeoutSeconds)
    {
        _name = name;
        _connectionString = connectionString;
        _postgres = postgres;
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>Every value of the query's first column.</summary>
    public List<T> Column<T>(string sql)
    {
        var values = new List<T>();

        Read(sql, reader =>
        {
            while (reader.Read())
                values.Add(reader.IsDBNull(0) ? default! : (T)Convert.ChangeType(reader.GetValue(0), typeof(T)));
        });

        return values;
    }

    /// <summary>
    /// The same as a set, which is what a rule usually wants.
    /// </summary>
    /// <remarks>
    /// Because the question is almost always membership - is this id one the live table
    /// knows - and asking that of a list is a scan per row of the sheet.
    /// </remarks>
    public HashSet<T> Set<T>(string sql) => new HashSet<T>(Column<T>(sql));

    /// <summary>The first column of the first row, or the type's empty value.</summary>
    public T? Scalar <T>(string sql)
    {
        var values = Column<T>(sql);

        return values.Count > 0 ? values[0] : default;
    }

    /// <summary>Every row as a dictionary of column name to value.</summary>
    public List<Dictionary<string, object?>> Rows(string sql)
    {
        var rows = new List<Dictionary<string, object?>>();

        Read(sql, reader =>
        {
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                for (int at = 0; at < reader.FieldCount; at++)
                    row[reader.GetName(at)] = reader.IsDBNull(at) ? null : reader.GetValue(at);

                rows.Add(row);
            }
        });

        return rows;
    }

    private void Read(string sql, Action<DbDataReader> read)
    {
        RefuseAnythingButAQuery(sql);

        try
        {
            using DbConnection connection = _postgres
                ? new NpgsqlConnection(_connectionString)
                : new MySqlConnection(_connectionString);

            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = sql;
            command.CommandTimeout = _timeoutSeconds;

            using var reader = command.ExecuteReader();

            read(reader);
        }
        catch (Exception failure) when (failure is not TabbitException && failure is not TabbitDefectException)
        {
            // A store that cannot answer is not a store that agreed. Reported as the rule's
            // failure so the run stops, with the connection named and its secret masked.
            throw new TabbitException(
                $"Validation could not query `{_name}`: {failure.Message} "
                + $"({ConnectionString.Redact(_connectionString)})");
        }
    }

    /// <summary>
    /// Refuses a statement that is not a read.
    /// </summary>
    /// <remarks>
    /// A guard, not a sandbox - `SELECT` can call a function that writes, and a rule can bypass
    /// this entirely by opening its own connection. What it does catch is the accident: a rule
    /// pasted from somewhere that ends in a DELETE, run against production by a validation
    /// nobody expected to write anything. The account should be read-only too; this is the
    /// second lock, not the first.
    /// </remarks>
    private static void RefuseAnythingButAQuery(string sql)
    {
        string head = (sql ?? "").TrimStart().Split(' ', '\t', '\n', '\r').FirstOrDefault() ?? "";

        if (head.Equals("select", StringComparison.OrdinalIgnoreCase)
            || head.Equals("with", StringComparison.OrdinalIgnoreCase)
            || head.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new TabbitException(
            $"Validation may only read: `{head}` is not a query. Use SELECT, WITH or SHOW.");
    }
}

/// <summary>A read-only view of one Redis database.</summary>
public sealed class RedisStore : IRedisStore
{
    private readonly string _name;
    private readonly string _configuration;
    private readonly int _database;

    internal RedisStore(string name, string configuration, int database)
    {
        _name = name;
        _configuration = configuration;
        _database = database;
    }

    /// <summary>Whether a key is there.</summary>
    public bool Exists(string key) => With(db => db.KeyExists(key));

    /// <summary>A string value, or null.</summary>
    public string? Get(string key) => With(db => (string?)db.StringGet(key));

    /// <summary>One field of a hash, or null.</summary>
    public string? Field(string key, string field) => With(db => (string?)db.HashGet(key, field));

    /// <summary>Every member of a set.</summary>
    public HashSet<string> Members(string key)
        => With(db => new HashSet<string>(db.SetMembers(key).Select(member => (string)member!)));

    private T With<T>(Func<IDatabase, T> read)
    {
        try
        {
            using var connection = ConnectionMultiplexer.Connect(_configuration);

            return read(connection.GetDatabase(_database));
        }
        catch (Exception failure) when (failure is not TabbitException && failure is not TabbitDefectException)
        {
            throw new TabbitException(
                $"Validation could not read `{_name}`: {failure.Message} "
                + $"({ConnectionString.Redact(_configuration)})");
        }
    }
}

/// <summary>
/// The connections a `runtime` rule opens by name.
/// </summary>
/// <remarks>
/// The recipe holds one string per name, and its scheme says which store it is - `mysql:`,
/// `postgres:` or `redis:`. Explicit rather than sniffed: an ADO connection string and a Redis
/// configuration string are not distinguishable by shape, and guessing wrong means a rule fails
/// with a message about syntax rather than about the recipe.
/// </remarks>
internal sealed class RuntimeStores
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Validating;

    /// <summary>
    /// How long a query may take.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A validation waiting on a store that is not answering holds up every
    /// build behind it, and a store that is not answering has already failed the check.
    /// </remarks>
    private const int TimeoutSeconds = 15;

    private readonly IReadOnlyDictionary<string, string> _connections;
    private readonly Dictionary<string, SqlStore> _sql = new Dictionary<string, SqlStore>();
    private readonly Dictionary<string, RedisStore> _redis = new Dictionary<string, RedisStore>();

    internal RuntimeStores(IReadOnlyDictionary<string, string> connections)
        => _connections = connections ?? new Dictionary<string, string>();

    /// <summary>Opens a SQL store by the name the recipe gave it.</summary>
    internal SqlStore Sql(string name)
    {
        lock (_sql)
        {
            if (_sql.TryGetValue(name, out var found))
                return found;

            var (scheme, rest) = Split(name);

            bool postgres = scheme is "postgres" or "postgresql";

            if (!postgres && scheme != "mysql")
            {
                throw new TabbitException(
                    $"`Validation.Connections.{name}` is a `{scheme}:` connection, which `Db()` "
                    + $"does not open. Use `mysql:` or `postgres:` - or `Redis(\"{name}\")` if it "
                    + $"is a cache.");
            }

            var store = new SqlStore(name, rest, postgres, TimeoutSeconds);

            _sql.Add(name, store);

            return store;
        }
    }

    /// <summary>Opens a Redis store by the name the recipe gave it.</summary>
    internal RedisStore Redis(string name)
    {
        lock (_redis)
        {
            if (_redis.TryGetValue(name, out var found))
                return found;

            var (scheme, rest) = Split(name);

            if (scheme != "redis")
            {
                throw new TabbitException(
                    $"`Validation.Connections.{name}` is a `{scheme}:` connection, which "
                    + $"`Redis()` does not open. Use `redis://host:port/db`, or `Db(\"{name}\")` "
                    + $"if it is a database.");
            }

            var (configuration, database) = SplitRedis(rest);

            var store = new RedisStore(name, configuration, database);

            _redis.Add(name, store);

            return store;
        }
    }

    /// <summary>The scheme and the rest of one named connection, with secrets expanded.</summary>
    private (string Scheme, string Tail) Split(string name)
    {
        if (!_connections.TryGetValue(name, out string? template))
        {
            string known = _connections.Count == 0
                ? "the recipe sets none"
                : string.Join(", ", _connections.Keys.OrderBy(key => key, StringComparer.Ordinal));

            throw new TabbitException(
                $"This rule opens the connection `{name}`, which the recipe does not have. "
                + $"Add it under `Validation.Connections` ({known}).");
        }

        string resolved = ConnectionString.Resolve(template, $"Validation.Connections.{name}");

        int colon = resolved.IndexOf(':');

        if (colon <= 0)
        {
            throw new TabbitException(
                $"`Validation.Connections.{name}` does not say which kind of store it is. Begin "
                + $"it with `mysql:`, `postgres:` or `redis://`.");
        }

        string scheme = resolved.Substring(0, colon).ToLowerInvariant();
        string rest = resolved.Substring(colon + 1).TrimStart('/');

        return (scheme, rest);
    }

    /// <summary>`host:port/db` split into a configuration string and a database number.</summary>
    private static (string Configuration, int Number) SplitRedis(string rest)
    {
        int slash = rest.LastIndexOf('/');

        if (slash < 0)
            return (rest, 0);

        string tail = rest.Substring(slash + 1);

        return int.TryParse(tail, out int database)
            ? (rest.Substring(0, slash), database)
            : (rest, 0);
    }

    /// <summary>Says what a run reached, so a passing validation names what it compared against.</summary>
    internal void LogOpened()
    {
        foreach (string name in _sql.Keys.Concat(_redis.Keys).OrderBy(key => key, StringComparer.Ordinal))
            Log.Debug($"Validation queried the store `{name}`.");
    }
}
