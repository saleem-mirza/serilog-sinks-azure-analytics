// Copyright 2018 Zethian Inc.
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

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Serilog.Sinks.Extensions
{
    internal static class JsonExtensions
    {
        private const string LogPropertyName = "LogProperties";

        internal static JObject Flatten(this JObject jsonObject, bool flatObject = true)
        {
            if (jsonObject == null)
                return null;

            if (flatObject) {
                return jsonObject;
            }

            var logPropToken = jsonObject.GetValue(LogPropertyName);
            jsonObject.Remove(LogPropertyName);

            jsonObject.Add(LogPropertyName, logPropToken.ToString(Newtonsoft.Json.Formatting.None, null));

            return jsonObject;
            

            // var dict = new Dictionary<string, object>();
            // FlattenJToken(dict, jsonObject, string.Empty);
            //
            // return JObject.FromObject(dict);
        }

        private static string Join(string prefix, string name)
        {
            return (string.IsNullOrEmpty(prefix) ? name : prefix + "_" + name).Trim();
        }

        private static void FlattenJToken(IDictionary<string, object> dict, JToken token, string prefix)
        {
            switch (token.Type) {
                case JTokenType.Object:
                    foreach (var prop in token.Children<JProperty>())
                        FlattenJToken(dict, prop.Value, Join(prefix, prop.Name));

                    break;
                case JTokenType.Array:
                    dict.Add(prefix, token);

                    break;
                default:
                    dict.Add(prefix, ((JValue) token).Value);

                    break;
            }
        }
    }
}
