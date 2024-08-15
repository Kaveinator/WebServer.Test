using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using WebServer;

namespace WebServer.Test {
    internal class Program {
        internal static void Main(string[] args) {
            Console.WriteLine("Hello, World!");
            var server = new HttpConfiguration() {
                DefaultDomain = "localhost",
                AutoStart = true,
                Port = 8080,
                DebugMode = true
            }.CreateServer(new HttpLogger());

            _ = Console.ReadLine();
        }
    }
    public class HttpLogger : IHttpLogger {
        public void Log(string message) => Console.WriteLine(message);

        public void LogError(HttpListenerContext context, List<MethodBase> methodsUsed, Exception exception)
            => Console.WriteLine(exception);

        public void LogRequest(HttpListenerContext context, HttpResponse response, List<MethodBase> methodsUsed)
            => Console.WriteLine($"[{(int)response.StatusCode}] {context.Request.Url?.PathAndQuery}");
    }
}