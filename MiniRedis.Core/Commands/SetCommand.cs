using System.Reflection.Metadata.Ecma335;
using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public class SetCommand : ICommand
    {
        public string[] Tokens { get; private set; }
        private readonly ICacheStore _store;
        public SetCommand(string[] tokens, ICacheStore store)
        {
            Tokens = tokens;
            _store = store;
        }

        public string Execute()
        {
            var key = Tokens[1];
            var value = Tokens[2];

            if (!int.TryParse(Tokens[3], out int minutes))
                return "TTL inválido";

            var ttl = TimeSpan.FromMinutes(minutes);

            _store.Set(key, value, ttl);
            return "Setado com sucesso";
        }
    }
}
