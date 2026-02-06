using Serilog;
using System.Diagnostics;
using DeepSyncWearableServer.Protocol.Data;

namespace DeepSyncWearableServer.Wearable
{
    public class SimulatedWearableConfig(
        string ip,
        int id,
        Color color,
        int baseHeartRate,
        double amplitude,
        double speedHz,
        double interval)
    {
        public string Ip { get; set; } = ip;
        public int Id { get; } = id;
        public Color Color { get; } = color;
        public int BaseHeartRate { get; } = baseHeartRate;
        public double Amplitude { get; } = amplitude;
        public double SpeedHz { get; } = speedHz;
        public double IntervalMs { get; } = interval;
    }

    public class SimulatedWearable()
    {
        private readonly object _stateLock = new();

        private string _ip = "";
        private int _id = -1;
        private Color _color = RandomColor();
        private int _baseHeartRate = 60;
        private double _noiseAmplitude;
        private double _noiseSpeedHz;
        private double _sendIntervalMs = 100.0;
        private WearableData? _latestData;

        public Task RunAsync(CancellationToken ct) => UpdateLoopAsync(ct);

        public void SetIp(string ip)
        {
            lock (_stateLock)
            {
                _ip = ip;
            }
        }

        public string GetIp()
        {
            lock (_stateLock)
            {
                return _ip;
            }
        }

        public void SetId(int id)
        {
            lock (_stateLock)
            {
                _id = id;
            }
        }

        public void SetColor(Color color)
        {
            lock (_stateLock)
            {
                _color = color;
            }
        }

        public void SetHeartRate(int heartRate)
        {
            lock (_stateLock)
            {
                _baseHeartRate = heartRate;
            }
        }

        public void SetHeartRateNoise(double amplitude, double speedHz)
        {
            lock (_stateLock)
            {
                _noiseAmplitude = amplitude;
                _noiseSpeedHz = speedHz;
            }
        }

        public void SetSendInterval(double interval)
        {
            lock (_stateLock)
            {
                _sendIntervalMs = interval;
            }
        }

        internal SimulatedWearableConfig GetConfig()
        {
            lock (_stateLock)
            {
                return new SimulatedWearableConfig(
                    _ip,
                    _id,
                    _color,
                    _baseHeartRate,
                    _noiseAmplitude,
                    _noiseSpeedHz,
                    _sendIntervalMs
                );
            }
        }

        internal bool TryGetLatestData(out WearableData? data)
        {
            lock (_stateLock)
            {
                data = _latestData;
                return data != null;
            }
        }

        internal void ApplyCommand(WearableCommand cmd)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<SimulatedWearable>();

            switch (cmd)
            {
                case NewIdCmd newId:
                    SetId(newId.NewId);
                    log.Information($"Simulated wearable received new ID: {newId.NewId}");
                    break;

                case ColorCmd colorCmd:
                    SetColor(colorCmd.Color);
                    log.Information($"Simulated wearable received new color: {colorCmd.Color}");
                    break;

                default:
                    log.Warning($"Simulated wearable received unknown command: {cmd}");
                    break;
            }
        }

        private async Task UpdateLoopAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<SimulatedWearable>();

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    SimulatedWearableConfig cfg = GetConfig();

                    int heartRate = ComputeHeartRate(
                        cfg.BaseHeartRate,
                        cfg.Amplitude,
                        cfg.SpeedHz,
                        sw.Elapsed
                    );

                    int elapsedMs = (int)sw.Elapsed.TotalMilliseconds;
                    WearableData data = new(cfg.Id)
                    {
                        Id = cfg.Id,
                        HeartRate = heartRate,
                        Color = cfg.Color,
                        Timestamp = elapsedMs
                    };

                    lock (_stateLock)
                    {
                        _latestData = data;
                    }

                    //log.Debug("Simulated wearable updated data: {@Data}", data);
                    await Task.Delay(TimeSpan.FromMilliseconds(cfg.IntervalMs), ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error(ex, "Simulated wearable update loop failed");
            }
        }

        private static int ComputeHeartRate(
            int baseHr, double amplitude, double speedHz, TimeSpan elapsed)
        {
            double hr = baseHr;
            if (amplitude != 0d && speedHz != 0d)
            {
                hr += amplitude * Math.Sin(2 * Math.PI * speedHz * elapsed.TotalSeconds);
            }

            int rounded = (int)Math.Round(hr);
            return rounded < 0 ? 0 : rounded;
        }

        private static Color RandomColor()
        {
            return new Color(
                (byte)Random.Shared.Next(0, 256),
                (byte)Random.Shared.Next(0, 256),
                (byte)Random.Shared.Next(0, 256));
        }
    }
}