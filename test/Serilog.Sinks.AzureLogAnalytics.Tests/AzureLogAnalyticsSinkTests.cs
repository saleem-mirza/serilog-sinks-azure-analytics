using Azure.Core;
using System.Text.Json;

namespace Serilog.Sinks.AzureLogAnalytics.Tests;

/// <summary>
/// Counts token requests and asserts the scope, so a "typo fix" to the doubled slash in
/// https://monitor.azure.com//.default fails here rather than at runtime against Azure.
/// </summary>
internal sealed class StubCredential : TokenCredential
{
    public int Calls;

    public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        Assert.Equal(new[] { "https://monitor.azure.com//.default" }, context.Scopes);
        return new AccessToken("fake-token", DateTimeOffset.Now.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken cancellationToken) =>
        new(GetToken(context, cancellationToken));
}

public class AzureLogAnalyticsSinkTests
{
    private const string ExpectedPath =
        "/dataCollectionRules/dcr-test/streams/Custom-Test_CL?api-version=2023-01-01";

    private static LoggerCredential CredentialFor(int port, TokenCredential? token = null) => new()
    {
        Endpoint        = $"http://localhost:{port}",
        ImmutableId     = "dcr-test",
        StreamName      = "Custom-Test_CL",
        TokenCredential = token,
    };

    [Fact]
    public async Task FullBatchShipsImmediatelyAndTheRemainderFlushesOnTheTimeLimit()
    {
        var token = new StubCredential();
        using var collector = new Collector(targetCount: 2, statusFor: _ => 204);

        using (var log = new LoggerConfiguration()
            .WriteTo.AzureLogAnalytics(CredentialFor(collector.Port, token),
                new ConfigurationSettings { BatchSize = 2, BufferSize = 1000 })
            .CreateLogger())
        {
            log.Information("first {Number}", 1);
            log.Information("second {Number}", 2);
            log.Information("third {Number}", 3);

            // Batch one fills on size. Batch three waits out BufferingTimeLimit (10s).
            await collector.WaitAsync(25);
        }

        Assert.Equal(2, collector.Bodies.Count);
        Assert.Contains("first", collector.Bodies[0]);
        Assert.Contains("second", collector.Bodies[0]);
        Assert.Contains("third", collector.Bodies[1]);
        Assert.All(collector.Paths, path => Assert.Equal(ExpectedPath, path));
        Assert.All(collector.AuthHeaders, header => Assert.Equal("Bearer fake-token", header));
        Assert.Equal(collector.Bodies.Count, token.Calls);
    }

    /// <summary>
    /// The DCR column names are PascalCase. System.Text.Json skips PropertyNamingPolicy for
    /// dictionary keys, which is what keeps the envelope stable under NamingStrategy.CamelCase.
    /// </summary>
    [Fact]
    public async Task EnvelopeKeysStayPascalCaseUnderCamelCaseNaming()
    {
        using var collector = new Collector(targetCount: 1, statusFor: _ => 204);

        using (var log = new LoggerConfiguration()
            .WriteTo.AzureLogAnalytics(CredentialFor(collector.Port, new StubCredential()),
                new ConfigurationSettings
                {
                    BatchSize = 1,
                    PropertyNamingStrategy = NamingStrategy.CamelCase,
                })
            .CreateLogger())
        {
            log.Information("hello {Name}", "world");
            await collector.WaitAsync(25);
        }

        var body = Assert.Single(collector.Bodies);
        using var document = JsonDocument.Parse(body);
        var envelope = document.RootElement[0];

        Assert.True(envelope.TryGetProperty("TimeGenerated", out _));
        Assert.True(envelope.TryGetProperty("Event", out var @event));
        Assert.True(envelope.TryGetProperty("Message", out var message));
        Assert.Contains("world", message.GetString());
        Assert.Equal("hello {Name}", @event.GetProperty("MessageTemplate").GetString());
    }

    /// <summary>
    /// EmitBatchAsync must let failures propagate so Serilog's batching sink retries them.
    /// Swallowing the failure would report the batch as delivered and lose the events.
    /// </summary>
    [Fact]
    public async Task RejectedBatchIsRetriedUntilAccepted()
    {
        var token = new StubCredential();
        using var collector = new Collector(targetCount: 3, statusFor: n => n <= 2 ? 500 : 204);

        using (var log = new LoggerConfiguration()
            .WriteTo.AzureLogAnalytics(CredentialFor(collector.Port, token),
                new ConfigurationSettings { BatchSize = 2, BufferSize = 1000 })
            .CreateLogger())
        {
            log.Information("alpha");
            log.Information("beta");

            // Serilog paces retries at roughly ten seconds.
            await collector.WaitAsync(60);
        }

        Assert.True(collector.Bodies.Count >= 3, $"expected retries, saw {collector.Bodies.Count} attempts");
        Assert.All(collector.Bodies, body =>
        {
            Assert.Contains("alpha", body);
            Assert.Contains("beta", body);
        });
        Assert.All(collector.AuthHeaders, header => Assert.Equal("Bearer fake-token", header));
        Assert.Equal(collector.Bodies.Count, token.Calls);
    }

    /// <summary>Without a TokenCredential the sink builds a ClientSecretCredential. No network call.</summary>
    [Fact]
    public void ClientSecretCredentialIsBuiltWhenNoTokenCredentialIsSupplied()
    {
        var credential = CredentialFor(1);
        credential.TenantId     = "00000000-0000-0000-0000-000000000000";
        credential.ClientId     = "11111111-1111-1111-1111-111111111111";
        credential.ClientSecret = "not-a-real-secret";

        var log = new LoggerConfiguration()
            .WriteTo.AzureLogAnalytics(credential, new ConfigurationSettings())
            .CreateLogger();

        log.Dispose();
    }
}
