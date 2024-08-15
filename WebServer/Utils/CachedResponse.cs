using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WebServer.Utils {
    public class CachedResponse : HttpResponse {
        #region Static Methods, etc
        public static bool BypassCache = false;
        public static List<CachedResponse> Instances { get; private set; } = new List<CachedResponse>();
        public static ushort RaiseAllUpdateFlags() {
            ushort flagsRaised = 0;
            foreach (CachedResponse resource in Instances) {
                if (resource.NeedsUpdate) continue;
                flagsRaised++;
                resource.RaiseUpdateFlag();
                //Logger.LogDebug($"Raised Update Flag for '{resource.Name}'");
            }
            return flagsRaised;
        }

        public static CachedResponse? Get(HttpServer server, string path) {
            path = path.Trim();
            return Instances.FirstOrDefault(x => x.Server == server && x.Path == path);
        }

        public static bool TryGet(HttpServer server, string path, out CachedResponse? resource) 
            => (resource = Get(server, path)) != null;
        #endregion

        public readonly HttpServer Server;
        public string Path;
        bool UpdateFlagRaised;
        Stopwatch TimeSinceLastUpdate;
        public HttpServer.Callback? UpdateMethod; // If null, HttpServer will attempt to find one or GetStaticFile

        public bool NeedsUpdate => UpdateFlagRaised || BypassCache || Server.Config.MaxCacheAge < TimeSinceLastUpdate.Elapsed.TotalSeconds;

        public CachedResponse(HttpServer parentServer, HttpServer.Callback? updateMethod) {
            Instances.Add(this);
            Server = parentServer;
            TimeSinceLastUpdate = Stopwatch.StartNew();
            UpdateMethod = updateMethod;
        }

        public void RaiseUpdateFlag() => UpdateFlagRaised = true;
        public void ClearFlag() => UpdateFlagRaised = false;
    }
}
