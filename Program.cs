using System.Diagnostics;
using System.Text.Json;

namespace VideoStuff {
    internal class Program {

        public static Video InVideo { get; set; }

        public static FileInfo FFMpeg { get; set; } = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));
        public static FileInfo FFProbe { get; set; } = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe"));

        static readonly List<string> FFArgsList = [];
        static string FFArgs => String.Join(" ", FFArgsList);

        static bool Errored = false;

        static void Main(string[] args) {
            if (!File.Exists(args.FirstOrDefault())) {
                Console.WriteLine("Invalid File");
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
                return;
            }
            else if (!FFMpeg.Exists) {
                Console.WriteLine("Unable to Locate FFMpeg");
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
                return;
            }
            else if (!FFProbe.Exists) {
                Console.WriteLine("Unable to Locate FFProbe");
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            RunProbe(args.FirstOrDefault()!);

            Console.WriteLine(InVideo.Name);
            FFArgsList.Add($"-i \"{InVideo.FullPath}\"");

            WriteSeparator();

            ConsoleKey remuxOrConv = PromptUserKey("Remux (R) or Convert (C)? [C]: ");
            if (remuxOrConv == ConsoleKey.R)
                Remux();
            else if (remuxOrConv == ConsoleKey.C || remuxOrConv == ConsoleKey.Enter)
                Convert();

            Console.WriteLine($"ffmpeg {FFArgs}");

            RunFFMpeg();

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

            ConsoleKey maxSize = PromptUserKey("Prevent Filesize from Exceeding 50MB? (Y/N) [Y]: ");
            if (maxSize != ConsoleKey.N) {
                int totalRate = 400000000 / (int)Math.Ceiling(InVideo.Duration);
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }

            ConsoleKey cut = PromptUserKey("Cut Video? (Y/N) [N]: ");
            if (cut == ConsoleKey.Y) {
                string startTime = PromptUser("Start Time: ");
                if (String.IsNullOrEmpty(startTime)) 
                    startTime = "0";
                FFArgsList.Add($"-ss {startTime}");

                string? endTime = PromptUser("End Time: ");
                if (!String.IsNullOrEmpty(endTime))
                    FFArgsList.Add($"-to {endTime}");

                InVideo.Suffix = ".cut";
            }
            else {
                ConsoleKey speed = PromptUserKey("Change Speed Of Video? (Y/N) [N]: ");
                if (speed == ConsoleKey.Y) {
                    string mult = PromptUser("Speed: ");
                    if (String.IsNullOrEmpty(mult))
                        mult = "1";

                    string fps = PromptUser("FPS [Speed*FPS]: ");
                    if (String.IsNullOrEmpty(fps))
                        FFArgsList.Add($"-vf \"setpts=PTS/{mult},fps=source_fps*{mult}\" -af \"atempo={mult}\"");
                    else {
                        FFArgsList.Add($"-vf \"setpts=PTS/{mult},fps={fps}\" -af \"atempo={mult}\"");
                        InVideo.Duration = InVideo.Duration / double.Parse(mult);
                        InVideo.FPS = int.Parse(fps);
                    }

                    InVideo.Suffix = $".{mult}x";
                }
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
                if (line.Contains("Error opening output file")) 
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
