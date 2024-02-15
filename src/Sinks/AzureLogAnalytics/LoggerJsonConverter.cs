using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serilog.Sinks.AzureLogAnalytics
{
    internal class LoggerJsonConverter : JsonConverter<LogEvent>
    {
        private readonly ITextFormatter _formatter;
        public LoggerJsonConverter(ITextFormatter formatter)
        {
            if (formatter != null)
            {
                _formatter = formatter;
            } else
            {
                _formatter = new JsonFormatter();
            }
        }
        public override LogEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, LogEvent value, JsonSerializerOptions options)
        {
            var jsonString = new StringWriter();
            _formatter.Format(value, jsonString);
            writer.WriteRawValue(jsonString.ToString());
        }
    }
}
