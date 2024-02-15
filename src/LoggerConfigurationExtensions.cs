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

using Serilog.Configuration;
using Serilog.Sinks;
using Serilog.Sinks.AzureLogAnalytics;

namespace Serilog
{
    /// <summary>
    ///     Adds the WriteTo.AzureLogAnalytics() extension method to <see cref="LoggerConfiguration" />.
    /// </summary>
    public static class LoggerConfigurationExtensions
    {
        public static LoggerConfiguration AzureLogAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            LoggerCredential credentials,
            ConfigurationSettings configSettings
        )
        {
            if (configSettings == null)
            {
                configSettings = new ConfigurationSettings();
            }
            return loggerConfiguration.Sink(
                new AzureLogAnalyticsSink(credentials, configSettings),
                restrictedToMinimumLevel: configSettings.MinLogLevel,
                levelSwitch: configSettings.LevelSwitch
            );
        }
    }
}
