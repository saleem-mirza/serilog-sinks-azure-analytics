// Copyright 2019 Zethian Inc.
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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;
using Serilog.Sinks.Extensions;
using Serilog.Sinks.PeriodicBatching;
using NamingStrategy = Serilog.Sinks.AzureAnalytics.NamingStrategy;

namespace Serilog.Sinks
{
    internal class AzureLogAnalyticsSink : IBatchedLogEventSink
    {
        private readonly Uri _analyticsUrl;
        private readonly string _authenticationId;
        private readonly string _workSpaceId;
        private readonly JsonSerializer _jsonSerializer;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly ConfigurationSettings _configurationSettings;
        private static readonly HttpClientHandler ClientHandler = new HttpClientHandler();
        private static readonly HttpClient Client = new HttpClient(ClientHandler);
        private const int MaximumMessageSize = 30_000_000;
        private const int TargetMessageSize = 1_000_000;

        internal AzureLogAnalyticsSink(string workSpaceId, string authenticationId, ConfigurationSettings settings) 
        {
            _configurationSettings = settings;

            _workSpaceId      = workSpaceId;
            _authenticationId = authenticationId;
            
            if (!string.IsNullOrEmpty(settings.Proxy)) {
                ClientHandler.Proxy = new WebProxy(settings.Proxy);
                ClientHandler.UseProxy = true;
            }

            switch (settings.PropertyNamingStrategy) {
                case NamingStrategy.Default:
                    _jsonSerializerSettings = new JsonSerializerSettings
                    {
                        ContractResolver      = new DefaultContractResolver()
                    };

                    break;
                case NamingStrategy.CamelCase:
                    _jsonSerializerSettings = new JsonSerializerSettings()
                    {
                        ContractResolver      = new CamelCasePropertyNamesContractResolver()
                    };

                    break;
                case NamingStrategy.Application:
                    _jsonSerializerSettings = JsonConvert.DefaultSettings()
                     ?? new JsonSerializerSettings
                        {
                            ContractResolver      = new DefaultContractResolver()
                        };


                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            _jsonSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            _jsonSerializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
            _jsonSerializerSettings.Formatting = Newtonsoft.Json.Formatting.None;

            _jsonSerializer = new JsonSerializer {
                ContractResolver = _jsonSerializerSettings.ContractResolver,
                ReferenceLoopHandling = _jsonSerializerSettings.ReferenceLoopHandling,
                PreserveReferencesHandling = _jsonSerializerSettings.PreserveReferencesHandling
            };
            _analyticsUrl = GetServiceEndpoint(settings.AzureOfferingType, _workSpaceId);
        }

        private static Uri GetServiceEndpoint(AzureOfferingType azureOfferingType, string workspaceId)
        {
            string offeringDomain;
            switch (azureOfferingType) {
                case AzureOfferingType.Public:
                    offeringDomain = "azure.com";
                    break;
                case AzureOfferingType.US_Government:
                    offeringDomain = "azure.us";
                    break;
                case AzureOfferingType.China:
                    offeringDomain = "azure.cn";
                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            return new Uri(
                $"https://{workspaceId}.ods.opinsights.{offeringDomain}/api/logs?api-version=2016-04-01");
        }

        internal AzureLogAnalyticsSink(
            string workSpaceId,
            string authenticationId,
            string logName,
            bool storeTimestampInUtc,
            IFormatProvider formatProvider,
            int? logBufferSize,
            int batchSize,
            AzureOfferingType azureOfferingType = AzureOfferingType.Public,
            bool flattenObject = true,
            string proxy = null) : this(
            workSpaceId,
            authenticationId,
            new ConfigurationSettings
            {
                FormatProvider         = formatProvider,
                StoreTimestampInUtc    = storeTimestampInUtc,
                AzureOfferingType      = azureOfferingType,
                BufferSize             = logBufferSize,
                BatchSize              = batchSize,
                LogName                = logName,
                PropertyNamingStrategy = NamingStrategy.Default,
                Flatten                = flattenObject,
                Proxy                  = proxy
            }) { }

        private int GetStringSizeInBytes(int stringLength)
        {
            return sizeof(char) * stringLength;
        }

        private static string BuildSignature(int contentLength, string dateString, string key)
        {
            var stringToHash =
                "POST\n" + contentLength + "\napplication/json\n" + "x-ms-date:" + dateString + "\n/api/logs";

            var encoding = new UTF8Encoding();
            var keyByte = Convert.FromBase64String(key);
            var messageBytes = encoding.GetBytes(stringToHash);
            using (var hmacsha256 = new HMACSHA256(keyByte)) {
                return Convert.ToBase64String(hmacsha256.ComputeHash(messageBytes));
            }
        }

        /// <summary>
        /// Send the data to Azure Log Analytics
        /// </summary>
        /// <param name="jsonStringCollection"></param>
        /// <remarks>There is no retry action - it is presumed that is handled higher up.</remarks>
        private async Task PostDataAsync(List<string> jsonStringCollection)
        {
            var logEventJsonString = $"[{string.Join(",", jsonStringCollection.ToArray())}]";
            var contentLength = Encoding.UTF8.GetByteCount(logEventJsonString);

            var dateString = DateTime.UtcNow.ToString("r");
            var hashedString = BuildSignature(contentLength, dateString, _authenticationId);
            var signature = $"SharedKey {_workSpaceId}:{hashedString}";

            var stringContent = new StringContent(logEventJsonString);
            stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            stringContent.Headers.Add("Log-Type", _configurationSettings.LogName);

            using (var request = new HttpRequestMessage(HttpMethod.Post, _analyticsUrl))
            {
                request.Content = stringContent;
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(signature);
                request.Headers.Add("x-ms-date", dateString);

                var response = await Client.SendAsync(request).ConfigureAwait(false);
                var message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    SelfLog.WriteLine("Transferring log: [{0}]", response.ReasonPhrase);
                }
                else
                {
                    SelfLog.WriteLine("Transferring log: [{0}] {1}", response.ReasonPhrase, message);
                }

                //throw an exception on failure.
                response.EnsureSuccessStatusCode();
            }
        }

        /// <inheritdoc />
        public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            var jsonStringCollection = new List<string>();
            var jsonStringCollectionSize = 0;

            var postTasks = new List<Task>();

            foreach (var logEvent in batch)
            {
                var eventObject = JObject.FromObject(
                        logEvent.Dictionary(
                            _configurationSettings.StoreTimestampInUtc,
                            _configurationSettings.FormatProvider),
                        _jsonSerializer)
                    .Flatten(_configurationSettings.Flatten);

                var jsonString = JsonConvert.SerializeObject(eventObject, _jsonSerializerSettings);

                //Protect against a single, over-sized message we can't write out.
                var messageSize = GetStringSizeInBytes(jsonString.Length);
                if (messageSize >= MaximumMessageSize)
                {
                    SelfLog.WriteLine("Log size is more than 30 MB. Consider sending smaller message");
                    SelfLog.WriteLine("Dropping invalid log message");

                    continue;
                }

                //Now see if we've got a large enough message we really needs to send now...
                if ((jsonStringCollectionSize + messageSize) > TargetMessageSize)
                {
                    SelfLog.WriteLine($"Sending batch of log messages to Log Analytics of size {jsonStringCollectionSize}");
                    postTasks.Add(PostDataAsync(jsonStringCollection));

                    //and reset our collection since we've sent them.
                    jsonStringCollection = new List<string> ();
                    jsonStringCollectionSize = 0;
                }

                //adding this message won't exceed our buffer size so lets buffer it and keep going.
                jsonStringCollection.Add(jsonString);
                jsonStringCollectionSize += messageSize;
            }

            if (jsonStringCollection.Any())
            {
                SelfLog.WriteLine($"Sending batch of log messages to Log Analytics of size {jsonStringCollectionSize}");
                postTasks.Add(PostDataAsync(jsonStringCollection));
            }

            //now we have to wait for all of our tasks to complete before we return for correctness 
            if (postTasks.Any())
            {
                await Task.WhenAll(postTasks.ToArray()).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task OnEmptyBatchAsync()
        {
        }
    }
}
