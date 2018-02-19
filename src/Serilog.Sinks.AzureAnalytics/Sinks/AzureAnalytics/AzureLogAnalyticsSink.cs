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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Batch;
using Serilog.Sinks.Extensions;

namespace Serilog.Sinks
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
            IFormatProvider formatProvider,
            int logBufferSize = 25_000,
            int batchSize = 100,
            string urlSuffix = ".com"): base(batchSize, logBufferSize)
        {
            _workSpaceId = workSpaceId;
            _authenticationId = authenticationId;
            _logName = logName;
            _storeTimestampInUtc = storeTimestampInUtc;
            _formatProvider = formatProvider;

            _analyticsUrl =
                new Uri("https://" + _workSpaceId + ".ods.opinsights.azure" + urlSuffix + "/api/logs?api-version=2016-04-01");
        }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion

        protected override async Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch)
        {
            if ((logEventsBatch == null) || (logEventsBatch.Count == 0))
                return true;

            var logEventJsonBuilder = new StringBuilder();

            foreach (var logEvent in logEventsBatch)
            {
                var jsonString = JsonConvert.SerializeObject(
                    JObject.FromObject(
                            logEvent.Dictionary(
                                _storeTimestampInUtc,
                                _formatProvider))
                        .Flaten());

                logEventJsonBuilder.Append(jsonString);
                logEventJsonBuilder.Append(",");
            }

            if (logEventJsonBuilder.Length > 0)
                logEventJsonBuilder.Remove(logEventJsonBuilder.Length - 1, 1);

            if (logEventsBatch.Count > 1)
            {
                logEventJsonBuilder.Insert(0, "[");
                logEventJsonBuilder.Append("]");
            }

            var logEventJsonString = logEventJsonBuilder.ToString();
            var contentLength = Encoding.UTF8.GetByteCount(logEventJsonString);

            var dateString = DateTime.UtcNow.ToString("r");
            var hashedString = BuildSignature(contentLength, dateString, _authenticationId);
            var signature = "SharedKey " + _workSpaceId + ":" + hashedString;

            var result = await PostDataAsync(signature, dateString, logEventJsonString)
                .ConfigureAwait(true);
            return result == "OK";
        }

        private static string BuildSignature(int contentLength, string dateString, string key)
        {
            var stringToHash =
                "POST\n" +
                contentLength +
                "\napplication/json\n" +
                "x-ms-date:" + dateString +
                "\n/api/logs";

            var encoding = new UTF8Encoding();
            var keyByte = Convert.FromBase64String(key);
            var messageBytes = encoding.GetBytes(stringToHash);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                return Convert.ToBase64String(hmacsha256.ComputeHash(messageBytes));
            }
        }

        private async Task<string> PostDataAsync(string signature, string dateString, string jsonString)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", signature);
                    client.DefaultRequestHeaders.Add("x-ms-date", dateString);

                    var stringContent = new StringContent(jsonString);
                    stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                    stringContent.Headers.Add("Log-Type", _logName);
                    var response = client.PostAsync(_analyticsUrl, stringContent)
                        .Result;

                    var message = await response.Content.ReadAsStringAsync()
                        .ConfigureAwait(false);

                    SelfLog.WriteLine("{0}: {1}", response.ReasonPhrase, message);               
                    return response.ReasonPhrase;
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("ERROR: " + (ex.InnerException??ex).Message);
                return "FAILED";
            }
        }
    }
}