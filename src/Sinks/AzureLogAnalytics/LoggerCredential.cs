using Azure.Core;
using System;

namespace Serilog.Sinks.AzureLogAnalytics
{
    public class LoggerCredential
    {
        public String Endpoint { get; set; }
        public String ImmutableId { get; set; }
        public String StreamName { get; set; }
        public String TenantId { get; set; }
        public String ClientId { get; set; }
        public String ClientSecret { get; set; }
        public TokenCredential TokenCredential { get; set; }
    }
}