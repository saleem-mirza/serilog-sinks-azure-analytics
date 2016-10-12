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

using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;

namespace Serilog
{
    public static class LoggerConfigurationExtentions
    {
        public static LoggerConfiguration AzureAnalytics(
            this LoggerSinkConfiguration loggerConfiguration,
            string workspaceId,
            string authenticationId,
            string logName = "DiagnosticsLog",
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum)
        {
            return loggerConfiguration.Sink(new AzureAnalyticsSink(workspaceId, authenticationId, logName), restrictedToMinimumLevel);
        }
    }
}
