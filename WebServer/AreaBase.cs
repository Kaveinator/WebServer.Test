using Microsoft.Extensions.Hosting;
using RazorLight;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebServer.Models;

namespace WebServer {
    public abstract class AreaBase { // Formally `HttpEndpointHandler`
        // Provides razor pages, resources and relevent db tables and entities
        public readonly HttpServer Server;
        public readonly RazorLightEngine RazorEngine;
        public readonly string Hostname;
        public readonly string AreaPath;
        public readonly AreaBase? ParentArea;

        public AreaBase(HttpServer server, string host) : this(server, host, string.Empty) { }

        public AreaBase(HttpServer server, string host, string areaPath) {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (string.IsNullOrEmpty(host)) throw new ArgumentNullException(nameof(host));
            Server = server;
            RazorEngine = server.GetOrCreateRazorEngine(host);
            Hostname = host;
            AreaPath = areaPath ?? string.Empty;
        }

        public AreaBase(AreaBase parentArea, string relativeAreaPath) { // So you can build on top of other areas
            ParentArea = parentArea;
            Server = parentArea.Server;
            RazorEngine = parentArea.RazorEngine;
            Hostname = parentArea.Hostname;
            AreaPath = $"{parentArea.AreaPath}/{relativeAreaPath.Trim(' ', '/', '\\')}";
        }

        public static Uri BuildUri(HttpListenerRequest request, string path) {
            string uriString = $"{(request.IsSecureConnection ? "https" : "http")}://{request.UserHostName}/";
            if (string.IsNullOrEmpty(path))
                return new Uri(uriString);
            path = path.Replace('\\', '/');
            if (path[0] == '/')
                path = path.Substring(1);
            uriString += path;
            return new Uri(uriString);
        }

        #region Razor Stuff
        public async Task<HttpResponse> GetGenericStatusPageAsync(StatusPageModel model, ExpandoObject? viewBag = null) {
            return await Server.GetGenericStatusPageAsync(model, Hostname, viewBag);
        }
        public async Task<HttpResponse> GetGenericStatusPageAsync(HttpStatusCode statusCode, ExpandoObject? viewBag = null)
             => await GetGenericStatusPageAsync(new StatusPageModel(statusCode), viewBag);

        /// <summary>Renders a View with a T model</summary>
        /// <typeparam name="T">Type of the model</typeparam>
        /// <param name="viewPath">The view path relative to the AreaName, keep in mind that prepending a '/' (or '\') will go to root of the top-most area</param>
        /// <param name="model">The model supplied to the view</param>
        /// <returns>A string of the compiled view</returns>
        public async Task<string> RenderAsync<T>(string viewPath, T model, ExpandoObject viewBag = null) { // Formally Try/GetTemplate
            viewPath = viewPath.Trim().Replace('\\', '/');
            string root = viewPath[0] == '/' ? "" : AreaPath;
            return await RazorEngine.CompileRenderAsync<T>($"{root}{viewPath}", model, viewBag);
        }
        #endregion

        #region Event Binds
        public Regex GetRegex(string regex) => new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public bool TryAddEventCallback(Regex regex, HttpServer.Callback callback)
            => Server.TryAddEventCallback(Hostname, regex, callback);

        public void AddEventCallback(Regex regex, HttpServer.Callback callback) {
            if (!TryAddEventCallback(regex, callback))
                throw new Exception($"Error in Area '{AreaPath}'! Failed to bind event callback to `{callback.Method.Name}`");
        }

        public bool TryAddEventCallback(string regexPattern, HttpServer.Callback callback)
            => TryAddEventCallback(GetRegex(regexPattern), callback);

        public void AddEventCallback(string regexPattern, HttpServer.Callback callback) {
            if (!TryAddEventCallback(regexPattern, callback))
                throw new Exception($"Error in Area '{AreaPath}'! Failed to bind event callback to `{callback.Method.Name}`");
        }
        #endregion
    }
}
