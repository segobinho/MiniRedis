namespace MiniRedis.Core.Commands
{
    public class CommandFactory
    {
        private readonly Dictionary<string, Func<string[], ICommand>> _commands =
            new(StringComparer.OrdinalIgnoreCase);

        public ICommand Create(string[] tokens)
        {
            if (tokens.Length == 0)
                return new ErrorCommand("ERR empty command");

            var name = tokens[0];

            if (!_commands.TryGetValue(name, out var factory))
                return new ErrorCommand($"ERR unknown command '{name}'");


            return factory(tokens);
        }

        public void Register(string name, Func<string[], ICommand> factory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Command name inválido", nameof(name));

            _commands[name] = factory;
        }
    }
}
