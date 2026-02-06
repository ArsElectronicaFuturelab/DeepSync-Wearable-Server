using Serilog;
using System.Net;
using System.Net.Sockets;

namespace DeepSyncWearableServer.Network
{
    public class TcpClientConnection(
        string serverAddress,
        int port,
        Func<TcpClient, CancellationToken, Task> connectionHandler)
    {

        private readonly IPAddress _serverAddress = IPAddress.TryParse(serverAddress, out var ip)
            ? ip
            : throw new ArgumentException("Invalid IP address", nameof(serverAddress));

        private readonly int _port = port;

        private readonly Func<TcpClient, CancellationToken, Task> _connectionHandler =
            connectionHandler ?? throw new ArgumentNullException(nameof(connectionHandler));

        public async Task RunAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<TcpClientConnection>();
            log.Information($"Connecting to {_serverAddress}:{_port}");

            using TcpClient client = new();

            try
            {
                await client.ConnectAsync(_serverAddress, _port, ct);
                log.Information($"Connected to {_serverAddress}:{_port}");

                await _connectionHandler(client, ct);
            }
            catch (OperationCanceledException)
            {
                log.Information("Client connection canceled");
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Failed to connect to {_serverAddress}:{_port}");
                throw;
            }
            finally
            {
                if (client.Connected)
                {
                    client.Close();
                }

                log.Information($"Client stopped for {_serverAddress}:{_port}");
            }
        }
    }
}