﻿// Copyright 2019 Zethian Inc.
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
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks;
using Serilog.Sinks.AzureAnalytics;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog
{
    /// <summary>
    ///     Adds the WriteTo.AzureLogAnalytics() extension method to <see cref="LoggerConfiguration" />.
    /// </summary>
    public static class LoggerConfigurationExtensions
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
        /// <param name="logBufferSize">Maximum number of log entries this sink can hold before stop accepting log messages. Supported size is between 5000 and 25000</param>
        /// <param name="batchSize">Number of log messages to be sent as batch. Supported range is between 1 and 1000</param>
        /// <param name="azureOfferingType">Azure offering type for public or government. Default is AzureOfferingType.Public</param>
        /// <param name="proxy">Use proxy to send data to Azure</param>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        [Obsolete(
            "This interface is obsolete and may get removed in future release. Please consider using AzureAnalytics",
            false)]
        public static LoggerConfiguration AzureLogAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            string logName = "DiagnosticsLog",
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            bool storeTimestampInUtc = true,
            IFormatProvider formatProvider = null,
            int? logBufferSize = ConfigurationSettings.DefaultBufferSize,
            int batchSize = ConfigurationSettings.DefaultBatchSize,
            AzureOfferingType azureOfferingType = AzureOfferingType.Public,
            string proxy = null)
        {
            if (string.IsNullOrEmpty(workspaceId))
                throw new ArgumentNullException(nameof(workspaceId));
            if (string.IsNullOrEmpty(authenticationId))
                throw new ArgumentNullException(nameof(authenticationId));

            var logAnalyticsSink = new AzureLogAnalyticsSink(
                    workspaceId,
                    authenticationId,
                    logName,
                    storeTimestampInUtc,
                    formatProvider,
                    logBufferSize,
                    batchSize,
                    azureOfferingType,
                    proxy: proxy);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = batchSize,
                Period = TimeSpan.FromSeconds(5),
                EagerlyEmitFirstEvent = true,
                QueueLimit = logBufferSize
            };

            return loggerConfiguration.Sink(new PeriodicBatchingSink(logAnalyticsSink, batchingOptions), 
                restrictedToMinimumLevel);
        }

        /// <summary>
        ///     Adds a sink that writes log events to Azure Log Analytics.
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
        /// <param name="logBufferSize">Maximum number of log entries this sink can hold before stop accepting log messages. Supported size is between 5000 and 25000</param>
        /// <param name="batchSize">Number of log messages to be sent as batch. Supported range is between 1 and 1000</param>
        /// <param name="azureOfferingType">Azure offering type for public or government. Default is AzureOfferingType.Public</param>
        /// <param name="levelSwitch">
        /// A switch allowing the pass-through minimum level to be changed at runtime.
        /// </param>
        /// <param name="flattenObject">Flat out complex object into simple object. All nested properties will move to root level with computed names</param>
        /// <param name="proxy">Use proxy to send data to Azure</param>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration AzureAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            string logName = "DiagnosticsLog",
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            bool storeTimestampInUtc = true,
            IFormatProvider formatProvider = null,
            int? logBufferSize = ConfigurationSettings.DefaultBufferSize,
            int batchSize = ConfigurationSettings.DefaultBatchSize,
            AzureOfferingType azureOfferingType = AzureOfferingType.Public,
            LoggingLevelSwitch levelSwitch = null,
            bool flattenObject = true,
            string proxy = null)
        {
            if (string.IsNullOrEmpty(workspaceId))
                throw new ArgumentNullException(nameof(workspaceId));
            if (string.IsNullOrEmpty(authenticationId))
                throw new ArgumentNullException(nameof(authenticationId));

            var logAnalyticsSink = new AzureLogAnalyticsSink(
                    workspaceId,
                    authenticationId,
                    logName,
                    storeTimestampInUtc,
                    formatProvider,
                    logBufferSize,
                    batchSize,
                    azureOfferingType,
                    flattenObject,
                    proxy);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = batchSize,
                Period = TimeSpan.FromSeconds(5),
                EagerlyEmitFirstEvent = true,
                QueueLimit = logBufferSize
            };

            return loggerConfiguration.Sink(new PeriodicBatchingSink(logAnalyticsSink, batchingOptions),
                restrictedToMinimumLevel,
                levelSwitch);
        }

        /// <summary>
        ///     Adds a sink that writes log events to Azure Log Analytics.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="workspaceId">Workspace Id from Azure OMS Portal connected sources.</param>
        /// <param name="authenticationId">
        ///     Primary or Secondary key from Azure OMS Portal connected sources.
        /// </param>
        /// <param name="loggerSettings">Optional configuration settings for logger</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="levelSwitch">
        /// A switch allowing the pass-through minimum level to be changed at runtime.
        /// </param>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration AzureAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            ConfigurationSettings loggerSettings,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch levelSwitch = null)
        {
            if (string.IsNullOrEmpty(workspaceId))
                throw new ArgumentNullException(nameof(workspaceId));
            if (string.IsNullOrEmpty(authenticationId))
                throw new ArgumentNullException(nameof(authenticationId));

            var logAnalyticsSink = new AzureLogAnalyticsSink(
                workspaceId,
                authenticationId,
                loggerSettings);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = loggerSettings?.BatchSize ?? ConfigurationSettings.DefaultBatchSize,
                Period = TimeSpan.FromSeconds(5),
                EagerlyEmitFirstEvent = true,
                QueueLimit = loggerSettings?.BufferSize
            };

            return loggerConfiguration.Sink(new PeriodicBatchingSink(logAnalyticsSink, batchingOptions),
                restrictedToMinimumLevel,
                levelSwitch);
        }
    }
}
