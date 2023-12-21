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
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Newtonsoft.Json;
using Serilog.Events;

namespace Serilog.Sinks.Extensions
{
    internal static class LogEventExtensions
    {
        internal static string Json(this LogEvent logEvent, bool storeTimestampInUtc = false)
        {
            return JsonConvert.SerializeObject(ConvertToDictionary(logEvent, storeTimestampInUtc), new JsonSerializerSettings {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        internal static IDictionary<string, object> Dictionary(
            this LogEvent logEvent,
            bool storeTimestampInUtc = false,
            IFormatProvider formatProvider = null,
            bool preserveLogProperties = false,
            bool simplifyLogColumnNames = false)
        {
            return ConvertToDictionary(logEvent, storeTimestampInUtc, formatProvider, preserveLogProperties, simplifyLogColumnNames);
        }


        internal static string Json(this IReadOnlyDictionary<string, LogEventPropertyValue> properties)
        {
            return JsonConvert.SerializeObject(ConvertToDictionary(properties), new JsonSerializerSettings {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        internal static IDictionary<string, object> Dictionary(
            this IReadOnlyDictionary<string, LogEventPropertyValue> properties)
        {
            return ConvertToDictionary(properties);
        }

        #region Private implementation

        private static dynamic ConvertToDictionary(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
        {
            var expObject = new ExpandoObject() as IDictionary<string, object>;
            foreach (var property in properties)
                expObject.Add(property.Key, Simplify(property.Value));

            return expObject;
        }

        private static dynamic ConvertToDictionary(
            LogEvent logEvent,
            bool storeTimestampInUtc,
            IFormatProvider formatProvider = null,
            bool preserveLogProperties = false,
            bool simplifyLogColumnNames = false)
        {
            var eventObject = new ExpandoObject() as IDictionary<string, object>;
            eventObject.Add(
                !simplifyLogColumnNames ? "Timestamp" : "TimeGenerated",
                storeTimestampInUtc
                    ? logEvent.Timestamp.ToUniversalTime().ToString("o")
                    : logEvent.Timestamp.ToString("o"));
            eventObject.Add(!simplifyLogColumnNames ? "LogLevel" : nameof(logEvent.Level), logEvent.Level.ToString());
            eventObject.Add(!simplifyLogColumnNames ? "LogMessageTemplate" : nameof(logEvent.MessageTemplate), logEvent.MessageTemplate.Text);
            eventObject.Add(!simplifyLogColumnNames ? "LogMessage" : "Message", logEvent.RenderMessage(formatProvider));
            eventObject.Add(!simplifyLogColumnNames ? "LogException" : nameof(logEvent.Exception), logEvent.Exception);
            if (preserveLogProperties)
            {
                foreach (var property in logEvent.Properties)
                {
                    eventObject.Add(property.Key, Simplify(property.Value));
                }
            }
            else
            {
                eventObject.Add("LogProperties", logEvent.Properties.Dictionary());
            }

            return eventObject;
        }

        private static object Simplify(LogEventPropertyValue data)
        {
            if (data is ScalarValue value) {
                return value.Value;
            }

            // ReSharper disable once SuspiciousTypeConversion.Global
            if (data is DictionaryValue dictValue) {
                var expObject = new ExpandoObject() as IDictionary<string, object>;
                foreach (var item in dictValue.Elements) {
                    if (item.Key.Value is string key)
                        expObject.Add(key, Simplify(item.Value));
                }

                return expObject;
            }

            if (data is SequenceValue seq)
                return seq.Elements.Select(Simplify).ToArray();

            if (!(data is StructureValue str))
                return null;

            {
                try {
                    if (str.TypeTag == null)
                        return str.Properties.ToDictionary(p => p.Name, p => Simplify(p.Value));

                    if (!str.TypeTag.StartsWith("DictionaryEntry") && !str.TypeTag.StartsWith("KeyValuePair"))
                        return str.Properties.ToDictionary(p => p.Name, p => Simplify(p.Value));

                    var key = Simplify(str.Properties[0].Value);

                    if (key == null)
                        return null;

                    var expObject = new ExpandoObject() as IDictionary<string, object>;
                    expObject.Add(key.ToString(), Simplify(str.Properties[1].Value));

                    return expObject;
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }
            }

            return null;
        }

        #endregion
    }
}
