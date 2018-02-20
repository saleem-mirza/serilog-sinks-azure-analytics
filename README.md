# Serilog.Sinks.AzureAnalytics
High performance Serilog sink that writes to Azure Log Analytics. Sink do support automatic batching of log messages for better performance and auto-recovery from transient errors.


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

```C#
var logger = new LoggerConfiguration()
    .WriteTo.AzureLogAnalytics(<workspaceId>, <authenticationId>)
    .CreateLogger();
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
