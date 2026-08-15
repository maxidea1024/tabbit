using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Brings the test databases up once for the whole run and reports where they landed.
///
/// Host ports are assigned by Docker rather than fixed in the compose file. Fixed
/// ports are fragile on a shared machine: an offset picked to dodge the defaults
/// still collides with the next project that picked an offset, which is what
/// happened when this was first written. Letting Docker choose and then asking it
/// what it chose makes a collision impossible.
///
/// `docker compose up --wait` blocks until every service reports healthy, which is
/// what makes this reliable - waiting on the port alone would connect to a MySQL
/// still initialising, which then restarts and drops the connection.
///
/// The containers are intentionally left running. Pulling and initialising four
/// engines takes far longer than the tests themselves, so a second run is fast.
/// `docker compose down` in test/fixtures/databases removes them.
/// </summary>
public static class DatabaseFixture
{
    /// <summary>
    /// Password the compose file configures.
    ///
    /// Reaches the converter through the environment, which is how the exporters
    /// expect a secret to arrive; one written into a recipe would be committed.
    /// </summary>
    public const string Password = "tabbit-test";

    public const string PasswordVariable = "TABBIT_TEST_DB_PASSWORD";
    public const string MySqlPortVariable = "TABBIT_TEST_MYSQL_PORT";
    public const string PostgresPortVariable = "TABBIT_TEST_POSTGRES_PORT";
    public const string MongoPortVariable = "TABBIT_TEST_MONGO_PORT";
    public const string RedisPortVariable = "TABBIT_TEST_REDIS_PORT";

    private static readonly object Gate = new object();
    private static bool _started;
    private static string _failure;
    private static Dictionary<string, string> _environment;

    /// <summary>
    /// Ensures the databases are up, or fails the calling test with the reason.
    /// </summary>
    public static void EnsureRunning()
    {
        lock (Gate)
        {
            if (!_started)
            {
                _failure = Start();
                _started = true;
            }
        }

        // A hard failure rather than a skip, as with the TypeScript and C++ gates:
        // a gate that quietly turns itself off is worse than no gate. These are the
        // only tests that show the exporters work against a real engine.
        Assert.True(_failure == null,
            $"Test databases are not available.{Environment.NewLine}{_failure}");
    }

    /// <summary>
    /// Environment the converter subprocess needs to resolve the `${...}`
    /// placeholders in the database recipe: the secret and the assigned ports.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ConverterEnvironment
    {
        get
        {
            EnsureRunning();
            return _environment;
        }
    }

    public static string MySqlConnectionString =>
        $"Server=127.0.0.1;Port={Port(MySqlPortVariable)};Database=tabbit_test;Uid=root;Pwd={Password}";

    public static string PostgreSqlConnectionString =>
        $"Host=127.0.0.1;Port={Port(PostgresPortVariable)};Database=tabbit_test;Username=postgres;Password={Password}";

    public static string MongoConnectionString =>
        $"mongodb://127.0.0.1:{Port(MongoPortVariable)}/tabbit_test";

    public static string RedisConnectionString => $"127.0.0.1:{Port(RedisPortVariable)}";

    private static string Port(string variable)
    {
        EnsureRunning();
        return _environment[variable];
    }

    private static string Start()
    {
        string composeDir = Path.Combine(RepoLayout.Root, "test", "fixtures", "databases");

        var probe = Run(composeDir, TimeSpan.FromMinutes(1), "docker", "version", "--format", "{{.Server.Version}}");
        if (!probe.Succeeded)
            return $"Docker is not available.{Environment.NewLine}{probe.Output}";

        // --wait blocks until every healthcheck passes. The generous timeout covers
        // a first run, which pulls four images.
        var up = Run(composeDir, TimeSpan.FromMinutes(15),
                     "docker", "compose", "up", "--detach", "--wait");

        if (!up.Succeeded)
            return $"`docker compose up --wait` failed.{Environment.NewLine}{up.Output}";

        var environment = new Dictionary<string, string> { [PasswordVariable] = Password };

        foreach (var (service, containerPort, variable) in new[]
                 {
                     ("mysql", 3306, MySqlPortVariable),
                     ("postgres", 5432, PostgresPortVariable),
                     ("mongo", 27017, MongoPortVariable),
                     ("redis", 6379, RedisPortVariable),
                 })
        {
            string port = DiscoverPort(composeDir, service, containerPort, out string error);
            if (port == null)
                return error;

            environment[variable] = port;
        }

        _environment = environment;
        return null;
    }

    /// <summary>
    /// Asks Docker which host port it published a service's container port on.
    /// </summary>
    private static string DiscoverPort(string composeDir, string service, int containerPort, out string error)
    {
        var result = Run(composeDir, TimeSpan.FromMinutes(1),
                         "docker", "compose", "port", service, containerPort.ToString());

        if (!result.Succeeded)
        {
            error = $"Could not determine the host port for `{service}`.{Environment.NewLine}{result.Output}";
            return null;
        }

        // Output is `address:port`, where the address may be 0.0.0.0 or an IPv6 form,
        // so the port is whatever follows the last colon.
        string mapping = result.Output.Trim();
        int colon = mapping.LastIndexOf(':');

        if (colon < 0 || colon == mapping.Length - 1)
        {
            error = $"Unexpected `docker compose port {service}` output: `{mapping}`";
            return null;
        }

        error = null;
        return mapping.Substring(colon + 1);
    }

    private sealed class RunResult
    {
        public bool Succeeded;
        public string Output;
    }

    private static RunResult Run(string workingDirectory, TimeSpan timeout, string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var output = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return new RunResult { Succeeded = false, Output = $"`{fileName}` timed out after {timeout}." };
            }

            process.WaitForExit();

            return new RunResult { Succeeded = process.ExitCode == 0, Output = output.ToString() };
        }
        catch (Exception ex)
        {
            return new RunResult { Succeeded = false, Output = $"Could not start `{fileName}`: {ex.Message}" };
        }
    }
}
