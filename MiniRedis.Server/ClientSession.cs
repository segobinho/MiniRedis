using System.Net.Sockets;
using System.Text;
using MiniRedis.Core.Commands;

namespace MiniRedis.Server
{
    public class ClientSession
    {
        private readonly CommandParser _parser;
        private readonly CommandFactory _factory;

        public ClientSession(
            CommandParser parser,
            CommandFactory factory)
        {
            _parser = parser;
            _factory = factory;
        }

        public async Task HandleAsync(TcpClient client,CancellationToken token)
        {
            Console.WriteLine(
      $"[SESSION] Sessão iniciada ({client.Client.RemoteEndPoint})"
  );
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync("MiniRedis TCP ready");

            while (!token.IsCancellationRequested && client.Connected)
            {
                ConsoleStyle.WritePrompt();
                var line = await reader.ReadLineAsync();

                if (line == null)
                    break;

                try
                {
                    var tokens = _parser.Parse(line);
                    var command = _factory.Create(tokens);
                    var result = command.Execute();
                    ConsoleStyle.WriteResult(result);
                    await writer.WriteLineAsync(result);
                }
                catch (Exception ex)
                {
                    await writer.WriteLineAsync($"ERR {ex.Message}");
                }
            }
        }
    }
}
