# Serilog.Sinks.AzureAnalytics
A Serilog sink that writes to Azure Log Analytics.


## Getting started
Install [Serilog.Sinks.AzureAnalytics](https://www.nuget.org/packages/serilog.sinks.azureanalytics) from NuGet

```PowerShell
Install-Package Serilog.Sinks.AzureAnalytics
```

Configure logger by calling `WriteTo.AzureLogAnalytics(<workspaceId>, <authenticationId>)`

> `workspaceId`: Workspace Id from Azure OMS Portal connected sources.
>
> `authenticationId`: Primary or Secondary key from Azure OMS Portal connected sources.

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
