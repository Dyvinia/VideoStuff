using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace VideoStuff {
    internal class Program {
        public static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Assembly.GetEntryAssembly()!.GetName().Name!);

        public static readonly string[] VideoExt = [".mkv", ".mp4", ".webm", ".mov", ".avi", ".m4v"];
        public static readonly string[] ImageSeqExt = [".png", ".jpg", ".jpeg", ".bmp"];

        public static Video InVideo { get; set; }

        public static FileInfo FFMpeg { get; set; } = new(Path.Combine(AppDataDir, "ffmpeg.exe"));
        public static FileInfo FFProbe { get; set; } = new(Path.Combine(AppDataDir, "ffprobe.exe"));

        static readonly List<string> FFArgsList = [];
        static string FFArgs => String.Join(" ", FFArgsList);

        static bool Errored = false;

        static void Main(string[] args) {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Directory.CreateDirectory(AppDataDir);
            if (!FFMpeg.Exists || !FFProbe.Exists) {
                DownloadFFMpeg().Wait();
                File.Delete(Path.Combine(AppDataDir, "ffmpeg.zip"));
            }

            string inFilePath = String.Empty;
            if (args.Length > 0)
                inFilePath = args[0];
            else
                inFilePath = PromptUser("Input File: ").Trim('\"');

            Console.Clear();

            if (VideoExt.Any(e => e == Path.GetExtension(inFilePath))) {
                RunProbe(inFilePath);

                Console.WriteLine(InVideo.Name);

                int durMin = (int)TimeSpan.FromSeconds(InVideo.Duration).TotalMinutes;
                int durSec = TimeSpan.FromSeconds(InVideo.Duration).Seconds;
                int durMicro = TimeSpan.FromSeconds(InVideo.Duration).Milliseconds;

                Console.WriteLine($"Length: {(durMin > 0 ? $"{durMin}:{durSec}.{durMicro}" : InVideo.Duration.ToString("N3"))} | FPS: {InVideo.FPS}");
                FFArgsList.Add($"-i \"{InVideo.FullPath}\"");

                WriteSeparator();

                ConsoleKey remuxOrConv = PromptUserKey("Remux (R) or Convert (C)? [C]: ");
                if (remuxOrConv == ConsoleKey.R)
                    Remux();
                else
                    Convert();

                Console.WriteLine($"ffmpeg {FFArgs}");

                RunFFMpeg();
            }
            else if (ImageSeqExt.Any(e => e == Path.GetExtension(inFilePath))) {
                Console.WriteLine($"Image Sequence: {inFilePath}");
                
                string startFrame = string.Concat(Path.GetFileNameWithoutExtension(inFilePath).Where(char.IsDigit));
                int startFrameNumber = int.Parse(startFrame);

                int frameCount = Directory.EnumerateFiles(Path.GetDirectoryName(inFilePath)!).Where(f => Path.GetFileNameWithoutExtension(f).Any(char.IsDigit)).Where(f => {
                    int fNum = int.Parse(string.Concat(Path.GetFileNameWithoutExtension(f).Where(char.IsDigit)));
                    return f.Contains(Path.GetFileNameWithoutExtension(inFilePath).Replace(startFrame, "")) && fNum > startFrameNumber;
                }).Count();

                Console.WriteLine($"Frames: {startFrameNumber}-{startFrameNumber + frameCount} | Frame Count: {frameCount}");

                WriteSeparator();

                string inSequence = Path.Combine(Path.GetDirectoryName(inFilePath)!, Path.GetFileNameWithoutExtension(inFilePath).Replace(startFrame, "") + $"%0{startFrame.Length}d" + Path.GetExtension(inFilePath));

                FFArgsList.Add($"-start_number {startFrameNumber}");

                FFArgsList.Add($"-i \"{inSequence}\"");

                FFArgsList.Add($"-vcodec libx264 -pix_fmt yuv420p");

                string fps = PromptUser("Framerate [30]: ");
                if (String.IsNullOrWhiteSpace(fps))
                    fps = "30";

                FFArgsList.Add($"-r {fps} -vf fps={fps}");

                InVideo = new(inFilePath, int.Parse(fps), frameCount) {
                    Suffix = ".sequence"
                };

                Console.WriteLine($"ffmpeg {FFArgs}");

                RunFFMpeg();
            }
            else {
                Console.WriteLine("File is not valid format");
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
            }

            if (Errored) {
                WriteSeparator();
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            WriteSeparator();
            Console.WriteLine(e.ExceptionObject.ToString());
            Console.Write("Press Enter to Exit...");
            Console.ReadLine();
            Environment.Exit(1);
        }

        public static void Remux() {
            ConsoleKey map = PromptUserKey("Map All Audio Tracks (Y/N) [N]: ");

            InVideo.Suffix = ".remux";

            FFArgsList.Add("-c copy");
            if (map == ConsoleKey.Y) {
                FFArgsList.Add("-map 0");
                InVideo.Suffix += "Mapped";
            }
        }

        public static void Convert() {
            FFArgsList.Add("-vcodec libx264 -acodec aac -ac 2");
            InVideo.Suffix = ".conv";

            ConsoleKey cut = PromptUserKey("Cut Video? (Y/N) [N]: ");
            if (cut == ConsoleKey.Y) {
                string startTime = PromptUser("Start Time: ");
                if (String.IsNullOrWhiteSpace(startTime)) 
                    startTime = "0";
                FFArgsList.Add($"-ss {startTime}");

                double newDur = InVideo.Duration - Video.ParseSeconds(startTime);

                string? endTime = PromptUser("End Time: ");
                if (!String.IsNullOrWhiteSpace(endTime)) {
                    FFArgsList.Add($"-to {endTime}");
                    newDur = Video.ParseSeconds(endTime) - Video.ParseSeconds(startTime);
                }
                InVideo.Duration = newDur;

                InVideo.Suffix = ".cut";
            }
            else {
                ConsoleKey speed = PromptUserKey("Change Speed Of Video? (Y/N) [N]: ");
                if (speed == ConsoleKey.Y) {
                    string mult = PromptUser("Speed: ");
                    if (String.IsNullOrWhiteSpace(mult))
                        mult = "1";

                    string fps = PromptUser("FPS [Speed*FPS]: ");
                    if (String.IsNullOrWhiteSpace(fps))
                        FFArgsList.Add($"-vf \"setpts=PTS/{mult},fps=source_fps*{mult}\" -af \"atempo={mult}\"");
                    else {
                        FFArgsList.Add($"-vf \"setpts=PTS/{mult},fps={fps}\" -af \"atempo={mult}\"");
                        InVideo.Duration = InVideo.Duration / double.Parse(mult);
                        InVideo.FPS = int.Parse(fps);
                    }

                    InVideo.Suffix = $".{mult}x";
                }
            }

            ConsoleKey maxSize = PromptUserKey("Prevent Filesize from Exceeding 50MB? (Y/H(alf)/N) [Y]: ");
            if (maxSize != ConsoleKey.N) {
                int totalRate = 400000000 / (int)Math.Ceiling(InVideo.Duration);
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }
            else if (maxSize != ConsoleKey.H) {
                int totalRate = 200000000 / (int)Math.Ceiling(InVideo.Duration);
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }
        }

        public static void RunFFMpeg() {
            FFArgsList.Add(InVideo.OutPathQuoted);
            Process ffmpeg = new() { 
                StartInfo = new() {
                    FileName = FFMpeg.FullName,
                    Arguments = FFArgs,
                    RedirectStandardError = true,
                }
            };
            ffmpeg.Start();

            string line = "";
            while (!ffmpeg.StandardError.EndOfStream) {
                char character = (char)ffmpeg.StandardError.Read();
                line += character;
                Console.Write(character);
                if (character == '\n')
                    line = "";
                if (character == '\r') {
                    if (line.Contains("frame=")) {
                        string lineCutFront = line[(line.IndexOf('=') + 1)..];
                        string linefinal = lineCutFront[..lineCutFront.IndexOf('f')];
                        if (int.TryParse(linefinal, out int currentFrame)) {
                            double percent = currentFrame * 100 / InVideo.TotalFrames;
                            Console.Write($"\r{percent}% ");
                        }
                    }
                    line = "";
                }
                if (line.Contains("error", StringComparison.CurrentCultureIgnoreCase)) 
                    Errored = true;
            }
            ffmpeg.WaitForExit();
        }

        public static void RunProbe(string path) {
            Process probe = new() {
                StartInfo = new() {
                    FileName = FFProbe.FullName,
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{path}\"",
                    RedirectStandardOutput = true,
                }
            };

            probe.Start();
            string output = probe.StandardOutput.ReadToEnd();
            InVideo = new(JsonDocument.Parse(output).RootElement);
        }

        private static async Task DownloadFFMpeg() {
            string zipPath = Path.Combine(AppDataDir, "ffmpeg.zip");

            WebClient client = new();
            client.DownloadProgressChanged += (s, e) => Console.Write($"Downloading FFMpeg {e.ProgressPercentage}% \r");

            await client.DownloadFileTaskAsync("https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/ffmpeg-6.1.1-essentials_build.zip", zipPath);

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.Contains("ffmpeg.exe") || e.FullName.Contains("ffprobe.exe")))
                entry.ExtractToFile(Path.Combine(AppDataDir, entry.Name), true);

            await Task.Delay(1000);
            Console.Clear();
        }

        public static ConsoleKey PromptUserKey(string message) {
            Console.Write(message);
            ConsoleKey key = Console.ReadKey().Key;
            Console.WriteLine();
            return key;
        }

        public static string PromptUser(string message) {
            Console.Write(message);
            return Console.ReadLine() ?? String.Empty;
        }

        public static void WriteSeparator() => Console.WriteLine("---------------------------------------------");
    }
}
