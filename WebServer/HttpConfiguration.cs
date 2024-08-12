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
    }
}
