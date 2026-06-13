using System.Net;
using System.Net.Sockets;

namespace MiniRedis.Server
{
    public class TcpRedisServer
    {
        private readonly int _port;
        private readonly Func<ClientSession> _sessionFactory;
        private TcpListener? _listener;

        public TcpRedisServer(
            int port,
            Func<ClientSession> sessionFactory)
        {
            _port = port;
            _sessionFactory = sessionFactory;
        }

        public async Task StartAsync(CancellationToken token)
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            Console.WriteLine($"[TCP] Listening on port {_port}");

            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                Console.WriteLine($"[TCP] Cliente conectado de {client.Client.RemoteEndPoint}");

                _ = Task.Run(() =>
                {
                    var session = _sessionFactory();
                    return session.HandleAsync(client, token);
                }, token);
            }
        }

        public void Stop()
        {
            _listener?.Stop();
        }
    }
}
