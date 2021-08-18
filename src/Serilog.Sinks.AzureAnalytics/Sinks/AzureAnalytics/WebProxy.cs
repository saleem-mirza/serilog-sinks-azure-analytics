using System;
using System.Net;

namespace Serilog.Sinks.AzureAnalytics
{
    internal class WebProxy : IWebProxy
    {
        private readonly Uri _proxyUri;      
        
        public WebProxy(string proxy) : this(new Uri(proxy))
        { }

        private WebProxy(Uri proxyUri)
        {
            _proxyUri = proxyUri;
        }  

        public Uri GetProxy(Uri destination)
        {
            return _proxyUri;
        }

        public bool IsBypassed(Uri host)
        {
            return false;
        }

        public ICredentials Credentials { get; set; }
    }
}
