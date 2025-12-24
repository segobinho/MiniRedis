namespace MiniRedis.Server
{
    public static class ConsoleStyle
    {
        public static void WritePrompt()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("127.0.0.1:6379> ");
            Console.ResetColor();
        }

        public static void WriteInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public static void WriteResult(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"(error) {message}");
            Console.ResetColor();
        }

        public static void WriteBanner()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(@"
 __  __ _       _ ____          _
|  \/  (_)_ __ (_)  _ \ ___  __| |_ ___
| |\/| | | '_ \| | |_) / _ \/ _` | / __|
| |  | | | | | | |  _ <  __/ (_| | \__ \
|_|  |_|_|_| |_|_|_| \_\___|\__,_|_|___/

            MiniRedis
");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("            by Segobi\n");
            Console.ResetColor();
        }
    }
}
