using System;
using System.Net;

namespace Serilog.Sinks
{
    internal class WebProxy : IWebProxy
    {
        private Uri _proxyUri;      
        
        public WebProxy(string proxy) : this(new Uri(proxy))
        { }
        
        public WebProxy(Uri proxyUri)
        {
            _proxyUri = proxyUri;
        }  

        public ICredentials Credentials { get; set; }

        public Uri GetProxy(Uri destination)
        {
            return _proxyUri;
        }

        public bool IsBypassed(Uri host)
        {
            return false;
        }
    }
}