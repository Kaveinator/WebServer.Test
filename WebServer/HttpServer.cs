using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RazorLight;
using RazorLight.Razor;
using WebServer.Models;
using WebServer.Utils;

namespace WebServer {
    public class HttpServer {
        public readonly HttpConfiguration Config;
        public readonly DirectoryInfo ViewsDirectory;
        public ushort ActivePort { get; protected set; }
        public HttpListener HttpListener { get; protected set; }
        public IHttpLogger? Logger;
        public delegate Task<HttpResponse?> Callback(HttpListenerContext context, CachedResponse? cache);

        Dictionary<string, Dictionary<Regex, Callback>> HttpCallbacks = new Dictionary<string, Dictionary<Regex, Callback>>();
        public List<AreaBase> RegisteredAreas = new List<AreaBase>();
        Dictionary<string, RazorLightEngine> RazorEngines = new Dictionary<string, RazorLightEngine>();
        public readonly RazorLightEngine DefaultRazorEngine;
        /* Instead of three directories (from previous render engine)
         *  - Public/PublicTemplates (was public static stuff, also allowed views)
         *  - Static (was used for builds)
         *  - Views/PrivateTemplates (was used for private views)
         * There will be only two:
         *  - Static - For builds, there should also be a build process for this
         *  - Views - Site source, including public/private views (views are able to be processed)
         */

        public HttpServer(HttpConfiguration config, IHttpLogger? logger = null) {
            Config = config;
            Logger = logger;
            ViewsDirectory = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Views"));
            if (!ViewsDirectory.Exists)
                ViewsDirectory.Create();
            if (!string.IsNullOrEmpty(config.DefaultDomain))
                ViewsDirectory.CreateSubdirectory(config.DefaultDomain);
            if (config.AutoStart)
                _ = StartAsync();
            DefaultRazorEngine = GetOrCreateRazorEngine(config.DefaultDomain);
        }

        public RazorLightEngine GetOrCreateRazorEngine(string hostname) {
            hostname = hostname.Trim(' ', '/', '\\');
            if (!RazorEngines.TryGetValue(hostname, out RazorLightEngine engine)) {
                RazorEngines.Add(hostname, engine = new RazorLightEngineBuilder()
                    .UseOptions(new RazorLightOptions() { // TODO: make this part of the config
                        EnableDebugMode = true
                    })
                    .UseProject(new FileSystemRazorProject(Path.Combine(ViewsDirectory.FullName, hostname), ".cshtml"))
                    .UseMemoryCachingProvider()
                    .Build()
                );
            }
            return engine;
        }

        public async Task StartAsync() {
            if (HttpListener?.IsListening ?? false)
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
            Tasks = new HashSet<Task>(128);
            for (int i = 0; i < 64; i++) // Create 64 tasks
                Tasks.Add(HttpListener.GetContextAsync());
            Logger?.Log($"Listening with {Tasks.Count} worker(s)");
            while (!token.IsCancellationRequested) {
                Task t = await Task.WhenAny(Tasks);
                Tasks.Remove(t);

                if (t is Task<HttpListenerContext> context) {
                    if (Tasks.Count < Config.MaxConcurrentRequests)
                        Tasks.Add(HttpListener.GetContextAsync());
                    Tasks.Add(ProcessRequestAsync(context.Result, token)); // Should I really be adding this to tasks?
                }
                //ProcessRequestAsync triggers this:
                // else Logger?.Log($"Got an unexpected task of type '{t.GetType().FullName}'");
            }
        }

        public async Task StopAsync() {
            HttpListener.Stop();
            ListenToken.Cancel();
        }

        public async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken token) {
            List<MethodBase> methodsUsed = new List<MethodBase>() { MethodBase.GetCurrentMethod() };
            try {
                // Add generic headers
                foreach (var kvp in Config.GenericHeaders) {
                    if (kvp.Value is null)
                        context.Response.Headers.Add(kvp.Key);
                    else context.Response.Headers.Add(kvp.Key, kvp.Value);
                }

                // Get domain and path
                string host = (context.Request.Url?.Host ?? Config.DefaultDomain).Trim('/', ' ').ToLowerInvariant(),
                       callbackKey = FormatCallbackKey(context.Request.Url?.LocalPath ?? string.Empty),
                       path = callbackKey.Any() ? $"/{callbackKey}" : "";
                string cacheName = $"{host}{path}";
                CachedResponse? cache = CachedResponse.Get(this, cacheName);
                HttpResponse? response = null;
                if (cache != null) {
                    if (!cache.NeedsUpdate)
                        response = cache;
                    else if (cache.UpdateMethod != null)
                        response = await cache.UpdateMethod(context, cache);
                }

                // Create reponse timeout logic, this will return a string to the client but an exception on the server
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                Task cancelationTask = Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
                if (Config.ResponseTimeout > 0)
                    cancellationTokenSource.CancelAfter(1000 * Config.ResponseTimeout);

                StatusPageModel? statusPageModel = null;
                try {
                    #region Event Callbacks
                    var hostnames = new[] { host ?? Config.DefaultDomain, Config.DefaultDomain }.Distinct();

                    if (response == null) {
                        var domainCallbackMatches = hostnames.Where(name => HttpCallbacks.ContainsKey(name));
                        var regexCallbacks = domainCallbackMatches.SelectMany(name => HttpCallbacks[name]);
                        /*var regexCallbacks = new[] { host, DefaultDomain }.Distinct()
                            .Where(domain => HttpCallbacks.ContainsKey(domain))
                            .SelectMany(domain => HttpCallbacks[domain]);*/
                        foreach (var kvp in regexCallbacks) {
                            bool match = kvp.Key.IsMatch(path);
                            if (!match) continue;
                            methodsUsed.Add(kvp.Value.Method);
                            var callback = kvp.Value(context, cache);
                            var completedTask = await Task.WhenAny(callback, cancelationTask);
                            // Check that the callback didn't throw any errors (hence it didn't complete)
                            if (callback.IsFaulted) throw callback.Exception;
                            // So no faults, check if the completed task is the callback
                            if (completedTask != callback)
                                throw new ResponseTimedOutException(cacheName, methodsUsed, cancellationTokenSource.Token);
                            response = callback.Result;

                            if (response != null) break;
                        }
                    }
                    #endregion

                    #region Try get Razor File
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    bool isPrivateView = fileName.Length > 0 && fileName.StartsWith('_');
                    if (response == null && !isPrivateView) {
                        foreach (var hostname in hostnames) {
                            // If no razor engine exists, continue to next hostname
                            if (!RazorEngines.TryGetValue(hostname, out var razorEngine))
                                continue;

                            string cwd = Path.Combine(ViewsDirectory.FullName, hostname).Replace('\\', '/');
                            string[] razorPaths = new[] { $"{path}", $"{path}.cshtml", $"{path}/index.cshtml" };
                            foreach (var razorPath in razorPaths) {
                                string fullpath = cwd + razorPath;
                                if (Path.GetExtension(fullpath).ToLower() != ".cshtml") continue;
                                bool fileExists = File.Exists(fullpath);
                                if (!fileExists) continue;

                                cache ??= new CachedResponse(this, null);
                                cache.StatusCode = HttpStatusCode.OK;
                                cache.ContentString = await razorEngine.CompileRenderAsync<object?>(razorPath, null);
                                cache.ContentType = "text/html";
                                response = cache;
                                break;
                            }
                        }
                    }
                    #endregion
                    
                    #region Try Static File
                    // Finally get a static file
                    if (response == null) {
                        Callback getStaticFile = GetStaticFile;
                        methodsUsed.Add(getStaticFile.Method);
                        response = await getStaticFile(context, cache);
                    }
                    #endregion
                }
                catch (AggregateException ex) { // Also catches OperationCanceledException and ResponseTimedOutException
                    statusPageModel = new StatusPageModel(HttpStatusCode.ServiceUnavailable) {
                        Exception = ex
                    };
                }
                catch (Exception ex) {
                    statusPageModel = new StatusPageModel(HttpStatusCode.InternalServerError) {
                        Exception = ex
                    };
                }

                response ??= await GetGenericStatusPageAsync(statusPageModel ?? new StatusPageModel(HttpStatusCode.NotFound));

                #region Ship Response
                context.Response.StatusCode = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.Redirect) {
                    context.Response.Redirect(response.ContentString);
                }
                foreach (var kvp in response.Headers) {
                    if (kvp.Value is null)
                        context.Response.Headers.Add(kvp.Key);
                    else context.Response.Headers.Add(kvp.Key, kvp.Value);
                }
                bool omitBody = new[] { "HEAD", "PUT", "DELETE" }.Contains(context.Request.HttpMethod.ToUpper()) ||
                    (100 <= (int)response.StatusCode && (int)response.StatusCode <= 199) ||
                    response.StatusCode == HttpStatusCode.NoContent ||
                    response.StatusCode == HttpStatusCode.NotModified;
                if (!omitBody) {
                    context.Response.Headers["Content-Type"] = response.ContentType;
                    context.Response.ContentEncoding = Encoding.UTF8;
                    context.Response.ContentLength64 = response.Content.Length;
                    await context.Response.OutputStream.WriteAsync(response.Content, 0, response.Content.Length);
                }
                Logger?.LogRequest(context, response, methodsUsed);
                context.Response.Close();
                #endregion

                // And finally, if the response allows cache (by being a cachedResponse)
                //  override the path that it is on, so it can be reused
                if (response is CachedResponse cachedResponse) {
                    cachedResponse.Path = $"{host}{path}"; // Rewrite the path, so it can be located when a new request is received
                    if (Config.DebugMode) cachedResponse.RaiseUpdateFlag();
                }
            }
            catch (Exception ex) {
                Logger?.LogError(context, methodsUsed, ex);
            }
        }

        static string FormatCallbackKey(string key)
            => string.IsNullOrEmpty(key) ? string.Empty
                : key.ToLower().Replace('\\', '/').Replace("//", "/").Trim(' ', '/');

        public async Task<HttpResponse> GetStaticFile(HttpListenerContext context, CachedResponse? cache) => await GetStaticFile(context.Request.Url?.Host, context.Request.Url?.LocalPath, cache);

        public async Task<HttpResponse> GetStaticFile(string? targetDomain, string? localPath, CachedResponse? cache) {
            if (!cache?.NeedsUpdate ?? false) return cache;
            string? fileName = Path.GetFileName(localPath);
            if (fileName != null && fileName.StartsWith('_') && fileName.EndsWith(".cshtml")) // Is the file a private cshtml file?
                localPath = localPath?.Substring(0, localPath.Length - fileName.Length);
            DirectoryInfo directory = ViewsDirectory; // Might be changed later
            // Works on windows, but on linux, the domain folder will need to be lowercase
            targetDomain = targetDomain?.ToLower() ?? Config.DefaultDomain;
            string basePath = Path.Combine(directory.FullName, targetDomain);
            bool usingFallbackDomain = !Directory.Exists(basePath);
            if (usingFallbackDomain) { // Only fallback to default if domain folder doesn't exist
                targetDomain = Config.DefaultDomain;
                basePath = Path.Combine(directory.FullName, Config.DefaultDomain);
            }
            string resourceIdentifier = FormatCallbackKey(localPath ?? string.Empty);
            CachedResponse resource = cache ?? new CachedResponse(this, null);
            string filePath = Path.Combine(basePath, resourceIdentifier);
            if (File.Exists(filePath)) {
                resource.StatusCode = HttpStatusCode.OK;
                resource.ContentType = MimeTypeMap.GetMimeType(Path.GetExtension(filePath).ToLower());
                resource.Content = File.ReadAllBytes(filePath);
                resource.Headers["cache-control"] = Config.DebugMode ? "no-store, no-cache, must-revalidate"
                    : "max-age=360000, s-max-age=900, stale-while-revalidate=120, stale-if-error=86400";
                resource.ClearFlag();
                return resource;
            }
            string? hitPath = Config.UriFillers.Select(filler => filePath + filler)
                .FirstOrDefault(path => path.Contains(basePath) && File.Exists(path));
            if (hitPath != null) {
                resource.StatusCode = HttpStatusCode.OK;
                resource.ContentType = MimeTypeMap.GetMimeType(Path.GetExtension(hitPath).ToLower());
                resource.Content = File.ReadAllBytes(hitPath);
                resource.Headers["cache-control"] = Config.DebugMode ? "no-store, no-cache, must-revalidate"
                    : "max-age=360000, s-max-age=900, stale-while-revalidate=120, stale-if-error=86400";
                resource.ClearFlag();
                return resource;
            }
            return await GetGenericStatusPageAsync(new StatusPageModel(Directory.Exists(filePath) ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound), host: targetDomain);
        }
        
        public async Task<HttpResponse> GetGenericStatusPageAsync(StatusPageModel pageModel, string? host = null, ExpandoObject? viewBag = null) {
            //try { pageModel.Exception ??= throw new Exception("test"); }
            //catch (Exception e) { pageModel.Exception = e; }
            var potentialHosts = new string[] { host ?? Config.DefaultDomain, Config.DefaultDomain }.Distinct()
                .Select(h => h.Trim(' ', '/', '\\'));
            const string viewName = "_StatusPage.cshtml";
            foreach (string hostname in potentialHosts) {
                if (!File.Exists(Path.Combine(ViewsDirectory.FullName, hostname, viewName))) continue;
                try {
                    return new HttpResponse(
                        pageModel.StatusCode,
                        await GetOrCreateRazorEngine(hostname).CompileRenderAsync(viewName, pageModel, viewBag),
                        MimeTypeMap.GetMimeType(".cshtml")
                    );
                } catch (Exception ex) {
                    pageModel.Exception = pageModel.Exception != null
                        ? new AggregateException(pageModel.Exception, ex)
                        : ex;
                    break;
                }
            }

            // If no template was found, fallback to plain text
            var bckResponse = new HttpResponse() {
                StatusCode = pageModel.StatusCode,
                ContentType = MimeTypeMap.GetMimeType(".txt"),
                ContentString = $"{(int)pageModel.StatusCode} {pageModel.Header} - {pageModel.Details}\n{pageModel.Exception?.ToString() ?? ""}"
            };
            return bckResponse;
        }

        public bool ContainsEventCallback(Task<HttpResponse?> callback) {
            if (callback == null) return false;
            return HttpCallbacks.Any(domainKvp => domainKvp.Value.Values.Any(v => v.Equals(callback)));
        }

        public bool TryAddEventCallback(string host, Regex regex, Callback callback) {
            if (string.IsNullOrEmpty(host) || regex == null || callback == null) return false;
            host = FormatCallbackKey(host);
            if (!HttpCallbacks.TryGetValue(host, out var domainCallbacks))
                HttpCallbacks.Add(host, domainCallbacks = new Dictionary<Regex, Callback>());
            domainCallbacks[regex] = callback;
            return true;
        }

        public bool TryRemoveEventCallback(Callback method) {
            if (method == null) return false;

            ushort removeCount = 0;
            foreach (var callbackDict in HttpCallbacks.Values) {
                foreach (var kvp in callbackDict.Where(kvp => kvp.Value == method)) {
                    callbackDict.Remove(kvp.Key);
                    removeCount++;
                }
            }
            return removeCount > 0;
        }

        public bool TryRegisterArea<T>(Func<T>? areaInitilizer, out T area) where T : AreaBase {
            area = areaInitilizer?.Invoke() ?? default;
            if (area == null) return false;
            RegisteredAreas.Add(area);
            return true;
        }

        public void RegisterArea<T>(Func<T>? areaInitializer, out T area) where T : AreaBase {
            if (!TryRegisterArea(areaInitializer, out area))
                throw new Exception($"Failed to bind {typeof(T).FullName}! Is your initializer returning a valid area?");
        }
    }
}
