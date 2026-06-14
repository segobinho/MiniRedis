namespace MiniRedis.Core.Cache
{
    internal sealed class RedisDictEntry
    {
        internal readonly string Key;
        internal readonly uint Hash; // cached — reused during rehash, avoids recomputing
        internal CacheItem Value;
        internal RedisDictEntry? Next;

        internal RedisDictEntry(string key, uint hash, CacheItem value)
        {
            Key = key;
            Hash = hash;
            Value = value;
        }
    }

    internal sealed class RedisDict
    {
        private const int InitialSize = 4;

        private struct Ht
        {
            internal RedisDictEntry?[] Buckets;
            internal int SizeMask; // = Size - 1, used in bitwise AND instead of modulo
            internal int Used;

            internal int Size => Buckets?.Length ?? 0;

            internal static Ht Create(int size) => new()
            {
                Buckets = new RedisDictEntry?[size],
                SizeMask = size - 1,
                Used = 0
            };
        }

        private Ht _ht0;
        private Ht _ht1;
        private int _rehashIdx = -1; // -1 = idle | >= 0 = bucket currently being migrated

        public int Count => _ht0.Used + _ht1.Used;

        public RedisDict()
        {
            _ht0 = Ht.Create(InitialSize);
        }

        // Ordinal hash — faster than culture-aware and stable within the process.
        // We never persist the hash, only the key, so per-process randomization is fine.
        private static uint ComputeHash(string key)
            => unchecked((uint)string.GetHashCode(key, StringComparison.Ordinal));

        // Power-of-2 sizes let us replace % with a single AND instruction.
        private static int BucketOf(uint hash, int sizeMask) => (int)(hash & (uint)sizeMask);

        public bool TryGetValue(string key, out CacheItem value)
        {
            uint hash = ComputeHash(key);

            for (var e = _ht0.Buckets[BucketOf(hash, _ht0.SizeMask)]; e != null; e = e.Next)
            {
                if (e.Hash == hash && e.Key == key) { value = e.Value; return true; }
            }

            if (_rehashIdx >= 0)
            {
                for (var e = _ht1.Buckets[BucketOf(hash, _ht1.SizeMask)]; e != null; e = e.Next)
                {
                    if (e.Hash == hash && e.Key == key) { value = e.Value; return true; }
                }
            }

            value = default!;
            return false;
        }

        public void Set(string key, CacheItem value)
        {
            if (_rehashIdx >= 0) RehashStep();

            uint hash = ComputeHash(key);

            // Update in-place when entry already exists (search both tables if rehashing).
            var existing = FindEntry(hash, key);
            if (existing != null) { existing.Value = value; return; }

            // New entries always go to ht1 during rehash so ht0 only shrinks — convergence guaranteed.
            ref Ht target = ref (_rehashIdx >= 0 ? ref _ht1 : ref _ht0);
            int idx = BucketOf(hash, target.SizeMask);
            var entry = new RedisDictEntry(key, hash, value);
            entry.Next = target.Buckets[idx];
            target.Buckets[idx] = entry;
            target.Used++;

            if (_rehashIdx < 0 && _ht0.Used >= _ht0.Size)
                StartExpand();
        }

        public bool Remove(string key)
        {
            if (_rehashIdx >= 0) RehashStep();

            uint hash = ComputeHash(key);

            if (TryRemoveFrom(ref _ht0, hash, key)) { TryFinishRehash(); return true; }
            if (_rehashIdx >= 0 && TryRemoveFrom(ref _ht1, hash, key)) { TryFinishRehash(); return true; }

            return false;
        }

        public void Clear()
        {
            _ht0 = Ht.Create(InitialSize);
            _ht1 = default;
            _rehashIdx = -1;
        }

        // Returns a snapshot list — safe to call under a read lock (no lazy enumeration).
        public List<KeyValuePair<string, CacheItem>> ToList()
        {
            var list = new List<KeyValuePair<string, CacheItem>>(Count);

            foreach (var bucket in _ht0.Buckets)
                for (var e = bucket; e != null; e = e.Next)
                    list.Add(new(e.Key, e.Value));

            if (_rehashIdx >= 0)
                foreach (var bucket in _ht1.Buckets)
                    for (var e = bucket; e != null; e = e.Next)
                        list.Add(new(e.Key, e.Value));

            return list;
        }

        private RedisDictEntry? FindEntry(uint hash, string key)
        {
            for (var e = _ht0.Buckets[BucketOf(hash, _ht0.SizeMask)]; e != null; e = e.Next)
                if (e.Hash == hash && e.Key == key) return e;

            if (_rehashIdx >= 0)
                for (var e = _ht1.Buckets[BucketOf(hash, _ht1.SizeMask)]; e != null; e = e.Next)
                    if (e.Hash == hash && e.Key == key) return e;

            return null;
        }

        private bool TryRemoveFrom(ref Ht ht, uint hash, string key)
        {
            int idx = BucketOf(hash, ht.SizeMask);
            RedisDictEntry? prev = null;
            for (var e = ht.Buckets[idx]; e != null; prev = e, e = e.Next)
            {
                if (e.Hash != hash || e.Key != key) continue;
                if (prev == null) ht.Buckets[idx] = e.Next;
                else prev.Next = e.Next;
                ht.Used--;
                return true;
            }
            return false;
        }

        // Migrates one bucket from ht0 to ht1 per call — amortized across writes.
        private void RehashStep()
        {
            // Skip empty buckets (may be many if table is sparse).
            while (_rehashIdx < _ht0.Size && _ht0.Buckets[_rehashIdx] == null)
                _rehashIdx++;

            if (_rehashIdx >= _ht0.Size) { FinishRehash(); return; }

            var e = _ht0.Buckets[_rehashIdx];
            _ht0.Buckets[_rehashIdx] = null;

            while (e != null)
            {
                var next = e.Next;
                int newIdx = BucketOf(e.Hash, _ht1.SizeMask);
                e.Next = _ht1.Buckets[newIdx];
                _ht1.Buckets[newIdx] = e;
                _ht0.Used--;
                _ht1.Used++;
                e = next;
            }

            _rehashIdx++;
            TryFinishRehash();
        }

        private void TryFinishRehash()
        {
            if (_rehashIdx >= 0 && _ht0.Used == 0) FinishRehash();
        }

        private void FinishRehash()
        {
            _ht0 = _ht1;
            _ht1 = default;
            _rehashIdx = -1;
        }

        private void StartExpand()
        {
            _ht1 = Ht.Create(NextPowerOfTwo(_ht0.Used * 2));
            _rehashIdx = 0;
        }

        private static int NextPowerOfTwo(int n)
        {
            if (n < InitialSize) return InitialSize;
            n--;
            n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
            return n + 1;
        }
    }
}
