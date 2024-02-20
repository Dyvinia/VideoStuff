using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VideoStuff {
    public class Video {
        public string FullPath { get; private set; }
        public string FullPathQuoted => $"\"{FullPath}\"";

        public string Name => Path.GetFileName(FullPath);
        public string Extension => Path.GetExtension(FullPath);

        public string? Suffix { get; set; } = String.Empty;

        public string OutPath => Path.Combine(Path.GetDirectoryName(FullPath)!, Path.GetFileNameWithoutExtension(Name) + Suffix + ".mp4");
        public string OutPathQuoted => $"\"{OutPath}\"";

        public int FPS { get; set; }
        public double Duration { get; set; }

        public int TotalFrames => (int)(Duration * FPS);

        public Video(string path, FileInfo ffProbe) {
            Process probe = new() {
                StartInfo = new() {
                    FileName = ffProbe.FullName,
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{path}\"",
                    RedirectStandardOutput = true,
                }
            };
            probe.Start();

            JsonElement rootElement = JsonDocument.Parse(probe.StandardOutput.ReadToEnd()).RootElement;
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

        public Video(string seqPath, int fps, int frameCount) {
            FullPath = seqPath;
            FPS = fps;
            Duration = (double)frameCount/fps;
        }

        public static double ParseSeconds(string value) {
            if (value.Contains(':'))
                return TimeSpan.ParseExact(value, [@"h\:m\:s\.FFFF", @"m\:s\.FFFF", @"h\:m\:s", @"m\:s"], CultureInfo.InvariantCulture).TotalSeconds;
            else return double.Parse(value);
        }
    }
}
