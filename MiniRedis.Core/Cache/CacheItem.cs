namespace MiniRedis.Core.Cache
{
    public class CacheItem
    {
        public CacheItem(string value, TimeSpan expiresAt)
        {
            Value = value;
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiresAt);
        }

        public string Value { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;


        public override string ToString()
        {
            return Value;
        }
    }
}
