using MiniRedis.Core.Cache;
using MiniRedis.Core.Commands;
using MiniRedis.Core.Persistence;
using MiniRedis.Server;

ICacheStore cache = new HashCacheStore();
var basePath = AppContext.BaseDirectory;
var snapshotPath = Path.Combine(basePath, "snapshot.json");
var snapshot = new SnapshotService( cache, snapshotPath);

// Na inicialização
snapshot.Load();


var parser = new CommandParser();
var factory = new CommandFactory();

CoreCommandRegistration.RegisterAll(factory, cache);

ConsoleStyle.WriteBanner();
ConsoleStyle.WriteInfo("MiniRedis Server started!");
ConsoleStyle.WriteInfo("Type commands like: GET key");
ConsoleStyle.WriteInfo("Type 'exit' to quit.\n");

while (true)
{
    ConsoleStyle.WritePrompt();

    var input = Console.ReadLine();

    if (input is null)
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        var tokens = parser.Parse(input);
        var command = factory.Create(tokens);

        var result = command.Execute();
        ConsoleStyle.WriteResult(result);
    }
    catch (Exception ex)
    {
        ConsoleStyle.WriteError(ex.Message);
    }
}

snapshot.Save();