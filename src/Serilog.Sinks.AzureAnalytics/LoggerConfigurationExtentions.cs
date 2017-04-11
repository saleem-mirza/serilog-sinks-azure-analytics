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
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks;

namespace Serilog
{
    /// <summary>
    ///     Adds the WriteTo.AzureLogAnalytics() extension method to <see cref="LoggerConfiguration" />.
    /// </summary>
    public static class LoggerConfigurationExtentions
    {
        /// <summary>
        ///     Adds a sink that writes log events to a Azure Log Analytics.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="workspaceId">Workspace Id from Azure OMS Portal connected sources.</param>
        /// <param name="authenticationId">
        ///     Primary or Secondary key from Azure OMS Portal connected sources.
        /// </param>
        /// <param name="logName">A distinguishable log type name. Default is "DiagnosticsLog"</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="storeTimestampInUtc">Flag dictating if timestamp to be stored in UTC or local timezone format.</param>
        /// <param name="formatProvider">
        ///     Supplies an object that provides formatting information for formatting and parsing
        ///     operations
        /// </param>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration AzureLogAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            string logName = "DiagnosticsLog",
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            bool storeTimestampInUtc = true,
            IFormatProvider formatProvider = null)
        {
            if (string.IsNullOrEmpty(workspaceId)) throw new ArgumentNullException(nameof(workspaceId));
            if (string.IsNullOrEmpty(authenticationId)) throw new ArgumentNullException(nameof(authenticationId));
            return loggerConfiguration.Sink(
                new AzureLogAnalyticsSink(
                    workspaceId,
                    authenticationId,
                    logName,
                    storeTimestampInUtc,
                    formatProvider),
                restrictedToMinimumLevel);
        }
    }
}