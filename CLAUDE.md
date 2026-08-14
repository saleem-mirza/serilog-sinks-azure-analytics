# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Serilog sink that ships log events to Azure Monitor through the Logs Ingestion API (Data Collection Rules), published as the `Serilog.Sinks.AzureLogAnalytics` NuGet package. Single project, no test project.

## Commands

All commands run from `src/`:

```bash
dotnet restore
dotnet build --no-restore
dotnet pack                       # produces the NuGet package
dotnet build -f net8.0            # single target framework
```

There is no test project, no solution file, and no lint or format step. Build and pack are the only verification checked into the repo, so run them from `src/` after any change.

To verify delivery behavior without an Azure subscription, point the sink at a local `HttpListener`: set `LoggerCredential.Endpoint` to `http://localhost:<port>`, supply a stub `TokenCredential` returning any non-empty token, and assert on the received body. That covers batching, the request URL, the bearer header, the envelope shape, and the retry path.

Targets `netstandard2.0;net8.0` with `LangVersion` 8.0. Language features newer than C# 8 will not compile. The assembly is strong-named with `src/Serilog.snk`, so a build needs that key file present.

CI (`.github/workflows/dotnet.yml`) runs restore, build, and pack on push and PR against `vnext`. The default working branch is `dev`, so CI does not fire for it.

## Architecture

Two types carry the sink, plus a converter:

1. `Sinks/AzureLogAnalytics/AzureLogAnalyticsSink.cs` implements `Serilog.Core.IBatchedLogEventSink`. Serilog's own `BatchingSink` owns all queuing, batching, retry, and shutdown flushing, so this class holds no threads or buffers. `EmitBatchAsync` wraps each `LogEvent` into `{ TimeGenerated, Event, Message }` and POSTs the array to `{Endpoint}/dataCollectionRules/{ImmutableId}/streams/{StreamName}?api-version=2023-01-01`. `OnEmptyBatchAsync` returns a completed task. Auth is a bearer token cached in `token`/`expire_on`, refreshed inline: either `LoggerCredential.TokenCredential` (any `Azure.Core` credential) or a client-credentials POST to `login.microsoftonline.com` when only tenant/client/secret are set. `httpClient` is a single static instance shared across sinks; its default `Authorization` header is mutated on refresh.

2. `LoggerConfigurationExtensions.cs` exposes the two `WriteTo.AzureLogAnalytics(...)` overloads, differing by a leading `ITextFormatter` parameter. The shorter one delegates to the longer with a null formatter, so the sink is constructed in one place. It translates `ConfigurationSettings` into Serilog's `BatchingOptions`: `BatchSize` maps to `BatchSizeLimit`, `BufferSize` to `QueueLimit`, and `BufferingTimeLimit` (10 seconds) plus `RetryTimeLimit` (2 minutes) are set explicitly to preserve the cadence of the hand-rolled batcher that Serilog's batching replaced. `ConfigurationSettings` and `LoggerCredential` property names must stay in sync with the `appsettings.json` keys documented in README.md, since `Serilog.Settings.Configuration` binds by name.

Event serialization goes through `LoggerJsonConverter`, a `JsonConverter<LogEvent>` that delegates to the supplied `ITextFormatter` (defaulting to Serilog's `JsonFormatter`) and writes the result with `WriteRawValue`. It decides the payload of the `Event` property only. The surrounding envelope and the serializer options (naming policy, `ReferenceHandler.IgnoreCycles`) are set in `AzureLogAnalyticsSink`.

The envelope is built as `Dictionary<string, object>`, and System.Text.Json does not apply `PropertyNamingPolicy` to dictionary keys. `TimeGenerated`, `Event`, and `Message` therefore stay PascalCase on the wire even under `NamingStrategy.CamelCase`, which matches the DCR column names. Switching the envelope to an anonymous type or a POCO would rename those columns and break ingestion.

`ConfigurationSettings` clamps values in its property setters, silently falling back to the default when out of range: `MaxDepth` 1-20 (default 5), `BufferSize` 1000-25000 (default 5000), `BatchSize` 1-1000 (default 100).

Two settings are inert. `MaxDepth` is clamped and never read. `FormatProvider` is declared and read nowhere. Both are public API, so removing them is a breaking change.

## Gotchas

- `EmitBatchAsync` must let exceptions escape. Serilog's batching infrastructure treats a thrown exception as batch failure and owns retry, backoff, and `SelfLog` diagnostics. Swallowing the failure and returning normally tells Serilog the batch was delivered, and the events are gone. `PostDataAsync` throws on both a missing token and a non-success status for that reason.
- `BatchingOptions.EagerlyEmitFirstEvent` defaults to true, so the first event after startup ships on its own rather than waiting for the buffer window. Set it to false to trade startup liveness for fewer requests.
- The OAuth scope at `AzureLogAnalyticsSink.cs` is `https://monitor.azure.com//.default`. The doubled slash is required by Azure Monitor. Removing it looks like a typo fix and breaks authentication at runtime, where no test catches it.
- `LoggerConfigurationExtensions` passes both `restrictedToMinimumLevel: MinLogLevel` and `levelSwitch: LevelSwitch` to `Sink(...)`, and `ConfigurationSettings` always constructs a `LevelSwitch` at `Verbose`. Serilog ignores the minimum level whenever a switch is supplied, so raising `MinLogLevel` alone changes nothing. Set `LevelSwitch`.
- The batching API (`IBatchedLogEventSink`, `BatchingOptions`, the `Sink(IBatchedLogEventSink, ...)` overload) requires Serilog 4.4.0 or later. Downgrading the Serilog reference breaks the build.
- `Azure.Core` drags in MSAL, `System.ClientModel`, and several `Microsoft.Extensions.*` abstractions transitively. It is present for `TokenCredential` support, which is the only reason the package carries that graph.

## Conventions

- Diagnostics go to `Serilog.Debugging.SelfLog`, never to exceptions escaping `Emit`. Batch write failures are the exception to this: they must throw so Serilog retries them.
- Everything except `LoggerConfigurationExtensions`, `LoggerCredential`, `ConfigurationSettings`, and `NamingStrategy` is `internal`. Adding a public type expands the package's API surface.
- Bump both `AssemblyVersion` and `Version` in the `.csproj` when releasing.
- The Apache 2.0 header is applied inconsistently: `LoggerConfigurationExtensions.cs` and `AzureLogAnalyticsSink.cs` carry it, while `ConfigurationSettings.cs`, `LoggerCredential.cs`, and `LoggerJsonConverter.cs` do not. Match the file you are editing rather than adding or stripping headers.
