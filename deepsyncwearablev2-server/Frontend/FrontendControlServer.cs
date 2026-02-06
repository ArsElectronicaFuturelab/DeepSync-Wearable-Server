using System.Net;
using System.Text;
using System.Text.Json;
using Serilog;

namespace DeepSyncWearableServer.Frontend
{
    public sealed class FrontendControlServer(
        string listenPrefix, 
        Action<FrontendSimulatedWearableConfig, CancellationToken> requestHandler)
    {
        private readonly string _listenPrefix = NormalizeListenPrefix(listenPrefix);

        private readonly Action<FrontendSimulatedWearableConfig, CancellationToken> _requestHandler = 
            requestHandler 
            ?? throw new ArgumentNullException(nameof(requestHandler));

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string NormalizeListenPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("Listen prefix cannot be null or whitespace.", nameof(prefix));
            }

            return prefix.EndsWith('/') ? prefix : $"{prefix.TrimEnd('/')}/";
        }

        public async Task RunAsync(CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<FrontendControlServer>();

            using HttpListener listener = new();
            listener.Prefixes.Add(_listenPrefix);
            listener.Start();

            log.Information($"Listening on {_listenPrefix}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().WaitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        log.Warning("Listener operation canceled");
                        break;
                    }

                    _ = Task.Run(() => ProcessRequestAsync(context, ct), ct);
                }
            }
            finally
            {
                listener.Stop();
                log.Information($"Stopped listening on {_listenPrefix}");
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            ILogger log = Log.Logger.WithClassAndMethodNames<FrontendControlServer>();

            try
            {
                if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    log.Warning("Received non-POST request: {Method}", context.Request.HttpMethod);

                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    context.Response.Close();
                    return;
                }

                using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
                string payload = await reader.ReadToEndAsync(ct);

                //log.Debug("Received request payload: {@Payload}", payload);

                FrontendSimulatedWearableConfig? update =
                    JsonSerializer.Deserialize<FrontendSimulatedWearableConfig>(payload, _jsonOptions);

                log.Debug("Received request: {@Update}", update);

                if (update == null)
                {
                    log.Error("Failed to deserialize request payload: {@Payload}", payload);

                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    return;
                }

                _requestHandler(update, ct);

                byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = ok.Length;
                await context.Response.OutputStream.WriteAsync(ok, ct);
                await context.Response.OutputStream.FlushAsync(ct);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                log.Error(ex, "Request handling failed");
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch { /* ignore */ }
            }
        }
    }
}