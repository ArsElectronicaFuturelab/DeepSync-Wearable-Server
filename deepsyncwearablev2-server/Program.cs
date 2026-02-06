using Serilog;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeepSyncWearableServer.Network;
using DeepSyncWearableServer.Protocol;
using DeepSyncWearableServer.Protocol.Data;
using DeepSyncWearableServer.Wearable;
using DeepSyncWearableServer.Frontend;

namespace DeepSyncWearableServer
{
    public static class LoggerExtensions
    {
        public static ILogger WithClassAndMethodNames<T>(this ILogger logger,
            [CallerMemberName] string memberName = "")
        {
            var className = typeof(T).Name;
            return logger.ForContext("ClassName", className).ForContext("MethodName", memberName);
        }
    }

    internal class DeepSyncServer
    {
        private static string _tcpWearable_address = "0.0.0.0";
        private static int _tcpWearable_localPort = 53397;

        private static string _tcpApp_address = "0.0.0.0";
        private static int _tcpApp_localPort = 43397;
        private static int _tcpApp_remotePort = 43396;

        private static readonly ConcurrentDictionary<string, WearableData> _wearableDataByIp = new();

        private static readonly ConcurrentDictionary<string, ConcurrentQueue<WearableCommand>> _wearableCmdQueueByIp = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _wearableSendSignalByIp = new();

        private static readonly string _wearableColorConfig = "wearable_colors.json";

        private static readonly WearableProtocol _wearableProtocol = new();
        private static readonly AppProtocol _appProtocol = new();

        private static ConcurrentDictionary<int, ColorCmd> _colorConfigQueue = new();

        private static readonly ConcurrentDictionary<string, int> _assignedIdsByIp = new();

        private static readonly string _frontendRestUrl =
            Environment.GetEnvironmentVariable("FRONTEND_REST") ?? "http://localhost:8788/api/wearables";
        private static readonly string _frontendSimRestUrl =
            Environment.GetEnvironmentVariable("FRONTEND_SIM_REST") ?? (_frontendRestUrl.TrimEnd('/') + "/simulated");
        private static readonly string _frontendControlListenUrl =
            Environment.GetEnvironmentVariable("FRONTEND_CTRL") ?? "http://localhost:8790/api/control";

        private static readonly TimeSpan _frontendUpdateInterval = TimeSpan.FromSeconds(0.1);

        private static FrontendSender? _frontendBridge;
        private static FrontendControlServer? _frontendControlServer;

        private static SimulatedWearableManager? _simulatedManager;
        private static readonly TimeSpan _simulatedUpdateInterval = TimeSpan.FromMilliseconds(100);

        private static CancellationToken _shutdownToken = CancellationToken.None;

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("   --ip-app <Ip>         ... interface to use for app connection");
            Console.WriteLine("   --port-app  <p>       ... port to use for app connection");
            Console.WriteLine("   --ip-wearable <Ip>    ... interface to use for wearable connection");
            Console.WriteLine("   --port-wearable  <p>  ... port to use for wearable connection");
            Console.WriteLine("   -d                    ... allow simualted wearables");
            //Console.WriteLine("   -v                    ... verbose logging");
        }

        static bool TryParseArgs(string[] args, out bool simulateWearables)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            simulateWearables = false;
            bool gotIpa = false, gotPa = false, gotIpw = false, gotPw = false;

            for (int idx = 0; idx < args.Length; idx++)
            {
                string arg = args[idx];

                switch (arg)
                {
                    case "-d":
                        simulateWearables = true;
                        log.Information($"Allow simulated wearables");
                        break;

                    case "--ip-app":
                        if (!TryGetNextArg(args, ref idx, out string ipApp)) return false;
                        if (!TcpServer.VerifyIp(ipApp))
                            throw new ArgumentException("Invalid Application interface!");

                        _tcpApp_address = ipApp;
                        gotIpa = true;

                        log.Information($"Using interface for application communication: {_tcpApp_address}");
                        break;

                    case "--port-app":
                        if (!TryGetNextArg(args, ref idx, out string portApp)) return false;
                        if (!int.TryParse(portApp, out int res_pa))
                            throw new ArgumentException("Invalid application port!");

                        _tcpApp_localPort = res_pa;
                        gotPa = true;

                        log.Information($"Local port for application communication: {_tcpApp_localPort}");
                        break;

                    case "--ip-wearable":
                        if (!TryGetNextArg(args, ref idx, out string ipWearable)) return false;
                        if (!TcpServer.VerifyIp(ipWearable))
                            throw new ArgumentException("Invalid wearable interface!");

                        _tcpWearable_address = ipWearable;
                        gotIpw = true;

                        log.Information($"Using interface for wearable communication: {_tcpWearable_address}");
                        break;

                    case "--port-wearable":
                        if (!TryGetNextArg(args, ref idx, out string portWearable)) return false;
                        if (!int.TryParse(portWearable, out int res_pw))
                            throw new ArgumentException("Invalid wearable port!");

                        _tcpWearable_localPort = res_pw;
                        gotPw = true;

                        log.Information($"Local port for wearable communication: {_tcpWearable_localPort}");
                        break;

                    default:
                        return false;
                }
            }

            bool appArgsOk = gotIpa && gotPa;
            bool wearableArgsOk = simulateWearables || (gotIpw && gotPw);

            return appArgsOk && wearableArgsOk;
        }

        static bool TryGetNextArg(string[] args, ref int idx, out string value)
        {
            value = "";
            if (idx + 1 >= args.Length) return false;
            value = args[++idx];
            return true;
        }

        public static async Task Main(string[] args)
        {
            string logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] " +
                    "({ClassName}.{MethodName}) {Message}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: logTemplate)
                .WriteTo.File("logs/desy-server-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: logTemplate)
                .CreateLogger();

            if (!TryParseArgs(args, out bool simulateWearables))
            {
                PrintUsage();
                return;
            }

            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();
            log.Information("Frontend REST endpoints: data='{DataEndpoint}', simulated='{SimEndpoint}'",
                _frontendRestUrl, _frontendSimRestUrl);

            LoadWearableColors(_wearableColorConfig);

            // -----------------------------
            // task management

            using CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            _shutdownToken = cts.Token;


            _frontendBridge = new FrontendSender(
                _frontendRestUrl,
                _frontendSimRestUrl
            );

            _frontendControlServer = new FrontendControlServer(
                _frontendControlListenUrl,
                HandleFrontendUpdate
            );

            TcpServer wearableServer = new(
                _tcpWearable_address,
                _tcpWearable_localPort,
                HandleWearableClientAsync
            );

            TcpServer appWriterServer = new(
                _tcpApp_address,
                _tcpApp_localPort,
                HandleAppClientWriterAsync
            );

            TcpServer appReaderServer = new(
                _tcpApp_address,
                _tcpApp_remotePort,
                HandleAppClientReaderAsync
            );


            List<Task> tasks = new()
            {
                CheckForWearableTimeoutAsync(cts.Token),
                FrontendWearableUpdateLoopAsync(cts.Token),
                _frontendControlServer.RunAsync(cts.Token),
                wearableServer.RunAsync(cts.Token),
                appWriterServer.RunAsync(cts.Token),
                appReaderServer.RunAsync(cts.Token),
            };

            // only if we allow simulated wearables, we start simulation manager and frontend server
            if (simulateWearables)
            {
                _simulatedManager = new SimulatedWearableManager();
                tasks.Add(SimulatedWearableUpdateLoopAsync(cts.Token));
            }

            await Task.WhenAll(tasks);
            await Log.CloseAndFlushAsync();
        }

        private static void LoadWearableColors(string file)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            if (!File.Exists(file)) return;
            log.Information($"Loading werable colors from file: {file}");

            try
            {
                JsonSerializerOptions jsonSerializerOptions = new()
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                string stream = File.ReadAllText(file);
                List<ColorCmd>? colors =
                    JsonSerializer.Deserialize<List<ColorCmd>>(stream, jsonSerializerOptions);

                if (colors == null)
                {
                    log.Warning("Could not load wearable colors: Deserialized color list was null!");
                    return;
                }

                foreach (ColorCmd colorCmd in colors)
                {
                    log.Information($"Color for {colorCmd.Id}: " +
                        $"{colorCmd.Color.R}, {colorCmd.Color.G}, {colorCmd.Color.B}");

                    _colorConfigQueue.TryAdd(colorCmd.Id, colorCmd);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Failed to deserialize JSON from file '{file}'");
            }
        }

        static async Task CheckForWearableTimeoutAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();
            log.Information("SimulatedWearable timeout detective started");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);

                    foreach ((string ip, WearableData wearableData) in _wearableDataByIp.ToArray())
                    {
                        if (wearableData == null || !wearableData.Stale) continue;

                        log.Warning($"SimulatedWearable {ip}: Stale, removing wearable");

                        _wearableDataByIp.TryRemove(ip, out _);
                        _assignedIdsByIp.TryRemove(ip, out _);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.Error(ex, "SimulatedWearable timeout detective failed");
            }

            log.Information("SimulatedWearable timeout detective stopped");
        }

        static async Task HandleWearableClientAsync(TcpClient client, CancellationToken serverCt)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"SimulatedWearable {ip}: Starting main worker task");

            using CancellationTokenSource? linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            CancellationToken ct = linkedCts.Token;

            using (client)
            {
                Task? reader = null;
                Task? writer = null;
                //Task? detective = null;
                NetworkStream? stream = null;

                _wearableSendSignalByIp.TryAdd(ip, new SemaphoreSlim(0));
                _wearableCmdQueueByIp.TryAdd(ip, new ConcurrentQueue<WearableCommand>());

                try
                {
                    client.NoDelay = true;
                    stream = client.GetStream();

                    reader = ReadWearableDataTask(client, stream, ct);
                    writer = WriteWearableDataTask(client, stream, ct);
                    //detective = CheckForWearableTimeout(ip, linkedCts, ct);

                    await Task.WhenAny(reader, writer);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    log.Error(ex, $"SimulatedWearable {ip}: Client handler error");
                }
                finally
                {
                    try { linkedCts.Cancel(); } catch { }

                    try { client.Client?.Shutdown(SocketShutdown.Both); } catch { }
                    try { stream?.Close(); } catch { }
                    try { client.Close(); } catch { }

                    var tasks = new[] { reader, writer }.Where(t => t != null).ToArray();

                    try { await Task.WhenAll(tasks!); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        log.Error(ex, $"SimulatedWearable {ip}: Worker shutdown error");
                    }

                    _wearableDataByIp.TryRemove(ip, out _);
                    _assignedIdsByIp.TryRemove(ip, out _);

                    log.Information($"SimulatedWearable {ip}: Disconnected");
                }
            }
        }

        static async Task ReadWearableDataTask(
            TcpClient client, Stream stream, CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"SimulatedWearable {ip}: Data reader started");

            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            char[] buf = new char[4096];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int cnt = await reader.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                    if (cnt == 0) break;

                    _wearableProtocol.Push(buf, 0, cnt);
                    WearableData? data = _wearableProtocol.DecodeData();

                    if (data == null) continue;
                    UpdateWearableData(ip, data);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, $"SimulatedWearable {ip}: Error");
            }

            log.Information($"SimulatedWearable {ip}: Data reader stopped");
        }

        private static void UpdateWearableData(string ip, WearableData data)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            // > what should happen when the IP is already known but the ID is different?
            //      * in this case we could discard the data and send a change id command instead
            //      * could potentially be the case when the wearable restarts within the timeout
            //        window while keeping the same IP

            _wearableDataByIp.AddOrUpdate(ip, data, (key, existing) =>
            {
                existing.Id = data.Id;
                existing.HeartRate = data.HeartRate;
                existing.Color = data.Color;
                existing.Timestamp = data.Timestamp;
                existing.TimestampInternal = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return existing;
            });

            _assignedIdsByIp.AddOrUpdate(ip, data.Id, (key, existing) =>
            {
                if (existing != data.Id)
                {
                    log.Warning($"SimulatedWearable {ip}: Assigned ID changed from " +
                        $"{existing} to {data.Id}!");
                }

                existing = data.Id;
                return existing;
            });

            if (_colorConfigQueue.TryGetValue(data.Id, out ColorCmd? colorCmd))
            {
                if (data.Color.R != colorCmd.Color.R
                    || data.Color.G != colorCmd.Color.G
                    || data.Color.B != colorCmd.Color.B)
                {
                    if (TryEnqueueWearableCommand(ip, new ColorCmd(data.Id, colorCmd.Color)))
                    {
                        log.Debug($"SimulatedWearable {ip}: Enqueued color command: {colorCmd.Color}");
                    }
                    else
                    {
                        log.Warning($"SimulatedWearable {ip}: Failed to enqueue " +
                            $"color command: {colorCmd.Color}");
                    }
                }
            }
        }

        static async Task WriteWearableDataTask(
            TcpClient client, Stream stream, CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"SimulatedWearable {ip}: Data writer started");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _wearableSendSignalByIp.TryGetValue(ip, out var sem);
                    _wearableCmdQueueByIp.TryGetValue(ip, out var queue);

                    if (sem == null || queue == null)
                    {
                        log.Warning($"SimulatedWearable {ip}: No wearable command queue or send signal found!");
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    await sem.WaitAsync(ct);
                    while (queue.TryDequeue(out WearableCommand? cmd))
                    {
                        if (cmd == null)
                        {
                            log.Warning($"SimulatedWearable {ip}: Write triggered without command, skipping!");
                            continue;
                        }

                        string dataStr = _wearableProtocol.EncodeCommand(cmd);
                        if (string.IsNullOrWhiteSpace(dataStr))
                        {
                            log.Warning($"SimulatedWearable {ip}: Encoded command was empty, skipping!");
                            continue;
                        }

                        byte[] dataBytes = Encoding.UTF8.GetBytes(dataStr);
                        await stream.WriteAsync(dataBytes.AsMemory(0, dataBytes.Length), ct);

                        log.Debug($"SimulatedWearable {ip}: Sent command to wearable: {cmd}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, $"SimulatedWearable {ip}: Error");
            }

            _wearableCmdQueueByIp.TryRemove(ip, out _);
            _wearableSendSignalByIp.TryRemove(ip, out _);

            log.Information($"SimulatedWearable {ip}: Data reader stopped");
        }

        static async Task HandleAppClientWriterAsync(TcpClient client, CancellationToken serverCt)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"App {ip}: Starting main worker task");

            using CancellationTokenSource? linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            CancellationToken ct = linkedCts.Token;

            using (client)
            {
                Task? writer = null;
                NetworkStream? stream = null;

                try
                {
                    client.NoDelay = true;
                    stream = client.GetStream();

                    writer = WriteAppDataTask(client, stream, ct);

                    await Task.WhenAny(writer);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    log.Error(ex, $"SimulatedWearable {ip}: Client handler error");
                }
                finally
                {
                    try { linkedCts.Cancel(); } catch { }

                    try { client.Client?.Shutdown(SocketShutdown.Both); } catch { }
                    try { stream?.Close(); } catch { }
                    try { client.Close(); } catch { }

                    var tasks = new[] { writer }.Where(t => t != null).ToArray();

                    try { await Task.WhenAll(tasks!); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        log.Error(ex, $"App {ip}: Worker shutdown error");
                    }

                    log.Information($"App {ip}: Disconnected");
                }
            }
        }

        static async Task WriteAppDataTask(TcpClient client, Stream stream, CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"App {ip}: Data writer started");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    foreach ((string wearableIp, WearableData wearableData) in _wearableDataByIp.ToArray())
                    {
                        if (wearableData == null)
                        {
                            log.Warning($"App {ip}: SimulatedWearable data empty, skipping!");
                            continue;
                        }

                        string dataStr = _appProtocol.EncodeData(wearableData);
                        if (string.IsNullOrWhiteSpace(dataStr))
                        {
                            log.Warning($"App {ip}: Encoded data was empty, skipping!");
                            continue;
                        }

                        byte[] dataBytes = Encoding.UTF8.GetBytes(dataStr);
                        await stream.WriteAsync(dataBytes.AsMemory(0, dataBytes.Length), ct);

                        log.Debug($"App {ip}: Sent data to app: {wearableData}");
                    }

                    await Task.Delay(100, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, $"App {ip}: Error");
            }

            log.Information($"App {ip}: Data writer stopped");
        }

        static async Task HandleAppClientReaderAsync(TcpClient client, CancellationToken serverCt)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"App {ip}: Starting main worker task");

            using CancellationTokenSource? linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            CancellationToken ct = linkedCts.Token;

            using (client)
            {
                Task? reader = null;
                NetworkStream? stream = null;

                try
                {
                    client.NoDelay = true;
                    stream = client.GetStream();

                    reader = ReadAppDataTask(client, stream, ct);

                    await Task.WhenAny(reader);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    log.Error(ex, $"App {ip}: Client handler error");
                }
                finally
                {
                    try { linkedCts.Cancel(); } catch { }

                    try { client.Client?.Shutdown(SocketShutdown.Both); } catch { }
                    try { stream?.Close(); } catch { }
                    try { client.Close(); } catch { }

                    var tasks = new[] { reader }.Where(t => t != null).ToArray();

                    try { await Task.WhenAll(tasks!); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        log.Error(ex, $"App {ip}: Worker shutdown error");
                    }

                    log.Information($"App {ip}: Disconnected");
                }
            }
        }

        static async Task ReadAppDataTask(TcpClient client, Stream stream, CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            string ip = TcpServer.GetRemoteIp(client);
            log.Information($"App {ip}: Data reader started");

            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            char[] buf = new char[4096];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int cnt = await reader.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                    if (cnt == 0) break;

                    _appProtocol.Push(buf, 0, cnt);
                    WearableCommand? cmd = _appProtocol.DecodeCommand();

                    if (cmd == null) continue;
                    UpdateWearableCommand(ip, cmd);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, $"App {ip}: Error");
            }

            log.Information($"App {ip}: Data reader stopped");
        }

        private static void UpdateWearableCommand(string ip, WearableCommand cmd)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            List<KeyValuePair<string, int>> matches = _assignedIdsByIp
                .Where(kvp => kvp.Value == cmd.Id)
                .ToList();

            if (matches.Count == 0)
            {
                log.Warning($"App {ip}: Command for '{cmd.Id}' not in assigned IDs, dropping command!");
                return;
            }

            if (matches.Count > 1)
            {
                string ips = string.Join(", ", matches.Select(kvp => kvp.Key));
                log.Warning($"App {ip}: Command for '{cmd.Id}' matched " +
                    $"multiple wearables ({ips}), dropping command!");
                return;
            }

            string wearableIp = matches[0].Key;
            if (TryEnqueueWearableCommand(wearableIp, cmd))
            {
                log.Debug($"App {ip}: Enqueued command for wearable {wearableIp}: {cmd}");
            }
            else
            {
                log.Warning($"App {ip}: Failed to enqueue command for wearable {wearableIp}!");
            }
        }

        private static bool TryEnqueueWearableCommand(string ip, WearableCommand cmd)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            if (!_wearableCmdQueueByIp.ContainsKey(ip)
                || !_wearableSendSignalByIp.ContainsKey(ip))
            {
                if (_simulatedManager != null && _simulatedManager.TryApplyCommand(ip, cmd))
                {
                    log.Debug($"SimulatedWearable {ip}: Applied command to simulated wearable: {cmd}");
                    return true;
                }

                log.Warning($"SimulatedWearable {ip}: No wearable command queue or " +
                    $"send signal found, skipping color config!");
                return false;
            }

            _wearableCmdQueueByIp.TryGetValue(ip, out var queue);
            _wearableSendSignalByIp.TryGetValue(ip, out var sem);
            if (queue == null || sem == null)
            {
                log.Warning($"SimulatedWearable {ip}: SimulatedWearable command queue or send signal was " +
                    $"null, skipping color config!");
                return false;
            }

            queue.Enqueue(cmd);
            sem.Release();
            return true;
        }

        private static void HandleFrontendUpdate(
            FrontendSimulatedWearableConfig config, CancellationToken cs)
        {
            if (config == null) return;
            if (_simulatedManager == null || _frontendBridge == null) return;
            if (string.IsNullOrWhiteSpace(config.Action)) return;

            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();

            //SimulatedWearableConfig simConfig = new(
            //    config.Ip,
            //    config.Id,
            //    config.Color,
            //    config.BaseHeartRate,
            //    config.Amplitude,
            //    config.SpeedHz,
            //    config.IntervalMs
            //);
            //simConfig.Ip = config.Ip;

            switch (config.Action.ToLowerInvariant())
            {
                case "create":
                    {
                        log.Information($"Create request for simulated wearable");
                        if (!_simulatedManager.TryCreateAndStart(_shutdownToken))
                        {
                            log.Warning($"Create request for simulated wearable failed");
                        }
                    }
                    break;

                case "update":
                    if (string.IsNullOrWhiteSpace(config.Ip))
                    {
                        log.Warning("Update request for simulated wearable had no valid IP");
                        break;
                    }

                    SimulatedWearableConfig simConfig = new(
                        config.Ip,
                        config.Id,
                        config.Color,
                        config.BaseHeartRate,
                        config.Amplitude,
                        config.SpeedHz,
                        config.IntervalMs
                    );

                    log.Information($"Updated request of simulated wearable {config.Ip}");
                    if (!_simulatedManager.TryApplyConfig(config.Ip, simConfig))
                    {
                        log.Warning($"Update request for simulated wearable '{config.Ip}' "+
                            "had no matching IP in manager");
                    }
                    break;

                case "delete":
                    if (string.IsNullOrWhiteSpace(config.Ip))
                    {
                        log.Warning("Delete request for simulated wearable had no valid IP");
                        break;
                    }

                    log.Information($"Remove request of simulated wearable {config.Ip}");
                    if (!_simulatedManager.TryRemove(config.Ip))
                    {
                        log.Warning($"Delete request for simulated wearable '{config.Ip}' " +
                            "had no matching IP in manager");
                    }
                    break;

                default:
                    log.Warning($"Unknown frontend action: {config.Action}");
                    return;
            }
        }

        //private static FrontendSimulatedWearable MapSimulatedWearableConfig(
        //    SimulatedWearableConfig cfg,
        //    bool deleted)
        //{
        //    return new FrontendSimulatedWearable
        //    {
        //        Id = cfg.Id,
        //        BaseHeartRate = cfg.BaseHeartRate,
        //        Amplitude = cfg.Amplitude,
        //        SpeedHz = cfg.SpeedHz,
        //        IntervalMs = cfg.Interval.TotalMilliseconds,
        //        Color = cfg.Color,
        //    };
        //}

        private static async Task SimulatedWearableUpdateLoopAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();
            log.Information("Simulated wearables config loop started");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_simulatedManager != null)
                    {
                        foreach ((string wearableIp, WearableData? data)
                            in _simulatedManager.GetLatestWearableData())
                        {
                            if (data == null)
                            {
                                log.Warning($"Simulated SimulatedWearable {wearableIp}: No data, skipping!");
                                continue;
                            }

                            UpdateWearableData(wearableIp, data);
                        }
                    }

                    await Task.Delay(_simulatedUpdateInterval, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, "Simulated wearables config loop failed");
            }

            log.Information("Simulated wearables config loop stopped");
        }

        private static async Task FrontendWearableUpdateLoopAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<DeepSyncServer>();
            log.Information("Frontend wearable update loop started");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_frontendBridge == null)
                    {
                        log.Warning("Frontend bridge not initialized, skipping config!");
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                        continue;
                    }

                    WearableData[] snapshot = _wearableDataByIp.Values.ToArray();

                    try
                    {
                        await _frontendBridge.SendWearableDataAsync(snapshot, ct);

                        if (_simulatedManager != null)
                        {
                            SimulatedWearableConfig[] simConfigs =
                                _simulatedManager.GetSimulatedWearablesConfigs();

                            //log.Debug("Simulated wearable configs: {@Configs}", simConfigs);
                            await _frontendBridge.SendSimulatedWearableConfigAsync(simConfigs, ct);
                        }
                    }
                    catch (HttpRequestException)
                    {
                        log.Warning("Frontend potentially not running");
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                        continue;
                    }

                    await Task.Delay(_frontendUpdateInterval, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, "Frontend wearable update loop failed");
            }

            log.Information("Frontend wearable update loop stopped");
        }
    }
}
