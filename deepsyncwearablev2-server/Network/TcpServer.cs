using Serilog;
using System.Net;
using System.Net.Sockets;

namespace DeepSyncWearableServer.Network
{
    public class TcpServer(
        string bindAddress, 
        int port,
        Func<TcpClient, CancellationToken, Task> clientHandler)
    {
        private readonly IPAddress _bindAddress = IPAddress.TryParse(bindAddress, out var ip) 
            ? ip 
            : throw new ArgumentException("Invalid IP address", nameof(bindAddress));
   
        private readonly int _port = port;

        private readonly Func<TcpClient, CancellationToken, Task> _clientHandler = 
            clientHandler ?? throw new ArgumentNullException(nameof(clientHandler));

        public async Task RunAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<TcpServer>();
            log.Information($"Listening on {_bindAddress}:{_port}");

            TcpListener listener = new(_bindAddress, _port);
            listener.Start();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(ct);

                    string clientIp = GetRemoteIp(client);
                    log.Information($"Client connected: {clientIp}");

                    _ = Task.Run(() => _clientHandler(client, ct), ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                listener.Stop();
                log.Information($"Stopped listening on port: {_port}");
            }
        }

        public static string GetRemoteIp(TcpClient client) =>
            client?.Client?.RemoteEndPoint is IPEndPoint ep
                ? ep.Address.ToString()
                : "UNKNOWN";

        public static bool VerifyIp(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }
    }
}