using System;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace ADFS.Proxy.Server.Cache
{
    public class InMemoryCache : ICacheService
    {
        private static readonly object WebCacheLock = new object();

        // Expiration in 10 minutes
        private static int DefaultExpirationInMinutes = 10;

        // Expiration in 30 minutes
        private static int ExpirationInHalfHour = 30;

        public T GetOrSet<T>(string cacheKey, Func<T> getItemCallback) where T : class
        {
            T item = MemoryCache.Default.Get(cacheKey) as T;

            if (item == null)
            {
                item = getItemCallback();

                if (item == null)
                {
                    // Double-checked locking
                    // See: http://stackoverflow.com/questions/39112/what-is-the-best-way-to-lock-cache-in-asp-net
                    lock (WebCacheLock)
                    {
                        MemoryCache.Default.Add(cacheKey, item, DateTime.Now.AddMinutes(DefaultExpirationInMinutes));
                    }
                }
            }
            return item;
        }

        public async Task<T> GetAsync<T>(string cacheKey) where T : class
        {
            T item = await Task.Run(() => MemoryCache.Default.Get(cacheKey) as T);
            if (item == null)
            {
                return default(T);
            }
            return item;
        }

        public void SetHalfHour<T>(string cacheKey, T item) where T : class
        {
            // Double-checked locking
            // See: http://stackoverflow.com/questions/39112/what-is-the-best-way-to-lock-cache-in-asp-net
            lock (WebCacheLock)
            {
                MemoryCache.Default.Add(cacheKey, item, DateTime.Now.AddMinutes(ExpirationInHalfHour));
            }
        }
    }

    interface ICacheService
    {
        T GetOrSet<T>(string cacheKey, Func<T> getItemCallback) where T : class;
        Task<T> GetAsync<T>(string cacheKey) where T : class;
        void SetHalfHour<T>(string cacheKey, T item) where T : class;
    }
}