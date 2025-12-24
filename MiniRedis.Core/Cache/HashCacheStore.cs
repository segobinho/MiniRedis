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
            if (hash != null && hash.IsExpired == false) return true;

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

        public void Set(string key, string json, TimeSpan ttl)
        {
            var cach = new CacheItem(json, ttl);

            _store[key] = cach;
        }

        public IEnumerable<string> GetAllKeys()
        {
            return _store.Keys;
        }
    }
}
