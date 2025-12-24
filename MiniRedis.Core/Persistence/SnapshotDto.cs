namespace MiniRedis.Core.Persistence
{
    public class SnapshotDto
    {
        public DateTimeOffset CreatedAt { get; set; }
        public List<SnapshotItemDto> Items { get; set; } = new();
    }
}
