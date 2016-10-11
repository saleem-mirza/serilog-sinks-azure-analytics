using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Batch;
using Serilog.Extensions;
using Newtonsoft.Json;
using System.Text;

namespace Serilog.Sinks.AzureAnalytics
{
    internal class AzureAnalyticsSink : BatchProvider, ILogEventSink
    {
        private readonly string _workSpaceId;
        private readonly string _logName;
        private readonly string _authenticationId;
        private readonly Uri _analyticsUrl;

        internal AzureAnalyticsSink(
            string workSpaceId,
            string authenticationId,
            string logName)
        {
            _workSpaceId = workSpaceId;
            _authenticationId = authenticationId;
            _logName = logName;

            _analyticsUrl = new Uri("https://" + _workSpaceId + ".ods.opinsights.azure.com/api/logs?api-version=2016-04-01");

        }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion

        protected override void WriteLogEvent(ICollection<LogEvent> logEventsBatch)
        {
            if(logEventsBatch == null || logEventsBatch.Count == 0)
            {
                return;
            }

            var logEventsJson = new StringBuilder();

            foreach (var logEvent in logEventsBatch)
            {
                var jsonString = JsonConvert.SerializeObject(
                    JObject.FromObject(
                        logEvent.Dictionary(storeTimestampInUtc: true))
                        .Flaten());
                logEventsJson.Append(jsonString);
                logEventsJson.Append(",");
            }

            if (logEventsJson.Length > 0)
            {
                logEventsJson.Remove(logEventsJson.Length - 1, 1);
            }

            if(logEventsBatch.Count > 1)
            {
                logEventsJson.Insert(0, "[");
                logEventsJson.Append("]");
            }

            var dateString = DateTime.UtcNow.ToString("r");
            var stringToHash = "POST\n" + logEventsJson.Length + "\napplication/json\n" + "x-ms-date:" + dateString + "\n/api/logs";
            var hashedString = BuildSignature(stringToHash, _authenticationId);
            var signature = "SharedKey " + _workSpaceId + ":" + hashedString;

            PostData(signature, dateString, logEventsJson.ToString());
        }

        private string BuildSignature(string message, string secret)
        {
            var encoding = new System.Text.ASCIIEncoding();
            var keyByte = Convert.FromBase64String(secret);
            var messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                var hash = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hash);
            }
        }

        private void PostData(string signature, string dateString, string jsonString)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                client.Headers.Add("Log-Type", _logName);
                client.Headers.Add("Authorization", signature);
                client.Headers.Add("x-ms-date", dateString);
                client.Headers.Add("time-generated-field", "Timestamp");
                client.UploadString(_analyticsUrl, "POST", jsonString);
            }
        }
    }
}
