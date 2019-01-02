using System;

namespace Serilog.Sinks.AzureAnalytics
{
    public class ConfigurationSettings
    {
        private int _logBufferSize = 25_000;
        private int _batchSize = 100;
        private string _logName = "DiagnosticsLog";
        public bool StoreTimestampInUtc = false;
        public IFormatProvider FormatProvider;
        public AzureOfferingType AzureOfferingType = AzureOfferingType.Public;
        public NamingStrategy PropertyNamingStrategy = NamingStrategy.Default;
        public string SecondaryAuthenticationKey = null;

        public int BufferSize
        {
            get => _logBufferSize;
            set => _logBufferSize = value >= 5_000 && value <= 100_000 ? value : 25_000;
        }

        public int BatchSize
        {
            get => _batchSize;
            set => _batchSize = value >= 1 && value <= 1_000 ? value : 100;
        }

        public string LogName
        {
            get => _logName;
            set => _logName = string.IsNullOrWhiteSpace(value) ? "DiagnosticsLog" : value;
        }
    }

    public enum NamingStrategy
    {
        Default = 0,
        CamelCase = 1,
        Application = 2
    }
}
