using System;
using System.Text.Json;

namespace Tabbit.Lsp;

/// <summary>
/// `tabbit lsp` - serves one editor over standard input and output.
/// </summary>
/// <remarks>
/// **Nothing but the protocol may be written to standard output.** A log line, the version
/// banner, one stray `Console.WriteLine`, and the client is reading a header where a body
/// should be and the session is over. That is why <see cref="Program"/> branches here before
/// it sets logging up, and why what this says about itself goes to standard error.
/// </remarks>
internal static class LspCommand
{
    public static int Run(string[] arguments)
    {
        ChooseMessageLanguage(arguments);

        // The raw handles, not `Console.In` and `Console.Out`: those are text writers with an
        // encoding and a newline convention of their own, and the protocol counts bytes.
        var connection = new JsonRpcConnection(
            Console.OpenStandardInput(), Console.OpenStandardOutput());

        using var server = new LspServer(connection.Write);

        while (server.Running)
        {
            JsonDocument? message;

            try
            {
                message = connection.Read();
            }
            catch (JsonException failure)
            {
                // The framing held - the body was read to the byte - so the next message is
                // still where it should be. Only this one is lost.
                Complain($"Unreadable message: {failure.Message}");
                continue;
            }

            if (message is null)
                break;

            using (message)
            {
                try
                {
                    server.Handle(message);
                }
                catch (Exception failure)
                {
                    // One request that cannot be answered is not a reason to stop answering
                    // the rest. An editor that loses its server loses every underline in the
                    // file with it.
                    Complain($"Failed while handling a message: {failure}");
                }
            }
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Picks the language reports come out in, from `--messages` or the environment.
    /// </summary>
    /// <remarks>
    /// Read once. The catalog is settled into each report as it is recorded, so changing the
    /// language means restarting the server - which spec/ops/lsp.md section 8 says out loud.
    /// </remarks>
    private static void ChooseMessageLanguage(string[] arguments)
    {
        string asked = "";

        for (int at = 0; at < arguments.Length; at++)
        {
            if (arguments[at] == "--messages" && at + 1 < arguments.Length)
                asked = arguments[at + 1].Trim();
            else if (arguments[at].StartsWith("--messages=", StringComparison.Ordinal))
                asked = arguments[at]["--messages=".Length..].Trim();
        }

        if (asked.Length == 0)
            asked = Environment.GetEnvironmentVariable("TABBIT_MESSAGES")?.Trim() ?? "";

        if (asked.Length > 0)
            Messages.MessageCatalog.Current = Messages.MessageCatalog.ForLanguage(asked);
    }

    private static void Complain(string said) => Console.Error.WriteLine($"tabbit lsp: {said}");
}
