namespace MiniRedis.Core.Cache
{
    public class HashCacheStore : ICacheStore
    {
        private readonly RedisDict _dict = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        public long Count
        {
            get
            {
                _lock.EnterReadLock();
                try { return _dict.Count; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public void Set(string key, string json, TimeSpan? ttl = null)
        {
            DateTimeOffset? expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null;
            var item = new CacheItem(json, expiresAt);

            _lock.EnterWriteLock();
            try { _dict.Set(key, item); }
            finally { _lock.ExitWriteLock(); }
        }

        public string? Get(string key)
        {
            _lock.EnterReadLock();
            bool found = _dict.TryGetValue(key, out var item);
            _lock.ExitReadLock();

            if (!found) return null;

            if (item.IsExpired())
            {
                _lock.EnterWriteLock();
                try { _dict.Remove(key); }
                finally { _lock.ExitWriteLock(); }
                return null;
            }

            return item.Value;
        }

        public void Delete(string key)
        {
            _lock.EnterWriteLock();
            try { _dict.Remove(key); }
            finally { _lock.ExitWriteLock(); }
        }

        public bool Exists(string key)
        {
            _lock.EnterReadLock();
            bool found = _dict.TryGetValue(key, out var item);
            _lock.ExitReadLock();

            if (!found) return false;

            if (item.IsExpired())
            {
                _lock.EnterWriteLock();
                try { _dict.Remove(key); }
                finally { _lock.ExitWriteLock(); }
                return false;
            }

            return true;
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try { _dict.Clear(); }
            finally { _lock.ExitWriteLock(); }
        }

        public IEnumerable<KeyValuePair<string, CacheItem>> GetAll()
        {
            _lock.EnterReadLock();
            try { return _dict.ToList(); }
            finally { _lock.ExitReadLock(); }
        }
    }
}
