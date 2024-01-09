# Serilog.Sinks.AzureAnalytics
High performance Serilog sink that writes to Azure Log Analytics. It supports automatic batching of log messages for better performance and auto-recovery from transient errors.


## Getting started
Install [Serilog.Sinks.AzureAnalytics](https://www.nuget.org/packages/serilog.sinks.azureanalytics) from NuGet

```PowerShell
Install-Package Serilog.Sinks.AzureAnalytics
```

Configure logger by calling `WriteTo.AzureLogAnalytics(<credentials>, <configSettings>)`

> `credentials`: A structure with required information to access Azure Log Ingestion API. to  from Azure OMS Portal connected sources. This parameter accepts:
```
endpoint:     Logs Ingestion URL for data collection endpoint.
immutableId:  ImmutableId for Data Collection Rules (DCR)
streamName:   Output stream name of target (Log Analytics API, can be accessed from DCR)
tenantId:     Directory (tenant) Id of registered application (Microsoft Entra ID)
clientId:     Application (client) Id of Microsoft Entra application
clientSecret: Client secret for registered Engra Application  
```
>

```C#
var logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(<credentials>, <configSettings>)
    .CreateLogger();
```

## JSON appsettings configuration

To configure AzureLogAnalytics sink in `appsettings.json`, in your code, call:

```C#
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{environment}.json")
                    .AddEnvironmentVariables()
                    .Build();

var logger = new LoggerConfiguration()
                         .ReadFrom.Configuration(configuration)
                         .CreateLogger();
```
In your `appsettings.json` file, configure following:

```JSON
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.AzureAnalytics" ],
    "MinimumLevel": "Verbose",
    "WriteTo": [
      {
        "Name": "AzureAnalytics",
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

```PowerShell
Install-Package Serilog.Settings.AppSettings
```
In your code, call `ReadFrom.AppSettings()`

```C#
var logger = new LoggerConfiguration()
    .ReadFrom.AppSettings()
    .CreateLogger();
```

