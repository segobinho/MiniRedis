
namespace MiniRedis.Core.Commands
{
    internal class ErrorCommand : ICommand
    {
        private readonly string _message;

        public ErrorCommand(string message)
        {
            _message = message;
        }

        //public CommandResult Execute()
        //{
        //    return new CommandResult(false, _message);
        //}

        string ICommand.Execute()
        {
            return _message;
        }
    }
}
