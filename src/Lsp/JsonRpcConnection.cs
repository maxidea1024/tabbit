using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabbit.Lsp;

/// <summary>
/// The framing the language server protocol puts around each message: a header block, a blank
/// line, then exactly that many bytes of JSON.
/// </summary>
/// <remarks>
/// **Bytes rather than characters, all the way through.** `Content-Length` counts UTF-8 bytes
/// and a <see cref="StreamReader"/> counts characters. One `///` line of Korean makes those
/// two numbers differ, and from that point on the stream is cut in the middle of a message and
/// every message after it is garbage. spec/ops/lsp.md section 8.
/// </remarks>
internal sealed class JsonRpcConnection
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _writing = new();

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>
    /// How every message on this connection is spelled: camel case, and nothing written for a
    /// member that is null.
    /// </summary>
    /// <remarks>
    /// The one exception is a response's `result`, which has to be written even when it is
    /// null - a response carrying neither a result nor an error is not a response. The type
    /// says so itself rather than this policy.
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The next message, or null once the other end has closed the stream.</summary>
    public JsonDocument? Read()
    {
        int length = ReadHeaders();

        if (length < 0)
            return null;

        var body = new byte[length];
        int filled = 0;

        while (filled < length)
        {
            int read = _input.Read(body, filled, length - filled);

            // The client went away in the middle of a message. Half of one is worth nothing,
            // and the session is over either way.
            if (read <= 0)
                return null;

            filled += read;
        }

        return JsonDocument.Parse(body);
    }

    /// <summary>Sends one message, header and all.</summary>
    public void Write(object message)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        // Locked because diagnostics are published from the timer that follows an edit, which
        // is not the thread reading requests. Two messages interleaved on one stream is a
        // stream the client cannot parse at all.
        lock (_writing)
        {
            _output.Write(header, 0, header.Length);
            _output.Write(body, 0, body.Length);
            _output.Flush();
        }
    }

    /// <summary>
    /// Reads the header block and answers how many bytes of body follow, or -1 at the end of
    /// the stream.
    /// </summary>
    /// <remarks>
    /// Header names are matched without case because the specification says they are
    /// case-insensitive, and `\r` is dropped rather than required: a client that writes bare
    /// newlines is still understood.
    /// </remarks>
    private int ReadHeaders()
    {
        var line = new List<byte>(64);
        int length = -1;

        while (true)
        {
            int next = _input.ReadByte();

            if (next < 0)
                return -1;

            if (next != '\n')
            {
                if (next != '\r')
                    line.Add((byte)next);

                continue;
            }

            // The blank line ends the block. Without a length there is no way to know where
            // the body stops, so the connection cannot be recovered and ends here.
            if (line.Count == 0)
                return length;

            string header = Encoding.ASCII.GetString(line.ToArray());
            line.Clear();

            int colon = header.IndexOf(':');

            if (colon <= 0)
                continue;

            if (header.AsSpan(0, colon).Trim()
                    .Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(header.AsSpan(colon + 1).Trim(), out int said))
            {
                length = said;
            }
        }
    }
}
