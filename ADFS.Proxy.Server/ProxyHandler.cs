using ADFS.Proxy.Server.Cache;
using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ADFS.Proxy.Server
{
    public class ProxyHandler : DelegatingHandler
    {
        public InMemoryCache memoryCache;

        public ProxyHandler()
        {
            memoryCache = new InMemoryCache();
        }

        private async Task<HttpResponseMessage> RedirectRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var localPath = request.RequestUri.LocalPath.Replace("/proxy", "/adfs");
            var redirectLocation = ConfigurationManager.AppSettings["ida:RedirectLocation"];

            string primaryCacheKey = request.RequestUri.ToString().ToLower();
            bool responseIsCached = false;
            HttpResponseMessage responseFromCache = null;

            // first, before even looking at the cache:
            // The Cache-Control: no-cache HTTP/1.1 header field is also intended for use in requests made by the client. 
            // It is a means for the browser to tell the server and any intermediate caches that it wants a 
            // fresh version of the resource. 

            if (request.Headers.CacheControl != null && request.Headers.CacheControl.NoCache && localPath != "/adfs/discovery/keys")
            {
                // Don't get from cache.  Get from server.
                return await HandleRedirectRequest(primaryCacheKey,
                    request, localPath, redirectLocation, cancellationToken);
            }

            // available in cache?
            var cacheEntry = await memoryCache.GetAsync<CacheEntry>(primaryCacheKey);
            if (cacheEntry != default(CacheEntry))
            {
                // TODO: for all of these, check the varyby headers (secondary key).  
                // An item is a match if secondary & primary keys both match!
                responseFromCache = new HttpResponseMessage(cacheEntry.StatusCode);
                responseFromCache.ReasonPhrase = cacheEntry.StatusDescription;
                responseFromCache.Version = cacheEntry.ProtocolVersion;
                responseFromCache.RequestMessage = request;

                responseIsCached = true;
            }

            if (responseIsCached)
            {
                // set the accompanying request message
                responseFromCache.RequestMessage = request;

                // Check conditions that might require us to revalidate/check

                // we must assume "the worst": get from server.

                bool mustRevalidate = HttpResponseHelpers.MustRevalidate(responseFromCache);

                if (mustRevalidate)
                {
                    // we must revalidate - add headers to the request for validation.  
                    //  
                    // we add both ETag & IfModifiedSince for better interop with various
                    // server-side caching handlers. 
                    //
                    if (responseFromCache.Headers.ETag != null)
                    {
                        request.Headers.Add(Utils.Constants.HttpHeaderConstants.IfNoneMatch,
                            responseFromCache.Headers.ETag.ToString());
                    }

                    if (responseFromCache.Content.Headers.LastModified != null)
                    {
                        request.Headers.Add(Utils.Constants.HttpHeaderConstants.IfModifiedSince,
                            responseFromCache.Content.Headers.LastModified.Value.ToString("r"));
                    }
                    // Don't get from cache.  Get from server.
                    return await HandleRedirectRequest(primaryCacheKey,
                        request, localPath, redirectLocation, cancellationToken);
                }
                else
                {
                    responseFromCache.Content = new StringContent(cacheEntry.Content, Encoding.UTF8, "application/json");

                    // response is allowed to be cached and there's
                    // no need to revalidate: return the cached response
                    return responseFromCache;
                }
            }
            else
            {
                // Don't get from cache.  Get from server.
                return await HandleRedirectRequest(primaryCacheKey,
                    request, localPath, redirectLocation, cancellationToken);
            }
        }

        private async Task<HttpResponseMessage> HandleRedirectRequest(string primaryCacheKey, HttpRequestMessage request, string localPath, string redirectLocation, CancellationToken cancellationToken)
        {
            var client = new HttpClient();

            try
            {
                var clonedRequest = await HttpRequestMessageExtensions.CloneHttpRequestMessageAsync(request);
                clonedRequest.RequestUri = new Uri(redirectLocation + localPath);
                clonedRequest.Headers.Host = ConfigurationManager.AppSettings["ida:AuthorizationHost"];

                client.DefaultRequestHeaders.UserAgent.TryParseAdd(ConfigurationManager.AppSettings["userAgent"]);

                if (request.Method == HttpMethod.Get)
                {
                    clonedRequest.Content = null;
                }

                HttpResponseMessage responseMessage = await client.SendAsync(clonedRequest,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                // ensure no NULL dates
                if (responseMessage.Headers.Date == null)
                {
                    responseMessage.Headers.Date = DateTimeOffset.UtcNow;
                }

                // check the response: is this response allowed to be cached?
                bool isCacheable = HttpResponseHelpers.CanBeCached(responseMessage);

                if (isCacheable)
                {
                    // add the response to cache
                    memoryCache.SetHalfHour(primaryCacheKey, new CacheEntry(responseMessage));
                }

                // what about vary by headers (=> key should take this into account)?

                return responseMessage;
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                client.Dispose();
            }
        }

        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            if (!request.RequestUri.LocalPath.StartsWith("/proxy"))
            {
                return await base.SendAsync(request, cancellationToken);
            }
            else
            {
                var backupServerCertificateValidation = System.Net.ServicePointManager.ServerCertificateValidationCallback;
                var backupSecurityProtocol = System.Net.ServicePointManager.SecurityProtocol;

                try
                {
                    // System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    System.Net.ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    System.Net.ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);

                    return await RedirectRequest(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    HttpResponseMessage response = request.CreateResponse(HttpStatusCode.OK, "Error");
                    response.Content = new StringContent(ex.Message + " | " + (ex.InnerException != null ? ex.InnerException.Message : ""), Encoding.Unicode);

                    return response;
                }
                finally
                {
                    System.Net.ServicePointManager.SecurityProtocol = backupSecurityProtocol;
                    System.Net.ServicePointManager.ServerCertificateValidationCallback = backupServerCertificateValidation;
                }
            }
        }
    }
}