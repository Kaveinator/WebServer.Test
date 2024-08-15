using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;

namespace WebServer {
    public interface IHttpLogger {
        void Log(string message);
        void LogRequest(HttpListenerContext context, HttpResponse response, List<MethodBase> methodsUsed);
        void LogError(HttpListenerContext context, List<MethodBase> methodsUsed, Exception exception);
    }
}
