using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public class GetCommand : ICommand
    {

        public string[] Tokens { get; private set; }
        private readonly ICacheStore _store;
        public GetCommand(string[] tokens, ICacheStore store)
        {
            Tokens = tokens;
            _store = store;
        }

        public string Execute()
        {
            return _store.Get(Tokens[1]) ?? "Não existe valor";
        }
    }
}
