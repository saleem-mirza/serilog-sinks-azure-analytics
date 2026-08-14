using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Serilog.Sinks.AzureLogAnalytics.Tests;

/// <summary>
/// Stands in for the Logs Ingestion endpoint on a loopback port. <paramref name="statusFor"/>
/// receives the 1-based request number and returns the status code to reply with.
/// </summary>
internal sealed class Collector : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly TaskCompletionSource<bool> _reachedTarget = new();
    private readonly int _targetCount;

    public readonly List<string> Bodies = new();
    public readonly List<string> AuthHeaders = new();
    public readonly List<string> Paths = new();

    public int Port { get; }

    public Collector(int targetCount, Func<int, int> statusFor)
    {
        _targetCount = targetCount;
        Port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }   // listener stopped

                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                {
                    Bodies.Add(await reader.ReadToEndAsync());
                }
                AuthHeaders.Add(ctx.Request.Headers["Authorization"] ?? "(none)");
                Paths.Add(ctx.Request.Url!.PathAndQuery);

                ctx.Response.StatusCode = statusFor(Bodies.Count);
                ctx.Response.Close();

                if (Bodies.Count >= _targetCount) _reachedTarget.TrySetResult(true);
            }
        });
    }

    /// <summary>Waits for targetCount requests, or the timeout. Assertions report what arrived.</summary>
    public Task WaitAsync(int seconds) =>
        Task.WhenAny(_reachedTarget.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));

    public string AllBodies => string.Join("", Bodies);

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose() => _listener.Stop();
}
