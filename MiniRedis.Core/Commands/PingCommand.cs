using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public class PingCommand : ICommand
    {
        public PingCommand()
        {
        }

        public string Execute()
        {
            return "pong";

        }
    }
}
