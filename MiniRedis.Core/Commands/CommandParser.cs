namespace MiniRedis.Core.Commands
{
    public class CommandParser
    {
        public string[] Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            return input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
