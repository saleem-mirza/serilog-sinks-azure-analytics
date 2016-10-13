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

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Serilog.Sinks.AzureAnalytics.Extensions
{
    internal static class JsonExtensions
    {
        internal static IDictionary<string, object> Flaten(this JArray jsonArray)
        {
            if (jsonArray == null)
                return null;

            var dict = new Dictionary<string, object>();
            FlatenJToken(dict, jsonArray, string.Empty);

            return dict;
        }

        internal static IDictionary<string, object> Flaten(this JObject jsonObject)
        {
            if (jsonObject == null)
                return null;

            var dict = new Dictionary<string, object>();
            FlatenJToken(dict, jsonObject, string.Empty);

            return dict;
        }

        private static string Join(string prefix, string name)
        {
            return (string.IsNullOrEmpty(prefix) ? name : prefix + "_" + name).Trim();
        }

        private static void FlatenJToken(IDictionary<string, object> dict, JToken token, string prefix)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in token.Children<JProperty>())
                        FlatenJToken(dict, prop.Value, Join(prefix, prop.Name));
                    break;
                case JTokenType.Array:
                    var index = 0;
                    foreach (var value in token.Children())
                    {
                        FlatenJToken(dict, value, Join(prefix, index.ToString()));
                        index++;
                    }
                    break;
                default:
                    dict.Add(prefix, ((JValue) token).Value);
                    break;
            }
        }
    }
}