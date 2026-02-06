using System.Text.Json.Serialization;

namespace DeepSyncWearableServer.Protocol.Data
{
    public struct Color(byte r = 0, byte g = 0, byte b = 0)
    {
        public byte R { get; set; } = r;
        public byte G { get; set; } = g;
        public byte B { get; set; } = b;
    }

    public class WearableData(int userId = -1)
    {
        public int Timestamp { get; set; } = 0;

        [JsonIgnore]
        public long TimestampInternal { get; set; } = 0;

        [JsonIgnore]
        public bool Stale {
            get
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (TimestampInternal == 0)
                {
                    TimestampInternal = now;
                    return false;
                }

                return (now - TimestampInternal) > 1000;
            }
        }

        public int Id { get; set; } = userId;

        public int HeartRate { get; set; } = 0;

        public Color Color { get; set; } = new Color(0, 0, 0);

        //public HeartRateData HeartRate { get; set; }
        //public SpO2Data spO2; 

        //public abstract class SensorData
        //{
        //    public DateTime lastUpdate = DateTime.MinValue;
        //}

        //public class HeartRateData : SensorData
        //{
        //    public int last { get; set; }

        //    public int average { get; set; }
        //}

        //public class SpO2Data : SensorData
        //{
        //    public float lastSpO2 = -1;
        //    public float averageSpO2 = -1;
        //}
    }

    public abstract class WearableCommand
    {
        public int Id { get; set; } = -1;

        protected WearableCommand() { }

        protected WearableCommand(int id = -1)
        {
            Id = id;
        }
    }

    public class ColorCmd : WearableCommand
    {
        public Color Color { get; set; } = default;

        public ColorCmd() : base() { }

        public ColorCmd(int id = -1, Color color = default) : base(id)
        {
            Color = color;
        }
    }

    public class NewIdCmd : WearableCommand
    {
        public int NewId { get; set; } = -1;

        public NewIdCmd() : base() { }

        public NewIdCmd(int id = -1, int newId = -1) : base(id)
        {
            NewId = newId;
        }
    }
}
