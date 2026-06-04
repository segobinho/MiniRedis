namespace MiniRedis.Core.Persistence
{
    public class CacheRecoveryService : IDisposable
    {
        private readonly ISnapshotService _snapshotService;
        private readonly TimeSpan _interval;


        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public CacheRecoveryService(ISnapshotService snapshotService, TimeSpan interval)
        {
            _snapshotService = snapshotService;
            _interval = interval;
        }

        public void Start()
        {
            try
            {
                _snapshotService.Load();
                Console.WriteLine($"recebapai");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Snapshot] Load failed: {ex.Message}");
            }

            _cts = new CancellationTokenSource();
            _backgroundTask = Task.Run(
                () => RunSnapshotLoop(_cts.Token));
        }

        private async Task RunSnapshotLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, token);
                    _snapshotService.Save();
                    //Console.WriteLine($"save sucesso");
                }
                catch (TaskCanceledException)
                {
                    // shutdown normal
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Snapshot] Save failed: {ex.Message}");
                }
            }
        }
        public void Stop()
        {
            if (_cts == null)
                return;

            _cts.Cancel();

            try
            {
                _backgroundTask?.Wait();
            }
            catch { }

            // Último snapshot garantido
            try
            {
                _snapshotService.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Snapshot] Final save failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
