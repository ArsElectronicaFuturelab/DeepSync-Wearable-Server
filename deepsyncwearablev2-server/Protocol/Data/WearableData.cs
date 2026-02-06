using System.Text.Json.Serialization;

namespace DeepSyncWearableServer.Protocol.Data
{
    public struct Color(byte r, byte g, byte b)
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

    public abstract class WearableCommand(int id)
    {
        public int Id { get; set; } = id;
    }

    public class ColorCmd(int id, Color color) : WearableCommand(id)
    {
        public Color Color { get; set; } = color;
    }

    class NewIdCmd(int id, int newId) : WearableCommand(id)
    {
        public int NewId { get; set; } = newId;
    }
}
