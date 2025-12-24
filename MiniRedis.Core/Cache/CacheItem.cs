public class CacheItem
{
    public string Value { get; }
    public DateTimeOffset? ExpiresAt { get; }

    public CacheItem(string value, DateTimeOffset? expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue &&
               ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }
}
