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

using Azure.Core;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.AzureLogAnalytics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Serilog.Formatting;

namespace Serilog.Sinks
{
    internal class AzureLogAnalyticsSink : IBatchedLogEventSink
    {
        private string token;
        private DateTimeOffset expire_on = DateTimeOffset.MinValue;
        private readonly string LoggerUriString;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly LoggerCredential _loggerCredential;
        private static readonly HttpClient httpClient = new HttpClient();

        const string scope = "https://monitor.azure.com//.default";

        internal AzureLogAnalyticsSink(LoggerCredential loggerCredential, ConfigurationSettings settings, ITextFormatter formatter)
        {
            _loggerCredential = loggerCredential;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = settings.PropertyNamingStrategy == NamingStrategy.CamelCase
                    ? JsonNamingPolicy.CamelCase
                    : null,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = false,
            };
            _jsonOptions.Converters.Add(new LoggerJsonConverter(formatter));

            LoggerUriString = $"{_loggerCredential.Endpoint}/dataCollectionRules/{_loggerCredential.ImmutableId}/streams/{_loggerCredential.StreamName}?api-version=2023-01-01";
        }

        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        public Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            if ((batch == null) || (batch.Count == 0))
                return Task.CompletedTask;

            var logs = batch.Select(s => new Dictionary<string, object>
            {
                ["TimeGenerated"] = DateTime.UtcNow,
                ["Event"] = s,
                ["Message"] = s.RenderMessage()
            });

            return PostDataAsync(logs);
        }

        private async Task<(string, DateTimeOffset)> GetAuthToken()
        {
            if (_loggerCredential.TokenCredential != null)
            {
                var tokenContext = new TokenRequestContext(new[] { scope });
                var access_token = await _loggerCredential.TokenCredential.GetTokenAsync(tokenContext, default);
                return (access_token.Token, access_token.ExpiresOn);
            }

            var uri = $"https://login.microsoftonline.com/{_loggerCredential.TenantId}/oauth2/v2.0/token";

            var content = new FormUrlEncodedContent(new[]{
                    new KeyValuePair<string, string>("client_id",_loggerCredential.ClientId),
                    new KeyValuePair<string, string>("scope", scope),
                    new KeyValuePair<string, string>("client_secret", _loggerCredential.ClientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

            var response = await httpClient.PostAsync(uri, content);
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

        // Exceptions propagate: Serilog's batching sink owns retry, backoff, and SelfLog
        // diagnostics for failed batches.
        private async Task PostDataAsync(IEnumerable<IDictionary<string, object>> logs)
        {
            if (expire_on <= DateTimeOffset.Now)
            {
                (token, expire_on) = await GetAuthToken();
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException(
                        "Invalid or expired authentication token. Validate credentials and try again.");
                }

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var jsonString = JsonSerializer.Serialize(logs, _jsonOptions);
            var jsonContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(LoggerUriString, jsonContent);
            response.EnsureSuccessStatusCode();
        }
    }
}
