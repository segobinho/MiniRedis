namespace MiniRedis.Core.Cache
{
    public class HashCacheStore : ICacheStore
    {

        public long Count => _store.Count();

        private readonly Dictionary<string, CacheItem> _store = new();

        public HashCacheStore(Dictionary<string, CacheItem> store)
        {
            _store = store;
        }

        public HashCacheStore()
        {

        }
        public void Clear()
        {
            _store.Clear();
        }

        public void Delete(string key)
        {
            _store.Remove(key);
        }

        public bool Exists(string key)
        {
            var hash = _store[key];
            if (hash != null && !hash.IsExpired()) return true;

            _store.Remove(key);
            return false;
        }

        public string? Get(string key)
        {
            if (!_store.ContainsKey(key))
            {
                return null;
            }


            return _store[key].Value;
        }

        public void Set(string key, string value, TimeSpan? ttl = null)
        {
            DateTimeOffset? expiresAt = ttl.HasValue
                ? DateTimeOffset.UtcNow.Add(ttl.Value)
                : null;

            var cacheItem = new CacheItem(value, expiresAt);
            _store[key] = cacheItem;
        }

        IEnumerable<KeyValuePair<string, CacheItem>> ICacheStore.GetAll()
        {
            return _store;
        }

       
    }
}
