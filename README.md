# Serilog.Sinks.AzureAnalytics
High performance Serilog sink that writes to Azure Log Analytics. It supports automatic batching of log messages for better performance and auto-recovery from transient errors.


## Getting started
Install [Serilog.Sinks.AzureAnalytics](https://www.nuget.org/packages/serilog.sinks.azureanalytics) from NuGet

```PowerShell
Install-Package Serilog.Sinks.AzureAnalytics
```

Configure logger by calling `WriteTo.AzureLogAnalytics(<workspaceId>, <authenticationId>)`

> `workspaceId`: Workspace Id from Azure OMS Portal connected sources.
>
> `authenticationId`: Primary or Secondary key from Azure OMS Portal connected sources.


This sink accepts following optional configuration parameters for fine grained control.

> `logName`: A distinguishable log type name. Default is "DiagnosticsLog"

> `restrictedToMinimumLevel`: The minimum log event level required in order to write an event to the sink.

> `storeTimestampInUtc`: Flag dictating if timestamp to be stored in UTC or local timezone format.

> `formatProvider`: Supplies an object that provides formatting information for formatting and parsing operations.

> `logBufferSize`: Maximum number of log entries this sink can hold before stop accepting log messages. Default is 25000, acceptable range is between 5000 to 100000.

> `batchSize`: Number of log messages to be sent as batch. Default 100, acceptable range is between 1 and 1000

> `azureOfferingType`: Enum specifying if log is being sent to public or government subscription. Default is AzureOfferingType.Public

> `flattenObject`: Flag to set if complex object in LogProperties should be flatten out or embed as JSON object. Default is True.

```C#
var logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(<workspaceId>, <authenticationId>)
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
  "Serilog": {
    "Using": [ "Serilog.Sinks.AzureAnalytics" ],
    "MinimumLevel": "Information",
    "Override": {
      "System": "Information",
      "Microsoft": "Information",
      "Microsoft.AspNetCore.Authentication": "Information",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "Microsoft.AspNetCore.Http.Connections": "Debug"
    },
    "WriteTo": [
      {
        "Name": "AzureAnalytics",
        "Args": {
          "logName": "Your Logger Name",
          "authenticationId": "******************************",
          "workspaceId": "****************************"          
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId", "WithThreadName", "WithEventType" ]
  }
```

## XML <appSettings> configuration

To use the AzureLogAnalytics sink with the [Serilog.Settings.AppSettings](https://www.nuget.org/packages/Serilog.Settings.AppSettings) package, first install that package if you haven't already done so:

```PowerShell
Install-Package Serilog.Settings.AppSettings
```
In your code, call `ReadFrom.AppSettings()`

```C#
var logger = new LoggerConfiguration()
    .ReadFrom.AppSettings()
    .CreateLogger();
```
In your application's App.config or Web.config file, specify the `AzureLogAnalytics` sink assembly and required **workspaceId** and **authenticationId** parameters under the `<appSettings>`

```XML
<appSettings>
  <add key="serilog:using:AzureLogAnalytics" value="Serilog.Sinks.AzureAnalytics" />
  <add key="serilog:write-to:AzureLogAnalytics.workspaceId" value="*************" />
  <add key="serilog:write-to:AzureLogAnalytics.authenticationId" value="*************" />
 </appSettings>
```

## Performance
Sink buffers log internally and flush to Azure Log Analytics in batches using dedicated thread for better performance.

## [Azure Log Analytics data limits](https://docs.microsoft.com/en-us/azure/log-analytics/log-analytics-data-collector-api#data-limits)
There are some constraints around the data posted to the Log Analytics Data collection API.

Maximum of 30 MB per post to Log Analytics. This is a size limit for a single post. If the data from a single post that exceeds 30 MB, you should split the data up to smaller sized chunks and send them concurrently.

Maximum of 32 KB limit for field values. If the field value is greater than 32 KB, the data will be truncated.

Recommended maximum number of fields for a given type is 50. This is a practical limit from a usability and search experience perspective.

>**Note**: Log data exceeding maximum permissible size will get dropped and will not appear in Azure Log.

---

Many thanks to the [<img src="resources/jetbrains.svg" width="100px"/>](https://www.jetbrains.com "JetBrains") for donating awesome suite of tools making this project possible.
