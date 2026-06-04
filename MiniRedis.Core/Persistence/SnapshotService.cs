using System.Text.Json;
using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Persistence
{
    public class SnapshotService : ISnapshotService
    {
        private readonly ICacheStore _cache;
        private readonly string _filePath;

        public SnapshotService(ICacheStore cache, string filePath)
        {
            _cache = cache;
            _filePath = filePath;
        }

        // 🔹 SALVAR SNAPSHOT
        public void Save()
        {
            var snapshot = new SnapshotDto
            {
                CreatedAt = DateTimeOffset.UtcNow
            };

            foreach (var entry in _cache.GetAll())
            {
                var item = entry.Value;

                if (item.IsExpired())
                    continue;

                snapshot.Items.Add(new SnapshotItemDto
                {
                    Key = entry.Key,
                    Value = item.Value,
                    ExpiresAt = item.ExpiresAt
                });
            }

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(
                _filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            );

            JsonSerializer.Serialize(
                stream,
                snapshot,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
        public void Load()
        {
            if (!File.Exists(_filePath))
                return;

            try
            {
                using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );

                var snapshot = JsonSerializer.Deserialize<SnapshotDto>(stream);

                if (snapshot == null)
                    return;

                foreach (var item in snapshot.Items)
                {
                    if (item.ExpiresAt.HasValue &&
                        item.ExpiresAt.Value <= DateTimeOffset.UtcNow)
                        continue;

                    TimeSpan? ttl = item.ExpiresAt.HasValue
                        ? item.ExpiresAt.Value - DateTimeOffset.UtcNow
                        : null;

                    _cache.Set(item.Key, item.Value, ttl);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
