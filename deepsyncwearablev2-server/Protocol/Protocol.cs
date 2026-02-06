using System.Text;
using System.Text.Json;
using DeepSyncWearableServer.Protocol.Data;

namespace DeepSyncWearableServer.Protocol
{
    public abstract class Protocol
    {
        protected readonly StringBuilder _buffer = new();
        public int BufferSize => _buffer.Length;

        protected readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public void ClearBuffer()
        {
            _buffer.Clear();
        }

        public void Push(char[] data, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(data, nameof(data));
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                count + offset, data.Length, $"{nameof(count)}/{nameof(offset)}");

            _buffer.Append(data, offset, count);
        }

        public void Push(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            _buffer.Append(data);
        }

        public virtual WearableCommand? DecodeCommand()
        {
            throw new NotImplementedException(
                "DecodeCommand for WearableCommand is not implemented in this protocol.");
        }

        public virtual string EncodeCommand(WearableCommand data)
        {
            throw new NotImplementedException(
                "EncodeCommand for WearableCommand is not implemented in this protocol.");
        }

        public virtual WearableData? DecodeData()
        {
            throw new NotImplementedException(
                "DecodeData for WearableData is not implemented in this protocol.");
        }

        public virtual string EncodeData(WearableData data)
        {
            throw new NotImplementedException(
                "EncodeData for WearableData is not implemented in this protocol.");
        }
    }
}