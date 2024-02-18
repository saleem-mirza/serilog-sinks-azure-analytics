using System;

namespace Serilog.Sinks.AzureAnalytics
{
    public class ConfigurationSettings
    {
        public const int DefaultBatchSize = 1_000;
        public const int DefaultBufferSize = 10_000;
        public const int MaxBufferSize = 100_000;

        private int? _logBufferSize = DefaultBufferSize;
        private int _batchSize = DefaultBatchSize;
        private string _logName = "DiagnosticsLog";
        public bool StoreTimestampInUtc = false;
        public IFormatProvider FormatProvider;
        public AzureOfferingType AzureOfferingType = AzureOfferingType.Public;
        public NamingStrategy PropertyNamingStrategy = NamingStrategy.Default;

        /// <summary>
        /// Maximum number of events to hold in the sink's internal queue, or <c>null</c>
        /// for an unbounded queue. The default is <c>100000</c>.
        /// </summary>
        public int? BufferSize
        {
            get => _logBufferSize;
            set => _logBufferSize = value is >= 1 and <= MaxBufferSize or null ? value : DefaultBufferSize;
        }

        public int BatchSize
        {
            get => _batchSize;
            set => _batchSize = value >= 1 ? value : DefaultBufferSize;
        }

        public string LogName
        {
            get => _logName;
            set => _logName = string.IsNullOrWhiteSpace(value) ? "DiagnosticsLog" : value;
        }

        public bool Flatten { get; set; }
        
        public string Proxy { get; set; }
    }

    public enum NamingStrategy
    {
        Default = 0,
        CamelCase = 1,
        Application = 2
    }
}
