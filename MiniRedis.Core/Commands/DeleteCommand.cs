using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public class DeleteCommand : ICommand
    {
        public string[] Tokens { get; private set; }
        private readonly ICacheStore _store;

        public DeleteCommand(string[] tokens, ICacheStore store)
        {
            Tokens = tokens;
            _store = store;
        }

        public string Execute()
        {
            var key = Tokens[1];
            _store.Delete(key);
            return "Cache removido com sucesso";
        }
    }
}
