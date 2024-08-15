using System;
using System.Collections.Generic;
using System.Text;

namespace WebServer {
    public class HttpConfiguration {
        public bool AutoStart = false;
        public ushort Port = 80;
        public string DefaultDomain = "localhost"; // The domain that the server will fallback on if the 
        public bool ShowExceptionOnErrorPages = true; // On InternalServerError(500), should it show the exception?
        public ushort MaxConcurrentRequests = 100;
        public List<string> UriFillers = new List<string>() { // Todo: Needs revision,
            ".html",
            ".htm",
            ".txt",
            "./index.html",
            "./index.htm",
            "./index.txt",
            "./default.webp",
            "./default.png",
            "../default.webp",
            "../default.png"
        };
        public Dictionary<string, string?> GenericHeaders = new Dictionary<string, string?>() {
            { "x-content-type-options: nosniff", null },
            { "x-xss-protection:1; mode=block", null },
            { "x-frame-options:DENY", null }
        };
        public ushort ResponseTimeout = 10;
        public uint MaxCacheAge = 604800;
        public bool DebugMode = false; // If true, it bypasses cache and also shows exceptions on error page (where applicable)

        public virtual HttpServer CreateServer(IHttpLogger? logger = null) => new HttpServer(this, logger);
    }
}
