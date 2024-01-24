using Serilog.Core;
using Serilog.Events;
using System;

namespace Serilog.Sinks.AzureLogAnalytics
{
    public class ConfigurationSettings
    {
        private int maxDepth;
        private LogEventLevel minLogLevel;
        private int bufferSize;
        private int batchSize;

        public ConfigurationSettings()
        {
            PropertyNamingStrategy = NamingStrategy.Default;
            LevelSwitch = new LoggingLevelSwitch(LogEventLevel.Verbose);
            MinLogLevel = LogEventLevel.Verbose;
            maxDepth = 5;
            bufferSize = 5000;
            batchSize = 100;
        }

        public IFormatProvider FormatProvider { get; set; }
        public NamingStrategy PropertyNamingStrategy { get; set; }
        public LoggingLevelSwitch LevelSwitch { get; set; }
        public LogEventLevel MinLogLevel
        {
            get => minLogLevel;
            set => minLogLevel = value;
        }
        public int MaxDepth
        {
            get => maxDepth;
            set
            {
                if (value > 0 && value <= 20) { maxDepth = value; } else { maxDepth = 5; }
            }
        }
        public int BufferSize
        {
            get => bufferSize;
            set
            {
                if (value >= 1000 && value <= 25_000) { bufferSize = value; } else { bufferSize = 5000; }
            }
        }
        public int BatchSize
        {
            get => batchSize;
            set
            {
                if (value > 0 && value <= 1000) { batchSize = value; } else { batchSize = 100; }
            }
        }
    }

    public enum NamingStrategy
    {
        Default = 0,
        CamelCase = 1
    }
}
