using Serilog;
using System.Text;
using System.Text.Json;
using DeepSyncWearableServer.Protocol.Data;
using DeepSyncWearableServer.Wearable;

namespace DeepSyncWearableServer.Frontend
{
    public class FrontendSimulatedWearableConfig
    {
        public string Action { get; set; } = string.Empty;

        public string Ip { get; set; } = string.Empty;
        public int Id { get; set; } = -1;
        public int BaseHeartRate { get; set; } = 90;
        public double Amplitude { get; set; } = 1.0;
        public double SpeedHz { get; set; } = 1.0;
        public double IntervalMs { get; set; } = 100.0;
        public Color Color { get; set; } = new Color(0, 0, 0);
    }

    public class FrontendSender(string restEndpointUrl, string simulatedEndpointUrl)
    {
        private readonly Uri _restEndpoint =
            new(restEndpointUrl ?? throw new ArgumentNullException(nameof(restEndpointUrl)));
        private readonly Uri _simulatedEndpoint =
            new(simulatedEndpointUrl ?? throw new ArgumentNullException(nameof(simulatedEndpointUrl)));
        private readonly HttpClient _httpClient = new();

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task SendWearableDataAsync(
            WearableData[] data,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(data);
            ILogger log = Log.Logger.WithClassAndMethodNames<FrontendSender>();

            string payload = JsonSerializer.Serialize(data, _jsonOptions);
            using StringContent content = new(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                _restEndpoint, content, ct);

            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                log.Error(ex, "Failed to post wearable data to frontend. " +
                    "Status: {StatusCode}", response.StatusCode);
            }

            //log.Debug("Posted wearable: {@Payload}", payload);
        }

        public async Task SendSimulatedWearableConfigAsync(
            SimulatedWearableConfig[] data,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(data);
            ILogger log = Log.Logger.WithClassAndMethodNames<FrontendSender>();

            string payload = JsonSerializer.Serialize(data, _jsonOptions);
            using StringContent content = new(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                _simulatedEndpoint, content, ct);

            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                log.Error(ex, "Failed to post simulated wearable config to frontend. " +
                    "Status: {StatusCode}", response.StatusCode);
                return;
            }

            //log.Debug("Posted simulated wearable: {@Payload}", payload);
        }
    }
}