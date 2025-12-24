namespace MiniRedis.Core.Cache
{
    public interface ICacheStore
    {
        //void Set<T>(string key, T value);
        //void Set<T>(string key, T value, TimeSpan ttl);
        //T? Get<T>(string key);

        void Set(string key, string json, TimeSpan? ttl);
        string? Get(string key);
        void Delete(string key);
        bool Exists(string key);
        void Clear();
        IEnumerable<KeyValuePair<string, CacheItem>> GetAll();
        long Count { get; }
    }
}
