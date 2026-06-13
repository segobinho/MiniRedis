//using MiniRedis.Core.Cache;
//using MiniRedis.Core.Commands;
//using MiniRedis.Core.Persistence;
//using MiniRedis.Server;

//ICacheStore cache = new HashCacheStore();
//var basePath = AppContext.BaseDirectory;
//var snapshotPath = Path.Combine(basePath, "snapshot.json");
//var snapshot = new SnapshotService(cache, snapshotPath);
//var CacheRecovery = new CacheRecoveryService(snapshot, TimeSpan.FromSeconds(10));


//// Na inicialização
////snapshot.Load();
//CacheRecovery.Start();


//var parser = new CommandParser();
//var factory = new CommandFactory();

//CoreCommandRegistration.RegisterAll(factory, cache);

//ConsoleStyle.WriteBanner();
//ConsoleStyle.WriteInfo("MiniRedis Server started!");
//ConsoleStyle.WriteInfo("Type commands like: GET key");
//ConsoleStyle.WriteInfo("Type 'exit' to quit.\n");

//while (true)
//{
//    ConsoleStyle.WritePrompt();

//    var input = Console.ReadLine();

//    if (input is null)
//        continue;

//    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
//        break;

//    try
//    {
//        var tokens = parser.Parse(input);
//        var command = factory.Create(tokens);

//        var result = command.Execute();
//        ConsoleStyle.WriteResult(result);
//    }
//    catch (Exception ex)
//    {
//        ConsoleStyle.WriteError(ex.Message);
//    }
//}
//CacheRecovery.Stop();
//snapshot.Save();










//using MiniRedis.Core.Cache;
//using MiniRedis.Core.Commands;
//using MiniRedis.Core.Persistence;
//using MiniRedis.Server;
//using System.Net;
//using System.Net.Sockets;

//// ===============================
//// Infra / Core
//// ===============================

//ICacheStore cache = new HashCacheStore();

//var basePath = AppContext.BaseDirectory;
//var snapshotPath = Path.Combine(basePath, "snapshot.json");

//// Snapshot + recovery
//var snapshotService = new SnapshotService(cache, snapshotPath);
//var cacheRecovery = new CacheRecoveryService(
//    snapshotService,
//    TimeSpan.FromSeconds(10)
//);

//// ===============================
//// Inicialização
//// ===============================

//// 1️⃣ Carrega snapshot inicial
//snapshotService.Load();

//// 2️⃣ Inicia snapshot periódico
//cacheRecovery.Start();

//// ===============================
//// Commands
//// ===============================

//var parser = new CommandParser();
//var factory = new CommandFactory();

//// se você tiver registro automático
//// CoreCommandRegistration.RegisterAll(factory, cache);

//// ===============================
//// TCP Server
//// ===============================

//var server = new TcpRedisServer(
//    port: 6379,
//    sessionFactory: () =>
//        new ClientSession(parser, factory)
//);

//// ===============================
//// Lifecycle
//// ===============================

//using var cts = new CancellationTokenSource();

//// CTRL + C
//Console.CancelKeyPress += (s, e) =>
//{
//    e.Cancel = true;
//    cts.Cancel();
//};

//ConsoleStyle.WriteBanner();
//ConsoleStyle.WriteInfo("MiniRedis TCP Server started!");
//ConsoleStyle.WriteInfo("Listening on port 6379");

//// 3️⃣ Start server
//var serverTask = server.StartAsync(cts.Token);

//// ===============================
//// Bloqueia até shutdown
//// ===============================

//try
//{
//    await serverTask;
//}
//catch (OperationCanceledException)
//{
//    // shutdown normal
//}

//// ===============================
//// Shutdown limpo
//// ===============================

//ConsoleStyle.WriteInfo("Shutting down...");

//cacheRecovery.Stop();
//snapshotService.Save();
//server.Stop();

//ConsoleStyle.WriteInfo("MiniRedis stopped.");









using MiniRedis.Core.Cache;
using MiniRedis.Core.Commands;
using MiniRedis.Core.Persistence;
using MiniRedis.Server;

// ===============================
// Infra / Core
// ===============================

ICacheStore cache = new HashCacheStore();

var basePath = AppContext.BaseDirectory;
var snapshotPath = Path.Combine(basePath, "snapshot.json");

// Snapshot + recovery
var snapshotService = new SnapshotService(cache, snapshotPath);
var cacheRecovery = new CacheRecoveryService(
    snapshotService,
    TimeSpan.FromSeconds(10)
);

// ===============================
// Inicialização
// ===============================

// 1️⃣ Carrega snapshot inicial
snapshotService.Load();

// 2️⃣ Inicia snapshot periódico
cacheRecovery.Start();

// ===============================
// Commands
// ===============================

var parser = new CommandParser();
var factory = new CommandFactory();
CoreCommandRegistration.RegisterAll(factory, cache);
// ===============================
// TCP Server
// ===============================

var server = new TcpRedisServer(
    port: 6379,
    sessionFactory: () => new ClientSession(parser, factory)
);

// ===============================
// Lifecycle
// ===============================

using var cts = new CancellationTokenSource();

// CTRL + C
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

ConsoleStyle.WriteBanner();
ConsoleStyle.WriteInfo("MiniRedis TCP Server started!");
ConsoleStyle.WriteInfo("Listening on port 6379");

// 🔥 START DO SERVIDOR (BACKGROUND)
_ = server.StartAsync(cts.Token);

// ===============================
// Mantém app viva
// ===============================

Console.WriteLine("Press ENTER to shutdown...");
Console.ReadLine();

// ===============================
// Shutdown limpo
// ===============================

cts.Cancel();

cacheRecovery.Stop();
snapshotService.Save();
server.Stop();

ConsoleStyle.WriteInfo("MiniRedis stopped.");

