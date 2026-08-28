using System.IO;
using System.Text;
using System.Text.Json;
using Tabbit.Lsp;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The framing around a protocol message: the header block, and the body that follows it.
/// </summary>
/// <remarks>
/// **Byte counting is the whole of it.** `Content-Length` counts UTF-8 bytes, and a reader
/// that counts characters instead is right until the first message carrying a non-ASCII
/// character - after which it is cut mid-message and never recovers. Every `.tbs` file in this
/// repository has Korean in its `///` comments, so that message is the first `didOpen`.
/// spec/ops/lsp.md section 8.
/// </remarks>
public class LspFramingTests
{
    private static JsonRpcConnection Reading(string wire)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(wire)), new MemoryStream());

    [Fact]
    public void A_message_is_read_from_its_declared_length()
    {
        using var message = Reading("Content-Length: 15\r\n\r\n{\"method\":\"hi\"}").Read();

        Assert.NotNull(message);
        Assert.Equal("hi", message!.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void The_length_counts_bytes_and_not_characters()
    {
        // Eleven characters of JSON, three of them Korean and three bytes each - so counting
        // characters would ask for six bytes too few and cut the message.
        string body = "{\"m\":\"한국어\"}";

        Assert.Equal(11, body.Length);
        Assert.Equal(17, Encoding.UTF8.GetByteCount(body));

        string wire = $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

        using var message = Reading(wire).Read();

        Assert.NotNull(message);
        Assert.Equal("한국어", message!.RootElement.GetProperty("m").GetString());
    }

    [Fact]
    public void Two_messages_arrive_one_after_the_other()
    {
        var connection = Reading(
            "Content-Length: 7\r\n\r\n{\"i\":1}" +
            "Content-Length: 7\r\n\r\n{\"i\":2}");

        using var first = connection.Read();
        using var second = connection.Read();

        Assert.Equal(1, first!.RootElement.GetProperty("i").GetInt32());
        Assert.Equal(2, second!.RootElement.GetProperty("i").GetInt32());
    }

    [Fact]
    public void Header_names_are_matched_without_case_and_other_headers_are_ignored()
    {
        string wire =
            "content-length: 7\r\n" +
            "Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n" +
            "{\"i\":1}";

        using var message = Reading(wire).Read();

        Assert.Equal(1, message!.RootElement.GetProperty("i").GetInt32());
    }

    [Fact]
    public void A_closed_stream_reads_as_nothing()
    {
        Assert.Null(Reading("").Read());
        Assert.Null(Reading("Content-Length: 40\r\n\r\n{\"cut\":").Read());
    }

    [Fact]
    public void What_is_written_carries_its_own_byte_count()
    {
        var written = new MemoryStream();
        var connection = new JsonRpcConnection(new MemoryStream(), written);

        connection.Write(new NotificationMessage
        {
            Method = "textDocument/publishDiagnostics",
            Params = new PublishDiagnosticsParams("file:///c%3A/x.tbs", []),
        });

        string wire = Encoding.UTF8.GetString(written.ToArray());
        int blank = wire.IndexOf("\r\n\r\n", System.StringComparison.Ordinal);
        string body = wire[(blank + 4)..];

        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n", wire);

        // Spelled the way the protocol spells it: camel case, and `params` rather than the
        // member's own name.
        Assert.Contains("\"jsonrpc\":\"2.0\"", body);
        Assert.Contains("\"method\":\"textDocument/publishDiagnostics\"", body);
        Assert.Contains("\"params\":", body);
    }

    [Fact]
    public void A_response_of_nothing_still_says_it_answered()
    {
        var written = new MemoryStream();
        var connection = new JsonRpcConnection(new MemoryStream(), written);

        using var id = JsonDocument.Parse("7");

        connection.Write(new ResponseMessage { Id = id.RootElement.Clone(), Result = null });

        string wire = Encoding.UTF8.GetString(written.ToArray());

        // A response with neither a result nor an error is not a response, so the null is
        // written rather than left out.
        Assert.Contains("\"result\":null", wire);
        Assert.Contains("\"id\":7", wire);
    }
}
