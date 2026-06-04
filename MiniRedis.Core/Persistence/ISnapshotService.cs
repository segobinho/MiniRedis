namespace MiniRedis.Core.Persistence
{
    public interface ISnapshotService
    {
        public void Save();
        public void Load();
    }
}
