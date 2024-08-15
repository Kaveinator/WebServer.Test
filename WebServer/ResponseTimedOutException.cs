using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace WebServer {
    public class ResponseTimedOutException : OperationCanceledException {
        public string FullPath;
        public List<MethodBase> MethodsUsed;
        public ResponseTimedOutException(string fullPath, List<MethodBase> methodsUsed, CancellationToken token)
        : base("The request took too long to fulfil", token) {
            FullPath = fullPath;
            MethodsUsed = methodsUsed;
        }

        public override string ToString()
        {
            return base.ToString()
              + $"\nFullPath: '{FullPath}';"
              + $"\nMethods Used: [\n{string.Join(",\n", MethodsUsed.Select(m => $"  '{m.ReflectedType.FullName}.{m.Name}'"))} \n];";
        }
    }
}
