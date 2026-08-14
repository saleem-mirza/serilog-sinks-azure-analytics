# Changelog

This changelog starts at 8.0.0. For earlier releases, see the commit history.

## 8.0.0

Requires Serilog 4.4.0 or later. Targets `netstandard2.0` and `net8.0`.

### Breaking

- `ConfigurationSettings` no longer constructs a `LoggingLevelSwitch` in its constructor. Serilog ignores `restrictedToMinimumLevel` whenever a switch is supplied, so the old default switch made `MinLogLevel` inert. `MinLogLevel` now governs by default, and assigning `LevelSwitch` overrides it. Code that reads `settings.LevelSwitch` without assigning it first now gets `null`.

### Fixed

- `TimeGenerated` now comes from `LogEvent.Timestamp` rather than `DateTime.UtcNow` at serialize time. A retried batch previously landed stamped up to `RetryTimeLimit` late, and every event in a batch shared a single timestamp.
- Credential construction is deferred to the first batch. `ClientSecretCredential` validates tenant, client, and secret in its own constructor, which ran while the logger was being configured, so a bad secret aborted application startup. A bad secret now fails a batch, which Serilog retries and reports through `SelfLog`.
- Each request sets its own `Authorization` header, so the shared static `HttpClient` no longer carries one sink's bearer token into another sink's requests.

### Changed

- Serilog 4.4 batching replaces the hand-rolled `BatchProvider`. Its queue, flush timer, retry loop, and shutdown drain are gone. The sink implements `IBatchedLogEventSink` and holds no threads or buffers.
- `Azure.Identity`'s `ClientSecretCredential` replaces the hand-rolled POST to `login.microsoftonline.com` and its JSON parsing. Token caching and refresh come from the credential. `Azure.Core` already pulled MSAL into the dependency graph, so this added one assembly.
- `LoggerCredential.TokenCredential` accepts any `Azure.Core.TokenCredential`, so managed identity and `DefaultAzureCredential` work without a client secret in configuration.

### Added

- Test project with four xunit facts driving the sink through the public `WriteTo.AzureLogAnalytics` API against an `HttpListener` on a loopback port, using a stub `TokenCredential`. The suite reaches neither Azure nor the network. CI runs a test step between build and pack.

### Notes

- `MaxDepth` and `FormatProvider` on `ConfigurationSettings` are declared but never read. They remain public API and are retained for compatibility.
