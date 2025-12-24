using MiniRedis.Core.Cache;
using MiniRedis.Core.Commands;

ICacheStore cache = new HashCacheStore();
var parser = new CommandParser();
var factory = new CommandFactory();

CoreCommandRegistration.RegisterAll(factory, cache);

Console.WriteLine("MiniRedis Server started!");
Console.WriteLine("Digite comandos (ex: get chave)");
Console.WriteLine("Digite 'exit' para sair.\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (input is null)
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var tokens = parser.Parse(input);
    var command = factory.Create(tokens);

    var result = command.Execute();
    Console.WriteLine(result);
}