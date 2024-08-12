using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WebServer {
    public class HttpServer {
        public readonly HttpConfiguration Config;
        public readonly DirectoryInfo ViewsDirectory;
        public ushort ActivePort { get; protected set; }
        public HttpListener HttpListener { get; protected set; }
        public IHttpLogger Logger;

        Dictionary<string, Dictionary<Regex, Func<HttpListenerRequest, Task<HttpResponse?>>>> HttpCallbacks = new Dictionary<string, Dictionary<Regex, Func<HttpListenerRequest, Task<HttpResponse?>>>>();
        /* Instead of three directories (from previous render engine)
         *  - Public/PublicTemplates (was public static stuff, also allowed views)
         *  - Static (was used for builds)
         *  - Views/PrivateTemplates (was used for private views)
         * There will be only two:
         *  - Static - For builds, there should also be a build process for this
         *  - Views - Site source, including public/private views (views are able to be processed)
         */

        public HttpServer(HttpConfiguration config, IHttpLogger logger = null) {
            Config = config;
            Logger = logger;
            ViewsDirectory = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Views"));
            if (!ViewsDirectory.Exists)
                ViewsDirectory.Create();
            if (!string.IsNullOrEmpty(config.DefaultDomain))
                ViewsDirectory.CreateSubdirectory(config.DefaultDomain);
            if (config.AutoStart)
                _ = StartAsync();
        }

        public async Task StartAsync() {
            if (HttpListener?.IsListening == true)
                await StopAsync();
            HttpListener = new HttpListener() {
                IgnoreWriteExceptions = true // Used to crash the server, don't think it is needed anymore
            };
            HttpListener.Prefixes.Add($"http://+:{Config.Port}/");

            HttpListener.Start();
            ActivePort = Config.Port;
            Logger?.Log($"HTTP Service started on port '{ActivePort}'");

            while (HttpListener.IsListening)
                await ListenAsync((ListenToken = new CancellationTokenSource()).Token);
        }

        CancellationTokenSource ListenToken;
        public int TasksCount => Tasks.Count;
        HashSet<Task> Tasks = new HashSet<Task>();
        async Task ListenAsync(CancellationToken token) {
            Tasks = new HashSet<Task>(64);
            for (int i = 0; i < Tasks.Count; i++) // Create 64 tasks
                Tasks.Add(HttpListener.GetContextAsync());
            Logger.Log($"Listening with {Tasks.Count} worker(s)");
            while (!token.IsCancellationRequested) {
                Task t = await Task.WhenAny(Tasks);
                Tasks.Remove(t);

                if (t is Task<HttpListenerContext> context) {
                    if (Tasks.Count < Config.MaxConcurrentRequests)
                        Tasks.Add(HttpListener.GetContextAsync());
                    Tasks.Add(ProcessRequestAsync(context.Result, token));
                }
                else Logger.Log($"Got an unexpected task of type '{t.GetType().FullName}'");
            }
        }

        public async Task StopAsync() {
            HttpListener.Stop();
            ListenToken.Cancel();
        }

        public async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken token) { }
    }
}
