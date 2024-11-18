namespace Serilog.Sinks.AzureLogAnalytics
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Serilog.Events;

    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(IEnumerable<IDictionary<string, object>>))]
    [JsonSerializable(typeof(LogEvent))]
    internal partial class LogsJsonSerializerContext : JsonSerializerContext
    {
    }
}