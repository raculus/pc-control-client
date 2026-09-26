using System.Text.Json.Serialization;

namespace pc_control_client
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ClientStatusResponse))]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }

    public class ClientStatusResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("remaining_seconds")]
        public int RemainingSeconds { get; set; }

        [JsonPropertyName("hours")]
        public int Hours { get; set; }

        [JsonPropertyName("minutes")]
        public int Minutes { get; set; }

        [JsonPropertyName("should_shutdown")]
        public bool ShouldShutdown { get; set; }

        // 추가된 부팅 시간 필드
        [JsonPropertyName("last_booted_at")]
        public string? LastBootedAt { get; set; }
    }
}