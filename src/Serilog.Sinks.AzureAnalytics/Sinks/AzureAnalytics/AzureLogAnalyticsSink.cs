// Copyright 2016 Zethian Inc.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Batch;
using Serilog.Sinks.Extensions;

#if NETSTANDARD1_6
using System.Net.Http;
#endif

namespace Serilog.Sinks.AzureAnalytics
{
    internal class AzureLogAnalyticsSink : BatchProvider, ILogEventSink
    {
        private readonly Uri _analyticsUrl;
        private readonly string _authenticationId;
        private readonly IFormatProvider _formatProvider;
        private readonly string _logName;
        private readonly bool _storeTimestampInUtc;
        private readonly string _workSpaceId;

        internal AzureLogAnalyticsSink(
            string workSpaceId,
            string authenticationId,
            string logName,
            bool storeTimestampInUtc,
            IFormatProvider formatProvider) : base(nThreads: 2)
        {
            _workSpaceId = workSpaceId;
            _authenticationId = authenticationId;
            _logName = logName;
            _storeTimestampInUtc = storeTimestampInUtc;
            _formatProvider = formatProvider;

            _analyticsUrl =
                new Uri("https://" + _workSpaceId + ".ods.opinsights.azure.com/api/logs?api-version=2016-04-01");
        }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion

        protected override void WriteLogEvent(ICollection<LogEvent> logEventsBatch)
        {
            if ((logEventsBatch == null) || (logEventsBatch.Count == 0))
                return;

            var logEventsJson = new StringBuilder();

            foreach (var logEvent in logEventsBatch)
            {
                var jsonString = JsonConvert.SerializeObject(
                    JObject.FromObject(
                            logEvent.Dictionary(
                                _storeTimestampInUtc,
                                _formatProvider))
                        .Flaten());

                logEventsJson.Append(jsonString);
                logEventsJson.Append(",");
            }

            if (logEventsJson.Length > 0)
                logEventsJson.Remove(logEventsJson.Length - 1, 1);

            if (logEventsBatch.Count > 1)
            {
                logEventsJson.Insert(0, "[");
                logEventsJson.Append("]");
            }

            var dateString = DateTime.UtcNow.ToString("r");
            var stringToHash = "POST\n" + logEventsJson.Length + "\napplication/json\n" + "x-ms-date:" + dateString +
                               "\n/api/logs";
            var hashedString = BuildSignature(stringToHash, _authenticationId);
            var signature = "SharedKey " + _workSpaceId + ":" + hashedString;

            PostData(signature, dateString, logEventsJson.ToString()).Wait();
        }

        private static string BuildSignature(string message, string secret)
        {
            var encoding = new ASCIIEncoding();
            var keyByte = Convert.FromBase64String(secret);
            var messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                var hash = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hash);
            }
        }

        private async Task PostData(string signature, string dateString, string jsonString)
        {
#if NETSTANDARD1_6
            using (var client = new HttpClient())
            {
                var headers = client.DefaultRequestHeaders;
                headers.Add("ContentType", "application/json");
                headers.Add("Log-Type", _logName);
                headers.Add("Authorization", signature);
                headers.Add("x-ms-date", dateString);
                headers.Add("time-generated-field", "Timestamp");
                await client.PostAsync(_analyticsUrl, new StringContent(jsonString, Encoding.UTF8, "application/json"));

            }
#else
            using (var client = new WebClient())
            {
                client.Headers.Add("ContentType", "application/json");
                client.Headers.Add("Log-Type", _logName);
                client.Headers.Add("Authorization", signature);
                client.Headers.Add("x-ms-date", dateString);
                client.Headers.Add("time-generated-field", "Timestamp");
                await client.UploadStringTaskAsync(_analyticsUrl, "POST", jsonString).ConfigureAwait(false);
            }
#endif
        }
    }
}