# Serilog.Sinks.AzureLogAnalytics

High performance Serilog sink that writes to Azure Log Analytics. It supports automatic batching of log messages for better performance and auto-recovery from transient errors.

## Getting started

Install [Serilog.Sinks.AzureLogAnalytics](https://www.nuget.org/packages/serilog.sinks.AzureLogAnalytics) from NuGet

```PowerShell
Install-Package Serilog
Install-Package Serilog.Settings.Configuration
Install-Package Serilog.Sinks.AzureLogAnalytics
Install-Package Microsoft.Extensions.Configuration.Json

```

Configure logger by calling `WriteTo.AzureLogAnalytics(<credentials>, <configSettings>)`

> `credentials`: A structure with required information to access Azure Log Ingestion API. to  from Azure OMS Portal connected sources. This parameter accepts:

```
endpoint:        Logs Ingestion URL for data collection endpoint.
immutableId:     ImmutableId for Data Collection Rules (DCR)
streamName:      Output stream name of target (Log Analytics API, can be accessed from DCR)
tenantId:        Directory (tenant) Id of registered application (Microsoft Entra ID)
clientId:        Application (client) Id of Microsoft Entra application
clientSecret:    Client secret for registered Engra Application
TokenCredential: An Azure Token Credntial provider implementing the Azure.Core.TokenCredential abstract class (Optional if clientSecret and clientId is provided
```
>

```C#
Log.Logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(<credentials>, <configSettings>)
    .CreateLogger();
```

## JSON appsettings configuration


In your `appsettings.json` file, configure following:

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

To configure and instanciate AzureLogAnalytics sink in `appsettings.json`, in your code, call:

```C#

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json").Build();

Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
```

[Tutorial: Send data to Azure Monitor Logs with Logs ingestion API (Azure portal)](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/tutorial-logs-ingestion-portal) is good resource to configure environment for Log Ingestion API in Azure portal.
