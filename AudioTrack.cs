using System.Text.Json.Serialization;

namespace VideoStuff {
    public class AudioTrack {

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_name")]
        public string Codec { get; set; } = "unknown";
    }
}
