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

    /// <remarks>
    /// **The port is claimed with a retry, because asking for a free one does not hold it.**
    /// The probe binds zero, reads what the operating system chose and lets go - and between
    /// letting go and this listener binding, another server doing the same thing can be given
    /// the same number. Serial that gap never opened; in parallel it is one of the two things
    /// that failed. doc/roadmap.md, the suite-parallelism entry.
    /// </remarks>
    public UpdaterTestServer(string root)
    {
        _root = root;

        for (int attempt = 0; ; attempt++)
        {
            int port = FreePort();

            try
            {
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();

                BaseUrl = $"http://127.0.0.1:{port}/data";
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                // Somebody else took it in the gap. The prefix has to come off too: a
                // listener keeps them, and a second Start would bind the lost one again.
                _listener.Prefixes.Clear();
            }
        }

        Task.Run(Loop);
    }

    public string BaseUrl { get; } = "";

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
