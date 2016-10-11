using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;

namespace Serilog
{
    public static class LoggerConfigurationExtentions
    {
        public static LoggerConfiguration AzureAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            string logName = "DiagnosticsLog",
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            return loggerConfiguration.Sink(new AzureAnalyticsSink(workspaceId, authenticationId, logName), restrictedToMinimumLevel);
        }
    }
}
