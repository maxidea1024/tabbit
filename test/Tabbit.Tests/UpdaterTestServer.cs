using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Tabbit.Tests;

/// <summary>
/// Serves a directory, and can be told to refuse.
/// </summary>
internal sealed class UpdaterTestServer : IDisposable
{
    private readonly HttpListener _listener = new HttpListener();
    private readonly string _root;
    private int _failuresLeft;
    private HttpStatusCode _failureStatus;

    public UpdaterTestServer(string root)
    {
        _root = root;

        int port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}/data";

        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();

        Task.Run(Loop);
    }

    public string BaseUrl { get; }

    /// <summary>Paths requested, in order. Cleared by a test that wants to count.</summary>
    public List<string> Requests { get; } = new List<string>();

    /// <summary>Makes the next `count` requests answer with `status`.</summary>
    public void FailNext(int count, HttpStatusCode status)
    {
        _failuresLeft = count;
        _failureStatus = status;
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            string path = context.Request.Url.AbsolutePath;

            lock (Requests)
                Requests.Add(path);

            try
            {
                if (_failuresLeft > 0)
                {
                    _failuresLeft--;
                    context.Response.StatusCode = (int)_failureStatus;
                }
                else
                {
                    string name = Path.GetFileName(path);
                    string file = Path.Combine(_root, name);

                    if (!File.Exists(file))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                    else
                    {
                        byte[] bytes = File.ReadAllBytes(file);

                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    }
                }
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
