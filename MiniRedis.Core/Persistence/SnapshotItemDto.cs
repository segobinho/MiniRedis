namespace MiniRedis.Core.Persistence
{
    public class SnapshotItemDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
