using System.Text.Json;
using DeepSyncWearableServer.Protocol.Data;

namespace DeepSyncWearableServer.Protocol
{
    public class WearableProtocol : Protocol
    {
        // Mesage format: "$<type>:<size>:<payload>&"

        private const char MSG_START = '$';
        private const char MSG_DELIMITER = ':';
        private const char MSG_END = '&';

        public const char MSG_TYPE_ID_CMD = 'i';
        public const char MSG_TYPE_COLOR_CMD = 'c';
        public const char MSG_TYPE_STATUS = 's';

        // Server to simulated wearable
        public override WearableCommand? DecodeCommand()
        {
            var message = DecodeFrame();
            if (message == null) return null;

            char msgType = message.Value.type[0];
            try
            {
                return msgType switch
                {
                    MSG_TYPE_ID_CMD => JsonSerializer.Deserialize<WearableCommand>(
                        message.Value.payload, _jsonOptions),
                    MSG_TYPE_COLOR_CMD => JsonSerializer.Deserialize<WearableCommand>(
                        message.Value.payload, _jsonOptions),
                    _ => throw new InvalidOperationException($"Unknown command type: {msgType}")
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Server to Wearable
        public override string EncodeCommand(WearableCommand data)
        {
            ArgumentNullException.ThrowIfNull(data);

            char msgType = data switch
            {
                ColorCmd => MSG_TYPE_COLOR_CMD,
                NewIdCmd => MSG_TYPE_ID_CMD,
                _ => throw new ArgumentException($"Unknown command type: {data.GetType().Name}")
            };

            string payload = JsonSerializer.Serialize(data, _jsonOptions);
            return EncodeFrame(msgType, payload);
        }

        // Wearable to Server
        public override WearableData? DecodeData()
        {
            (string type, string payload)? message = DecodeFrame();
            if (message == null) return null;

            // Check if it's a status message
            if (message.Value.type != MSG_TYPE_STATUS.ToString())
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<WearableData>(
                    message.Value.payload, _jsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Simulated wearable to server
        public override string EncodeData(WearableData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            string payload = JsonSerializer.Serialize(data, _jsonOptions);
            return EncodeFrame(MSG_TYPE_STATUS, payload);
        }

        private (string type, string payload)? DecodeFrame()
        {
            string buf = _buffer.ToString();
            if (buf.Length == 0) return null;

            // Find start
            int start = buf.IndexOf(MSG_START);
            if (start < 0)
            {
                _buffer.Clear();
                return null;
            }

            // Drop potential garbage in front
            if (start > 0)
            {
                _buffer.Remove(0, start);
                buf = _buffer.ToString();
            }

            // Find end
            int end = buf.IndexOf(MSG_END, 1);
            if (end < 0)
            {
                // Not yet complete
                return null;
            }

            // Found candidate frame
            _buffer.Remove(0, end + 1);
            string frame = buf[1..end];

            // Parse frame
            if (!TryParseFrame(frame, out string type, out int size, out string payload))
            {
                return null;
            }

            // do final size check
            if (payload.Length != size)
            {
                return null;
            }

            return (type, payload);
        }

        private string EncodeFrame(char type, string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                throw new ArgumentException("Payload cannot be empty", nameof(payload));

            return $"{MSG_START}{type}{MSG_DELIMITER}{payload.Length}{MSG_DELIMITER}{payload}{MSG_END}";
        }

        private bool TryParseFrame(string frame, out string type, out int size, out string payload)
        {
            type = "";
            size = 0;
            payload = "";

            // frame: "<cmd>:<size>:<payload>"
            int del1 = frame.IndexOf(MSG_DELIMITER);
            if (del1 <= 0) return false;

            int del2 = frame.IndexOf(MSG_DELIMITER, del1 + 1);
            if (del2 <= del1 + 1) return false;

            type = frame[0..del1];

            string sizeStr = frame[(del1 + 1)..del2];
            if (!int.TryParse(sizeStr, out size)) return false;

            payload = frame[(del2 + 1)..];

            return true;
        }
    }
}