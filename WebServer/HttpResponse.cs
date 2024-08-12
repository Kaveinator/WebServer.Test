using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebServer.Utils;

namespace WebServer {
    public class HttpResponse {
        public HttpStatusCode StatusCode = HttpStatusCode.NoContent;
        public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;
        public string ContentType = MimeTypeMap.GetMimeType(".txt");
        public byte[] Content = Array.Empty<byte>();
        public string ContentString {
            get => Encoding.UTF8.GetString(Content);
            set => Content = Encoding.UTF8.GetBytes(value);
        }

        public HttpResponse() { }

        public HttpResponse(HttpStatusCode statusCode, byte[] content, string contentType = "text/plain") {
            StatusCode = statusCode;
            Content = content;
            ContentType = contentType;
        }

        public HttpResponse(HttpStatusCode statusCode, string content, string contentType = "text/plain") {
            StatusCode = statusCode;
            ContentString = content;
            ContentType = contentType;
        }
    }
}
