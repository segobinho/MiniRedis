using MiniRedis.Core.Cache;

namespace MiniRedis.Core.Commands
{
    public class ClearCommand : ICommand
    {
        private readonly ICacheStore _store;

        public ClearCommand(ICacheStore store)
        {
            _store = store;
        }

        public string Execute()
        {
            _store.Clear();
            return "Cache limpado com sucesso";
        }
    }
}
