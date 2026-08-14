# Serilog.Sinks.AzureLogAnalytics

Serilog sink that writes log events to Azure Monitor through the [Logs Ingestion API](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/logs-ingestion-api-overview) and Data Collection Rules. Serilog's batching owns queuing, retry with backoff, and flushing on shutdown, so the sink holds no threads or buffers of its own.

## Requirements

- Serilog 4.4.0 or later. The batching API this sink builds on arrived in that release, and older versions fail to build.
- `netstandard2.0` or `net8.0`.

## Install

Install [Serilog.Sinks.AzureLogAnalytics](https://www.nuget.org/packages/serilog.sinks.AzureLogAnalytics) from NuGet:

```PowerShell
Install-Package Serilog
Install-Package Serilog.Sinks.AzureLogAnalytics
```

Add `Serilog.Settings.Configuration` and `Microsoft.Extensions.Configuration.Json` for the `appsettings.json` route.

## Azure setup

The sink posts one object per event, with three fields:

```JSON
{
  "TimeGenerated": "2026-08-14T15:43:23.2639270Z",
  "Event": { "Timestamp": "...", "Level": "Error", "MessageTemplate": "...", "Properties": { } },
  "Message": "rendered message text"
}
```

Your custom table and your DCR stream declaration must use exactly those three column names, in PascalCase. Azure drops fields whose names do not match and still answers with a success status, so a spelling or casing mismatch looks like silent data loss with no error anywhere.

### 1. Create the custom table

The table name must end in `_CL`, and `TimeGenerated` is required.

```bash
az monitor log-analytics workspace table create \
  -g <resource-group> --workspace-name <workspace> -n MyLogs_CL \
  --columns TimeGenerated=datetime Event=dynamic Message=string
```

### 2. Create the data collection rule

Save this as `dcr.json`, filling in your data collection endpoint and workspace resource IDs:

```JSON
{
  "location": "<region>",
  "properties": {
    "dataCollectionEndpointId": "<data collection endpoint resource id>",
    "streamDeclarations": {
      "Custom-MyLogs_CL": {
        "columns": [
          { "name": "TimeGenerated", "type": "datetime" },
          { "name": "Event",         "type": "dynamic"  },
          { "name": "Message",       "type": "string"   }
        ]
      }
    },
    "destinations": {
      "logAnalytics": [
        { "name": "law", "workspaceResourceId": "<workspace resource id>" }
      ]
    },
    "dataFlows": [
      {
        "streams": [ "Custom-MyLogs_CL" ],
        "destinations": [ "law" ],
        "transformKql": "source",
        "outputStream": "Custom-MyLogs_CL"
      }
    ]
  }
}
```

```bash
az rest --method put \
  --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Insights/dataCollectionRules/<dcr-name>?api-version=2023-03-11" \
  --body @dcr.json
```

Read back the `immutableId`, which is what the sink sends. It differs from the rule name:

```bash
az monitor data-collection rule show -g <rg> -n <dcr-name> --query immutableId -o tsv
```

To promote a field out of `Event` into its own typed column, add it to the table and compute it in `transformKql`. Note that Serilog's `JsonFormatter` omits `Level` for Information events, so guard against the empty case:

```kusto
source | extend Level = iff(isempty(tostring(Event.Level)), 'Information', tostring(Event.Level))
```

DCR transforms run a restricted subset of KQL. `coalesce` is rejected at rule creation, for example.

### 3. Grant publish rights

The identity writing logs needs the `Monitoring Metrics Publisher` role, scoped to the DCR:

```bash
az role assignment create --role "Monitoring Metrics Publisher" \
  --assignee <principal> --scope <dcr resource id>
```

Role assignments on a DCR take up to 15 minutes to propagate, and a newly created custom table takes 5 to 15 minutes before its first rows are queryable. An empty table shortly after the first write is usually timing rather than misconfiguration.

[Tutorial: Send data to Azure Monitor Logs with Logs ingestion API (Azure portal)](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/tutorial-logs-ingestion-portal) covers the same setup through the portal.

## Configure the sink

### With a TokenCredential (recommended)

`LoggerCredential.TokenCredential` accepts any `Azure.Core.TokenCredential`, which keeps secrets out of your configuration entirely. `DefaultAzureCredential` uses a managed identity in Azure and your `az login` session locally:

```C#
var credentials = new LoggerCredential
{
    Endpoint = "https://<your-dce>.<region>.ingest.monitor.azure.com",
    ImmutableId = "dcr-****",
    StreamName = "Custom-MyLogs_CL",
    TokenCredential = new DefaultAzureCredential()
};

Log.Logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(credentials, new ConfigurationSettings())
    .CreateLogger();
```

When `TokenCredential` is set, the sink ignores `TenantId`, `ClientId`, and `ClientSecret`.

This route requires configuring the sink in code. `Serilog.Settings.Configuration` binds strings by name and has no way to construct a `TokenCredential`, so `appsettings.json` supports only the client-secret path below.

### With a client secret

```C#
var credentials = new LoggerCredential
{
    Endpoint = "https://<your-dce>.<region>.ingest.monitor.azure.com",
    ImmutableId = "dcr-****",
    StreamName = "Custom-MyLogs_CL",
    TenantId = "****-****-****-****-****",
    ClientId = "****-****-****-****-****",
    ClientSecret = "*******"
};
```

The sink builds a `ClientSecretCredential` from these on the first batch rather than at `CreateLogger()`, so an invalid secret fails a batch that Serilog retries and reports through `SelfLog`, instead of aborting application startup.

### Credential parameters

| Parameter name    | Meaning                                                                        |
|-------------------|--------------------------------------------------------------------------------|
| `endpoint`        | Logs ingestion URL of the data collection endpoint.                             |
| `immutableId`     | `immutableId` of the data collection rule, not the rule name.                   |
| `streamName`      | Stream name declared in the DCR, in the form `Custom-<table>_CL`.               |
| `tokenCredential` | Any `Azure.Core.TokenCredential`. Takes precedence over the three fields below. |
| `tenantId`        | Directory (tenant) ID of the registered Entra application.                      |
| `clientId`        | Application (client) ID of the Entra application.                               |
| `clientSecret`    | Client secret of the registered Entra application.                              |

### Sink settings

| Parameter name          | Default   | Accepted range          | Meaning                                                              |
|-------------------------|-----------|-------------------------|----------------------------------------------------------------------|
| `batchSize`             | 100       | 1 to 1000               | Events per request.                                                   |
| `bufferSize`            | 5000      | 1000 to 25000           | Events held in the queue before new ones are dropped.                 |
| `minLogLevel`           | `Verbose` | Any `LogEventLevel`     | Minimum level written. Ignored when `levelSwitch` is set.             |
| `levelSwitch`           | `null`    | `LoggingLevelSwitch`    | Runtime level control. Overrides `minLogLevel` whenever it is set.    |
| `propertyNamingStrategy`| `Default` | `Default`, `CamelCase`  | Does not rename the `TimeGenerated`, `Event`, or `Message` keys.      |
| `maxDepth`              | 5         | 1 to 20                 | Declared but never read. Retained for API compatibility.              |
| `formatProvider`        | `null`    | `IFormatProvider`       | Declared but never read. Retained for API compatibility.              |

Values outside the accepted range fall back to the default silently rather than throwing.

`propertyNamingStrategy` never renames the three envelope keys, because those keys must match your DCR column names. The naming inside `Event` comes from the `ITextFormatter`.

Batches flush when `batchSize` is reached or after 10 seconds, whichever comes first. Failed batches are retried for up to 2 minutes.

## JSON appsettings configuration

```JSON
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.AzureLogAnalytics" ],
    "MinimumLevel": "Verbose",
    "WriteTo": [
      {
        "Name": "AzureLogAnalytics",
        "Args": {
          "credentials": {
            "endpoint": "https://****.****.ingest.monitor.azure.com",
            "immutableId": "dcr-****",
            "streamName": "Custom-****_CL",
            "tenantId": "****-****-****-****-****",
            "clientId": "****-****-****-****-****",
            "clientSecret": "*******"
          },
          "configSettings": {
            "bufferSize": "5000",
            "batchSize": "100"
          }
        }
      }
    ]
  }
}
```

To instantiate the sink from `appsettings.json`, call:

```C#
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json").Build();

Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
```

## Formatting event JSON

To change the shape of the JSON written into the `Event` column, pass an `ITextFormatter` as the first argument. The default is Serilog's `JsonFormatter`:

```C#
Log.Logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(new CompactJsonFormatter(), credentials, configSettings)
    .CreateLogger();
```

## Diagnostics

The sink reports delivery failures through Serilog's `SelfLog` rather than by throwing into your application. Enable it while setting up ingestion, otherwise a rejected batch is invisible:

```C#
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

Silence from `SelfLog` means the endpoint accepted the batch. Rows that never appear after an accepted batch point at the DCR rather than the sink, most often a column name mismatch or a `transformKql` that drops the row.

## Upgrading

Version 8.0.0 contains a breaking change to `ConfigurationSettings`. See [CHANGELOG.md](CHANGELOG.md).
