﻿using System.Diagnostics;
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


        public string? PixelFormat { get; set; }
        public string? ColorSpace { get; set; }

        public int VideoTrackIndex { get; set; }

        public List<AudioTrack> AudioTracks { get; set; } = [];


        public Video(string path, FileInfo ffProbe) {
            Process probe = new() {
                StartInfo = new() {
                    FileName = ffProbe.FullName,
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{path}\"",
                    RedirectStandardOutput = true,
                }
            };
            probe.Start();
            string output = probe.StandardOutput.ReadToEnd();
            JsonElement rootElement = JsonDocument.Parse(output).RootElement;
            FullPath = rootElement.GetProperty("format").GetProperty("filename").GetString() ?? String.Empty;

            JsonElement? videoStream = null;
            foreach (JsonElement stream in rootElement.GetProperty("streams").EnumerateArray()) {
                if (stream.GetProperty("codec_type").GetString() == "video")
                    videoStream = stream;
                if (stream.GetProperty("codec_type").GetString() == "audio")
                    AudioTracks.Add(JsonSerializer.Deserialize<AudioTrack>(stream)!);
            }

            if (!videoStream.HasValue)
                return;

            VideoTrackIndex = videoStream!.Value.GetProperty("index").GetInt32();

            FPS = int.Parse(videoStream!.Value.GetProperty("r_frame_rate").GetString()!.Split('/').First());

            if (videoStream!.Value.TryGetProperty("duration", out JsonElement durationElement))
                Duration = ParseSeconds(durationElement.GetString()!.TrimEnd('0').TrimEnd('.'));
            else if (videoStream!.Value.GetProperty("tags").TryGetProperty("DURATION", out JsonElement durationTagElement))
                Duration = ParseSeconds(durationTagElement.GetString()!.TrimEnd('0').TrimEnd('.'));

            PixelFormat = videoStream!.Value.GetProperty("pix_fmt").GetString()!;
            
            if (videoStream!.Value.TryGetProperty("color_space", out JsonElement colorSpace))
                ColorSpace = colorSpace.GetString()!;
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
