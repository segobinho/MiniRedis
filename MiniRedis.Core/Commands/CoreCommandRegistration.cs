using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public static class CoreCommandRegistration
    {
        public static void RegisterAll(
            CommandFactory factory,
            ICacheStore store)
        {
            factory.Register("get", tokens =>
                new GetCommand(tokens, store));

            factory.Register("set", tokens =>
                new SetCommand(tokens, store));

            factory.Register("delete", tokens =>
                new DeleteCommand(tokens, store));

            factory.Register("clear", _ =>
                 new ClearCommand(store));

            factory.Register("ping", _ => new PingCommand());
        }
    }
}
