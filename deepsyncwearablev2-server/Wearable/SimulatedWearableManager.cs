using System.Collections.Concurrent;
using DeepSyncWearableServer.Protocol.Data;

namespace DeepSyncWearableServer.Wearable
{
    internal sealed class SimulatedWearableManager
    {
        private readonly ConcurrentDictionary<string, SimulationInstance> _instances = new();
        private int _idSeed = 1000;

        private static string BuildIp(int id) => $"simulated-{id}";

        public bool TryCreateAndStart(CancellationToken ct)
        {
            SimulatedWearable simulatedWearable = new();
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            string simIp = BuildIp(Interlocked.Increment(ref _idSeed));
            //simulationConfig.Ip = simIp;
            //ApplySettings(simulatedWearable, simulationConfig);
            SimulationInstance instance = new(simulatedWearable, cts);
            instance.SimulatedWearable.SetIp(simIp);

            if (!_instances.TryAdd(simIp, instance))
            {
                cts.Dispose();
                return false;
            }

            _ = Task.Run(() => simulatedWearable.RunAsync(cts.Token), ct);
            return true;
        }

        public bool TryApplyConfig(
            string ip,
            SimulatedWearableConfig config)
        {
            if (_instances.TryGetValue(ip, out SimulationInstance? instance))
            {
                ApplySettings(instance.SimulatedWearable, config);
                return true;
            }

            return false;
        }

        public bool TryRemove(string simIp)
        {
            if (_instances.TryRemove(simIp, out SimulationInstance? instance))
            {
                instance.Cancellation.Cancel();
                instance.Cancellation.Dispose();
                return true;
            }

            return false;
        }

        public IEnumerable<(string Ip, WearableData? Data)> GetLatestWearableData()
        {
            foreach ((_, SimulationInstance instance) in _instances)
            {
                if (instance.SimulatedWearable.TryGetLatestData(out WearableData? data))
                {
                    yield return (instance.SimulatedWearable.GetIp(), data);
                }
            }
        }

        public bool TryApplyCommand(string ip, WearableCommand cmd)
        {
            foreach ((_, SimulationInstance instance) in _instances)
            {
                if (instance.SimulatedWearable.GetIp() == ip)
                {
                    instance.SimulatedWearable.ApplyCommand(cmd);
                    return true;
                }
            }

            return false;
        }

        public SimulatedWearableConfig[] GetSimulatedWearablesConfigs()
        {
            return _instances.Values
                .Select(instance => instance.SimulatedWearable.GetConfig())
                .ToArray();
        }

        private static void ApplySettings(
            SimulatedWearable wearable,
            SimulatedWearableConfig config)
        {
            wearable.SetIp(config.Ip);
            wearable.SetId(config.Id);
            wearable.SetHeartRate(config.BaseHeartRate);
            wearable.SetHeartRateNoise(config.Amplitude, config.SpeedHz);
            wearable.SetSendInterval(config.IntervalMs);
            wearable.SetColor(config.Color);
        }

        private sealed record SimulationInstance(
            SimulatedWearable SimulatedWearable,
            CancellationTokenSource Cancellation
        );
    }
}