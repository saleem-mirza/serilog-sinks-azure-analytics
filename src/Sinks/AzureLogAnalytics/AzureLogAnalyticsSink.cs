// Copyright 2025 Zethian Inc.
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
using System.Dynamic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.AzureLogAnalytics;
using Serilog.Sinks.Batch;
using NamingStrategy = Serilog.Sinks.AzureLogAnalytics.NamingStrategy;

namespace Serilog.Sinks
{
    internal class AzureLogAnalyticsSink : BatchProvider, ILogEventSink
    {
        private string token;
        private DateTimeOffset expire_on = DateTimeOffset.MinValue;
        private readonly string LoggerUriString;
        private readonly SemaphoreSlim _semaphore;
        private readonly LogsJsonSerializerContext _logsJsonSerializerContext;
        private readonly ConfigurationSettings _configurationSettings;
        private readonly LoggerCredential _loggerCredential;
        private static readonly HttpClient httpClient = new HttpClient();

        private const string scope = "https://monitor.azure.com//.default";

        internal AzureLogAnalyticsSink(LoggerCredential loggerCredential, ConfigurationSettings settings, ITextFormatter formatter) :
            base(settings.BatchSize, settings.BufferSize)
        {
            _semaphore = new SemaphoreSlim(1, 1);

            _loggerCredential = loggerCredential;

            _configurationSettings = settings;

            JsonSerializerOptions jsonOptions;
            switch (settings.PropertyNamingStrategy)
            {
                case NamingStrategy.Default:
                    jsonOptions = new JsonSerializerOptions();

                    break;

                case NamingStrategy.CamelCase:
                    jsonOptions = new JsonSerializerOptions()
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    };

                    break;

                default: throw new ArgumentOutOfRangeException();
            }

            jsonOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            jsonOptions.WriteIndented = false;
            jsonOptions.Converters.Add(new LoggerJsonConverter(formatter));
            _logsJsonSerializerContext = new LogsJsonSerializerContext(jsonOptions);
            if (_configurationSettings.MaxDepth > 0)
            {
                _configurationSettings.MaxDepth = _configurationSettings.MaxDepth;
            }

            LoggerUriString = $"{_loggerCredential.Endpoint}/dataCollectionRules/{_loggerCredential.ImmutableId}/streams/{_loggerCredential.StreamName}?api-version=2023-01-01";
        }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion ILogEvent implementation

        protected override async Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch)
        {
            if ((logEventsBatch == null) || (logEventsBatch.Count == 0))
                return true;

            var jsonStringCollection = new List<string>();

            var logs = logEventsBatch.Select(s =>
                {
                    var obj = new ExpandoObject() as IDictionary<string, object>;
                    obj.Add("TimeGenerated", DateTime.UtcNow);
                    obj.Add("Event", s);
                    obj.Add("Message", s.RenderMessage());
                    return obj;
                });

            return await PostDataAsync(logs);
        }

        private async Task<(string, DateTimeOffset)> GetAuthToken()
        {
            if (_loggerCredential.TokenCredential != null)
            {
                var tokenContext = new TokenRequestContext(new String[] { scope });
                var cancellationToken = new CancellationToken();
                var access_token = await _loggerCredential.TokenCredential.GetTokenAsync(tokenContext, cancellationToken);
                return (access_token.Token, access_token.ExpiresOn);
            }

            var uri = $"https://login.microsoftonline.com/{_loggerCredential.TenantId}/oauth2/v2.0/token";

            var content = new FormUrlEncodedContent(new[]{
                    new KeyValuePair<string, string>("client_id",_loggerCredential.ClientId),
                    new KeyValuePair<string, string>("scope", scope),
                    new KeyValuePair<string, string>("client_secret", _loggerCredential.ClientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

            var httpClient = new HttpClient();
            var response = httpClient.PostAsync(uri, content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                SelfLog.WriteLine(response.ReasonPhrase);
                return (string.Empty, DateTimeOffset.MinValue);
            }

            var responseObject = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (responseObject == null)
            {
                SelfLog.WriteLine("Invalid response");
                return (string.Empty, DateTimeOffset.MinValue);
            }

            try
            {
                return (
                    responseObject.RootElement.GetProperty("access_token").GetString(),
                    DateTimeOffset.Now.AddSeconds(responseObject.RootElement.GetProperty("expires_in").GetInt32())
                );
            }
            catch (System.Exception)
            {
                return (string.Empty, DateTimeOffset.MinValue);
            }
        }

        private async Task<bool> PostDataAsync(IEnumerable<IDictionary<string, object>> logs)
        {
            try
            {
                await _semaphore.WaitAsync();

                if (expire_on <= DateTimeOffset.Now)
                {
                    (token, expire_on) = await GetAuthToken();
                    if (string.IsNullOrEmpty(token))
                    {
                        SelfLog.WriteLine("Invalid or expired authentication token. Validate credentials and try again.");
                        return false;
                    }

                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{token}");
                }

                var jsonString = JsonSerializer.Serialize(logs, typeof(IEnumerable<IDictionary<string, object>>), _logsJsonSerializerContext);
                var jsonContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = httpClient.PostAsync(LoggerUriString, jsonContent).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    SelfLog.WriteLine(response.ReasonPhrase);
                    return false;
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}