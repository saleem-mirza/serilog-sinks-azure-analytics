using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Serilog.Extensions
{
    public static class JsonExtensions
    {
        public static IDictionary<string, object> Flaten(this JArray jsonArray)
        {
            if (jsonArray == null)
            {
                return null;
            }

            var dict = new Dictionary<string, object>();
            FlatenJToken(dict, jsonArray, string.Empty);

            return dict;
        }

        public static IDictionary<string, object> Flaten(this JObject jsonObject)
        {
            if (jsonObject == null)
            {
                return null;
            }

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
                    {
                        FlatenJToken(dict, prop.Value, Join(prefix, prop.Name));
                    }
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