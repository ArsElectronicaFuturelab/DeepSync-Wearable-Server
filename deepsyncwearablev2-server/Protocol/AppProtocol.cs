using System.Text.Json;
using DeepSyncWearableServer.Protocol.Data;

namespace DeepSyncWearableServer.Protocol
{
    public class AppProtocol : Protocol
    {
        // Message format: "<payload>X"
        private const char MSG_DELIMITER = 'X';

        // App to server
        public override WearableCommand? DecodeCommand()
        {
            string buf = _buffer.ToString();
            if (buf.Length == 0) return null;

            // find end
            int end = buf.IndexOf(MSG_DELIMITER);
            if (end < 0)
            {
                // potentially not yet complete
                return null;
            }

            string frame = _buffer.Remove(0, end + 1).ToString();
            WearableCommand? cmd = JsonSerializer.Deserialize<WearableCommand>(
                frame[..-1],
                _jsonOptions
            );

            if (cmd is null)
                throw new JsonException("Deserialization returned null.");

            return cmd;
        }

        // Server to app
        public override string EncodeData(WearableData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            string payload = JsonSerializer.Serialize(data, _jsonOptions);
            return $"{payload}{MSG_DELIMITER}";
        }
    }
}