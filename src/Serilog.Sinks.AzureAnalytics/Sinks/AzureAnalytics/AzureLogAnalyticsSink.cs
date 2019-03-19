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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;
using Serilog.Sinks.Batch;
using Serilog.Sinks.Extensions;
using NamingStrategy = Serilog.Sinks.AzureAnalytics.NamingStrategy;

namespace Serilog.Sinks
{
    internal class AzureLogAnalyticsSink : BatchProvider, ILogEventSink
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly Uri _analyticsUrl;
        private readonly string _authenticationId;
        private readonly string _workSpaceId;
        private readonly JsonSerializer _jsonSerializer;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly ConfigurationSettings _configurationSettings;
        private static readonly HttpClient Client = new HttpClient();
        private const int MaximumMessageSize = 30_000_000;
        

        internal AzureLogAnalyticsSink(string workSpaceId, string authenticationId, ConfigurationSettings settings) :
            base(settings.BatchSize, settings.BufferSize)
        {
            _semaphore = new SemaphoreSlim(1, 1);

            _configurationSettings = settings;

            _workSpaceId         = workSpaceId;
            _authenticationId    = authenticationId;

            switch (settings.PropertyNamingStrategy) {
                case NamingStrategy.Default:
                    _jsonSerializerSettings = new JsonSerializerSettings
                    {
                        ContractResolver      = new DefaultContractResolver(),
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };
                    _jsonSerializer = new JsonSerializer
                    {
                        ContractResolver      = _jsonSerializerSettings.ContractResolver,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };

                    break;
                case NamingStrategy.CamelCase:
                    _jsonSerializer = new JsonSerializer()
                    {
                        ContractResolver      = new CamelCasePropertyNamesContractResolver(),
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };
                    _jsonSerializerSettings = new JsonSerializerSettings()
                    {
                        ContractResolver      = new CamelCasePropertyNamesContractResolver(),
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };

                    break;
                case NamingStrategy.Application:
                    _jsonSerializerSettings = JsonConvert.DefaultSettings()
                     ?? new JsonSerializerSettings
                        {
                            ContractResolver      = new DefaultContractResolver(),
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                        };
                    _jsonSerializer = new JsonSerializer
                    {
                        ContractResolver      = _jsonSerializerSettings.ContractResolver,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };

                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            _jsonSerializerSettings.Formatting = Newtonsoft.Json.Formatting.None;

            var urlSuffix = settings.AzureOfferingType == AzureOfferingType.US_Government ? ".us" : ".com";
            _analyticsUrl = new Uri(
                $"https://{_workSpaceId}.ods.opinsights.azure{urlSuffix}/api/logs?api-version=2016-04-01");
        }

        internal AzureLogAnalyticsSink(
            string workSpaceId,
            string authenticationId,
            string logName,
            bool storeTimestampInUtc,
            IFormatProvider formatProvider,
            int logBufferSize = 25_000,
            int batchSize = 100,
            AzureOfferingType azureOfferingType = AzureOfferingType.Public,
            bool flattenObjects = true) : this(
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
                Flatten = flattenObjects
            }) { }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion

        private int GetStringSizeInBytes(int stringLength)
        {
            return sizeof(char) * stringLength;
        }

        protected override async Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch)
        {
            if ((logEventsBatch == null) || (logEventsBatch.Count == 0))
                return true;

            var logEventJsonBuilder = new StringBuilder();
            var result = true;
            var counter = 0;

            foreach (var logEvent in logEventsBatch) {
                
                var eventObject = JObject.FromObject(
                    logEvent.Dictionary(
                        _configurationSettings.StoreTimestampInUtc,
                        _configurationSettings.FormatProvider),
                    _jsonSerializer).Flatten(_configurationSettings.Flatten);
                
                var jsonString = JsonConvert.SerializeObject( eventObject, _jsonSerializerSettings);
                
                if (GetStringSizeInBytes(jsonString.Length) >= MaximumMessageSize) {
                    if (counter > 0) {
                        counter--;
                    }

                    SelfLog.WriteLine("Log size is more than 30 MB. Consider sending smaller message");
                    SelfLog.WriteLine("Dropping invalid log message");
                    continue;
                }

                if (GetStringSizeInBytes(logEventJsonBuilder.Length + jsonString.Length) > MaximumMessageSize) {

                    SelfLog.WriteLine($"Sending mini batch of size {counter}");
                    result = await SendLogMessageAsync(logEventJsonBuilder).ConfigureAwait(false);
                    if (!result) {
                        return false;
                    }

                    counter = 0;
                    logEventJsonBuilder.Clear();
                }
                
                logEventJsonBuilder.Append(jsonString);
                logEventJsonBuilder.Append(",");
                counter++;
            }

            if (counter < logEventsBatch.Count) {
                SelfLog.WriteLine($"Sending mini batch of size {counter}");
            }

            return result && await SendLogMessageAsync(logEventJsonBuilder).ConfigureAwait(false);;
        }

        private async Task<bool> SendLogMessageAsync(StringBuilder logEventJsonBuilder)
        {
            if (logEventJsonBuilder.Length > 0)
                logEventJsonBuilder.Remove(logEventJsonBuilder.Length - 1, 1);

            if (logEventJsonBuilder.Length > 0) {
                logEventJsonBuilder.Insert(0, "[");
                logEventJsonBuilder.Append("]");


                var logEventJsonString = logEventJsonBuilder.ToString();
                var contentLength = Encoding.UTF8.GetByteCount(logEventJsonString);

                var dateString = DateTime.UtcNow.ToString("r");
                var hashedString = BuildSignature(contentLength, dateString, _authenticationId);
                var signature = $"SharedKey {_workSpaceId}:{hashedString}";

                var result = await PostDataAsync(signature, dateString, logEventJsonString).ConfigureAwait(false);

                return result;
            }

            return false;
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

        private async Task<bool> PostDataAsync(string signature, string dateString, string jsonString)
        {
            try {
                await _semaphore.WaitAsync().ConfigureAwait(false);

                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("Authorization", signature);
                Client.DefaultRequestHeaders.Add("x-ms-date", dateString);

                var stringContent = new StringContent(jsonString);
                stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                stringContent.Headers.Add("Log-Type", _configurationSettings.LogName);

                var response = Client.PostAsync(_analyticsUrl, stringContent).GetAwaiter().GetResult();
                var message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode) {
                    SelfLog.WriteLine("Transferring log: [{0}]", response.ReasonPhrase);
                }
                else {
                    SelfLog.WriteLine("Transferring log: [{0}] {1}", response.ReasonPhrase, message);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) {
                SelfLog.WriteLine("ERROR: " + (ex.InnerException ?? ex).Message);

                return false;
            }
            finally {
                _semaphore.Release();
            }
        }
    }
}
