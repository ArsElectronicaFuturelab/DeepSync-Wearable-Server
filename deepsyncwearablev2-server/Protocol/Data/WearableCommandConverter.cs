using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSyncWearableServer.Protocol.Data
{
    public class WearableCommandConverter : JsonConverter<WearableCommand>
    {
        public override WearableCommand? Read(
            ref Utf8JsonReader reader, 
            Type typeToConvert, 
            JsonSerializerOptions options)
        {
            // Parse to JsonDocument to inspect the structure
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            // Create new options to avoid infinite recursion
            JsonSerializerOptions optionsWithoutConverter = new(options);
            optionsWithoutConverter.Converters.Clear();

            // Check if "type" property exists and fallback to ColorCmd if not found
            if (root.TryGetProperty("type", out JsonElement typeElement))
            {
                string? typeValue = typeElement.GetString();
                
                return typeValue switch
                {
                    "color" => JsonSerializer.Deserialize<ColorCmd>(
                        root.GetRawText(), optionsWithoutConverter),
                    "id" => JsonSerializer.Deserialize<NewIdCmd>(
                        root.GetRawText(), optionsWithoutConverter),
                    _ => JsonSerializer.Deserialize<ColorCmd>(
                        root.GetRawText(), optionsWithoutConverter)
                };
            }

            // No "type" field found - fallback to ColorCmd for legacy clients
            return JsonSerializer.Deserialize<ColorCmd>(
                root.GetRawText(), optionsWithoutConverter);
        }

        public override void Write(
            Utf8JsonWriter writer, WearableCommand value, JsonSerializerOptions options)
        {
            // Create options WITHOUT this converter to avoid infinite recursion
            JsonSerializerOptions optionsWithoutConverter = new(options);
            optionsWithoutConverter.Converters.Clear();

            // Use default serialization
            JsonSerializer.Serialize(writer, value, value.GetType(), optionsWithoutConverter);
        }
    }
}