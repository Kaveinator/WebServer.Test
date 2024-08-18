using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;

namespace WebServer.Extensions {
    public static class ExpandoObjectExt {
        public static object GetPropertyOrDefault(this ExpandoObject expando, string propertyName, object defaultValue) {
            if (expando is IDictionary<string, object> dict) {
                if (!dict.TryGetValue(propertyName, out var result))
                    dict.Add(propertyName, result = defaultValue);
                return result;
            }
            return defaultValue;
        }

        public static bool TryGetProperty(this ExpandoObject expando, string propertyName, out object value) {
            value = null;
            return expando is IDictionary<string, object> dict
                && dict.TryGetValue(propertyName, out value);
        }

    }
}
