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
using Azure.Identity;
using Serilog.Core;
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
        private readonly string LoggerUriString;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly TokenCredential _tokenCredential;
        private static readonly HttpClient httpClient = new HttpClient();

        // The doubled slash is required by Azure Monitor. It is not a typo.
        private static readonly string[] scopes = { "https://monitor.azure.com//.default" };

        internal AzureLogAnalyticsSink(LoggerCredential loggerCredential, ConfigurationSettings settings, ITextFormatter formatter)
        {
            _tokenCredential = loggerCredential.TokenCredential ?? new ClientSecretCredential(
                loggerCredential.TenantId,
                loggerCredential.ClientId,
                loggerCredential.ClientSecret);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = settings.PropertyNamingStrategy == NamingStrategy.CamelCase
                    ? JsonNamingPolicy.CamelCase
                    : null,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = false,
            };
            _jsonOptions.Converters.Add(new LoggerJsonConverter(formatter));

            LoggerUriString = $"{loggerCredential.Endpoint}/dataCollectionRules/{loggerCredential.ImmutableId}/streams/{loggerCredential.StreamName}?api-version=2023-01-01";
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

        // Exceptions propagate: Serilog's batching sink owns retry, backoff, and SelfLog
        // diagnostics for failed batches. TokenCredential implementations cache and refresh
        // the token themselves, so there is no token cache here.
        private async Task PostDataAsync(IEnumerable<IDictionary<string, object>> logs)
        {
            var accessToken = await _tokenCredential.GetTokenAsync(new TokenRequestContext(scopes), default);

            var jsonString = JsonSerializer.Serialize(logs, _jsonOptions);

            var request = new HttpRequestMessage(HttpMethod.Post, LoggerUriString)
            {
                Content = new StringContent(jsonString, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }
}
