using System.Globalization;
using System.Text.Json;

namespace VideoStuff {
    public class Video {
        public string FullPath { get; private set; }
        public string FullPathQuoted => $"\"{FullPath}\"";

        public string Name => Path.GetFileName(FullPath);
        public string Extension => Path.GetExtension(FullPath);

        public string? Suffix { get; set; }

        public string OutPath => Path.Combine(Path.GetDirectoryName(FullPath)!, Path.GetFileNameWithoutExtension(Name) + Suffix + ".mp4");
        public string OutPathQuoted => $"\"{OutPath}\"";

        public int FPS { get; set; }
        public double Duration { get; set; }

        public int TotalFrames => (int)(Duration * FPS);

        public Video(JsonElement rootElement) {
            FullPath = rootElement.GetProperty("format").GetProperty("filename").GetString() ?? String.Empty;

            JsonElement? videoStream = null;
            foreach (JsonElement stream in rootElement.GetProperty("streams").EnumerateArray()) {
                if (stream.GetProperty("codec_type").GetString() == "video") {
                    videoStream = stream;
                }
            }

            if (!videoStream.HasValue)
                return;

            FPS = int.Parse(videoStream!.Value.GetProperty("r_frame_rate").GetString()!.Split('/').First());

            if (videoStream!.Value.TryGetProperty("duration", out JsonElement durationElement))
                Duration = ParseSeconds(durationElement.GetString()!.TrimEnd('0').TrimEnd('.'));
            else if (videoStream!.Value.GetProperty("tags").TryGetProperty("DURATION", out JsonElement durationTagElement))
                Duration = ParseSeconds(durationTagElement.GetString()!.TrimEnd('0').TrimEnd('.'));

            
        }

        private double ParseSeconds(string value) {
            return TimeSpan.ParseExact(value, [@"h\:m\:s\.f", @"m\:s\.f", @"m\:s", @"%s", @"s\.f"], CultureInfo.InvariantCulture).TotalSeconds;
        }
    }
}
