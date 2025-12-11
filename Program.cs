using System.Diagnostics;
using System.IO.Compression;
using System.Media;
using System.Net;
using System.Reflection;

namespace VideoStuff {
    internal class Program {
        public static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Assembly.GetEntryAssembly()!.GetName().Name!);

        public static readonly string[] VideoExt = [".mkv", ".mp4", ".webm", ".mov", ".avi", ".m4v"];
        public static readonly string[] ImageSeqExt = [".png", ".jpg", ".jpeg", ".bmp"];
        public static readonly string[] AudioExt = [".mp3", ".wav", ".ogg", ".flac"];

        public static Video? InVideo { get; set; }

        public static FileInfo FFMpeg { get; set; } = new(Path.Combine(AppDataDir, "ffmpeg.exe"));
        public static FileInfo FFProbe { get; set; } = new(Path.Combine(AppDataDir, "ffprobe.exe"));
        public static FileInfo FFPlay { get; set; } = new(Path.Combine(AppDataDir, "ffplay.exe"));

        static readonly List<string> FFArgsList = [];
        static string FFArgs => string.Join(" ", FFArgsList);

        static bool Errored = false;

        static readonly bool UseHardwareAccel = true;

        static readonly bool PlaySoundOnCompletion = true;

        static void Main(string[] args) {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            ConsoleExtension.Show();

            Console.Title = $"VideoStuff v{Assembly.GetEntryAssembly()?.GetName().Version?.ToString()[..5]}";

            Directory.CreateDirectory(AppDataDir);
            if (!FFMpeg.Exists || !FFProbe.Exists || !FFPlay.Exists) {
                DownloadFFMpeg().Wait();
                File.Delete(Path.Combine(AppDataDir, "ffmpeg.zip"));
            }

            string inFilePath = string.Empty;
            if (args.Length > 0)
                inFilePath = args[0];
            else
                inFilePath = PromptUser("Input File: ", false).Trim('\"');

            Console.Clear();

            if (VideoExt.Any(e => e == Path.GetExtension(inFilePath))) {
                InVideo = new(inFilePath, FFProbe);

                Console.WriteLine(InVideo.Name);

                int durMin = (int)TimeSpan.FromSeconds(InVideo.Duration).TotalMinutes;
                double durSec = InVideo.Duration - (durMin * 60);

                Console.WriteLine($"Resolution: {InVideo.Width} x {InVideo.Height} ({InVideo.AspectRatio}) | FPS: {InVideo.FPS}");
                Console.WriteLine($"Length: {(durMin > 0 ? $"{durMin}:{durSec:N2}" : InVideo.Duration.ToString("N2"))} | Audio Tracks: {InVideo.AudioTracks.Count}");
                FFArgsList.Add($"-i \"{InVideo.FullPath}\"");

                WriteSeparator();

                ConsoleKey videoProcess = PromptUserKey("Convert Video [C], Remux Video [R], or Convert to Audio [A]? (C): ");
                if (videoProcess == ConsoleKey.R)
                    Remux();
                else if (videoProcess == ConsoleKey.A)
                    ConvertToAudio();
                else
                    Convert();

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

                string fps = PromptUser("Framerate (30): ");
                if (string.IsNullOrWhiteSpace(fps))
                    fps = "30";

                FFArgsList.Add($"-r {fps}");

                FFArgsList.Add($"-start_number {startFrameNumber}");

                FFArgsList.Add($"-i \"{inSequence}\"");

                FFArgsList.Add($"-vcodec libx264 -pix_fmt yuv420p");
                
                FFArgsList.Add($"-vf fps={fps}");

                InVideo = new(inFilePath, int.Parse(fps), frameCount) {
                    Suffix = ".seq"
                };

                RunFFMpeg();
            }
            else if (AudioExt.Any(e => e == Path.GetExtension(inFilePath))) {
                Console.WriteLine($"Audio: {inFilePath}");
                FFArgsList.Add($"-i \"{inFilePath}\" -c:a libopus \"{Path.ChangeExtension(inFilePath, ".opus")}\"");
                RunFFMpeg(false);
            }
            else {
                Console.WriteLine("File is not valid format");
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
            }

            PlaySound();

            if (Errored) {
                WriteSeparator();
                Console.Write("Press Enter to Exit...");
                Console.ReadLine();
            }
            else {
                Thread.Sleep(1000);
                ConsoleExtension.Minimize();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            WriteSeparator();
            Console.WriteLine(e.ExceptionObject.ToString());
            Console.Write("Press Enter to Exit...");
            Console.ReadLine();
            Environment.Exit(1);
        }

        public static void ConvertToAudio() {
            ConsoleKey format = PromptUserKey("Wav [W], MP3 [M], Opus [O] (W): ");
            if (format == ConsoleKey.M) {
                FFArgsList.Add("-vn -c:a libmp3lame");
                InVideo!.OutExtension = ".mp3";
            }
            else if (format == ConsoleKey.O) {
                FFArgsList.Add("-vn -c:a libopus");
                InVideo!.OutExtension = ".opus";
            }
            else {
                FFArgsList.Add("-vn -c:a pcm_u8");
                InVideo!.OutExtension = ".wav";
            }
        }

        public static void Remux() {
            FFArgsList.Add("-c copy");

            if (InVideo!.AudioTracks.Count > 1) {
                char map = PromptUserChar($"Select Audio Track [{InVideo.AudioTracks.First().Index} - {InVideo.AudioTracks.Last().Index}] or Map All [A] (1): ");

                if (map == 'a')
                    FFArgsList.Add("-map 0");
                if (int.TryParse(map.ToString(), out int selectedIndex) && selectedIndex >= InVideo.AudioTracks.First().Index && selectedIndex <= InVideo.AudioTracks.Last().Index)
                    FFArgsList.Add($"-map 0:v:{InVideo.VideoTrackIndex} -map 0:a:{selectedIndex - 1}");
            }

            InVideo.Suffix = ".remux";
        }

        public static void Convert() {
            if (UseHardwareAccel)
                FFArgsList.Add("-vcodec h264_nvenc -acodec aac -ac 2");
            else
                FFArgsList.Add("-vcodec libx264 -acodec aac -ac 2");

            if (InVideo!.AudioTracks.Count > 1) {
                char audioTrack = PromptUserChar($"Select Audio Track [{InVideo.AudioTracks.First().Index} - {InVideo.AudioTracks.Last().Index}] or Mute [M] ({InVideo.AudioTracks.First().Index}): ");
                if (int.TryParse(audioTrack.ToString(), out int selectedIndex) && selectedIndex >= InVideo.AudioTracks.First().Index && selectedIndex <= InVideo.AudioTracks.Last().Index)
                    FFArgsList.Add($"-map 0:v:{InVideo.VideoTrackIndex} -map 0:a:{selectedIndex - 1}");
                else if (audioTrack == 'm')
                    FFArgsList.Add("-an");
            }
            else {
                ConsoleKey mute = PromptUserKey("Mute Video? [Y/N] (N): ");
                if (mute == ConsoleKey.Y)
                    FFArgsList.Add("-an");
            }

            List<string> videoFilters = [];

            ConsoleKey cut = PromptUserKey("Cut Video? [Y/N] (N): ");
            if (cut == ConsoleKey.Y) {
                string startTime = PromptUser("Start Time: ");
                if (string.IsNullOrWhiteSpace(startTime))
                    startTime = "0";
                FFArgsList.Add($"-ss {startTime}");

                double newDur = InVideo.Duration - Video.ParseSeconds(startTime);

                string? endTime = PromptUser("End Time: ");
                if (!string.IsNullOrWhiteSpace(endTime)) {
                    newDur = Video.ParseSeconds(endTime) - Video.ParseSeconds(startTime);
                    FFArgsList.Add($"-t {newDur}");
                }
                InVideo.Duration = newDur;

                InVideo.Suffix = ".cut";
            }
            else {
                ConsoleKey speed = PromptUserKey("Change Speed Of Video? [Y/N] (N): ");
                if (speed == ConsoleKey.Y) {
                    string mult = PromptUser("Speed: ");
                    if (string.IsNullOrWhiteSpace(mult))
                        mult = "1";

                    string fps = PromptUser("FPS [Speed*FPS]: ");
                    if (string.IsNullOrWhiteSpace(fps)) {
                        videoFilters.Add($"setpts=PTS/{mult},fps=source_fps*{mult}");
                        InVideo.Duration /= double.Parse(mult);
                        InVideo.FPS = (int)(InVideo.FPS * double.Parse(mult));
                    }
                    else {
                        videoFilters.Add($"setpts=PTS/{mult},fps={fps}");
                        
                        InVideo.Duration /= double.Parse(mult);
                        InVideo.FPS = int.Parse(fps);
                    }
                    FFArgsList.Add($"-af \"atempo={mult}\"");
                    InVideo.Suffix = $".{mult}x";
                }
            }

            ConsoleKey crop = PromptUserKey("Crop Video? [S(quare)/4(:3)/N] (N): ");
            if (crop == ConsoleKey.S) {
                videoFilters.Add($"crop={InVideo.Height}:{InVideo.Height}");
                InVideo.Suffix += $".sqr";
            }
            else if (crop == ConsoleKey.D4) {
                videoFilters.Add($"crop={InVideo.Height * 4 / 3}:{InVideo.Height}");
                InVideo.Suffix += $".4x3";
            }

            ConsoleKey maxSize = PromptUserKey("Prevent Filesize from Exceeding 50MB? [Y/H(alf:25MB)/F(ifth:10MB)/8(MB)/N] (Y): ");
            if (maxSize != ConsoleKey.N && maxSize != ConsoleKey.H && maxSize != ConsoleKey.F && maxSize != ConsoleKey.D8) {
                int totalRate = 400000000 / InVideo.Duration.Ceiling();
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }
            else if (maxSize == ConsoleKey.H) {
                int totalRate = 200000000 / InVideo.Duration.Ceiling();
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }
            else if (maxSize == ConsoleKey.F) {
                int totalRate = 80000000 / InVideo.Duration.Ceiling();
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }
            else if (maxSize == ConsoleKey.D8) {
                int totalRate = 64000000 / InVideo.Duration.Ceiling();
                FFArgsList.Add($"-maxrate {totalRate} -bufsize {totalRate}");
            }

            if (UseHardwareAccel)
                FFArgsList.Add("-rc vbr -cq 28 -preset p7 -tune hq -multipass fullres -profile:v high");
            else {
                ConsoleKey qualityPreset = PromptUserKey("x264 Quality Preset [Fast, Medium, Slow, Veryslow] (M): ");
                FFArgsList.Add(qualityPreset switch {
                    ConsoleKey.F => "-preset fast",
                    ConsoleKey.S => "-preset slow",
                    ConsoleKey.V => "-preset veryslow",
                    _ => "-preset medium",
                });
            }

            ConsoleKey useFilters = PromptUserKey("Boost Vibrance/Contrast? [Y/N] (N): ");
            if (useFilters == ConsoleKey.Y) {
                SaveCurves();
                videoFilters.Add($"vibrance=intensity=0.15, curves=psfile=curves.acv");
                if (InVideo.ColorSpace is not null)
                    FFArgsList.Add($"-colorspace {InVideo.ColorSpace}");
                if (InVideo.PixelFormat is not null)
                    FFArgsList.Add($"-pix_fmt {InVideo.PixelFormat}");
                InVideo.Suffix += $".vibrant";
            }

            if (videoFilters.Count > 0)
                FFArgsList.Add($"-vf \"{string.Join(',', videoFilters)}\"");

            if (string.IsNullOrEmpty(InVideo.Suffix))
                InVideo.Suffix = ".conv";
        }

        public static void RunFFMpeg(bool addOutPath = true) {
            if (addOutPath)
                FFArgsList.Add(InVideo!.OutPathQuoted);

            Console.WriteLine($"ffmpeg {FFArgs}");

            Process ffmpeg = new() {
                StartInfo = new() {
                    FileName = FFMpeg.FullName,
                    Arguments = FFArgs,
                    RedirectStandardError = true,
                    WorkingDirectory = FFMpeg.DirectoryName
                }
            };
            ffmpeg.Start();

            string line = string.Empty;
            while (!ffmpeg.StandardError.EndOfStream) {
                char character = (char)ffmpeg.StandardError.Read();
                line += character;
                Console.Write(character);
                if (character == '\n')
                    line = string.Empty;
                if (character == '\r') {
                    if (line.Contains("frame=")) {
                        string lineCutFront = line[(line.IndexOf('=') + 1)..];
                        string linefinal = lineCutFront[..lineCutFront.IndexOf('f')];
                        if (int.TryParse(linefinal, out int currentFrame)) {
                            double percent = currentFrame * 100 / InVideo!.TotalFrames;
                            Console.Write($"\r{percent}% ");
                        }
                    }
                    line = string.Empty;
                }
                if (line.Contains("error", StringComparison.CurrentCultureIgnoreCase))
                    Errored = true;
            }
            ffmpeg.WaitForExit();
        }

        private static async Task DownloadFFMpeg() {
            string zipPath = Path.Combine(AppDataDir, "ffmpeg.zip");

            WebClient client = new();
            client.DownloadProgressChanged += (s, e) => Console.Write($"Downloading FFMpeg {e.ProgressPercentage}% \r");

            await client.DownloadFileTaskAsync("https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/ffmpeg-6.1.1-essentials_build.zip", zipPath);

            Console.Clear();
            Console.Write($"Extracting FFMpeg... \r");

            string[] exes = ["ffmpeg.exe", "ffprobe.exe", "ffplay.exe"];
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => exes.Any(e.FullName.Contains)))
                entry.ExtractToFile(Path.Combine(AppDataDir, entry.Name), true);

            await Task.Delay(100);
            Console.Clear();
        }

        private static void SaveCurves() {
            string curvesFile = Path.Combine(AppDataDir, "curves.acv");

            if (!File.Exists(curvesFile)) {
                using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoStuff.Resources.curves.acv")!;
                using FileStream f = File.OpenWrite(curvesFile);
                s.CopyTo(f);
                s.Close();
                f.Close();
            }
        }

        public static char PromptUserChar(string message) {
            Console.Write(message);
            ConsoleKeyInfo key = Console.ReadKey();

            bool isHotKey = Hotkey(key.Key);
            while (isHotKey) {
                key = Console.ReadKey();
                isHotKey = Hotkey(key.Key);
            }
            Console.WriteLine();

            return char.ToLower(key.KeyChar);
        }

        public static ConsoleKey PromptUserKey(string message) {
            Console.Write(message);
            ConsoleKeyInfo key = Console.ReadKey();

            bool isHotKey = Hotkey(key.Key);
            while (isHotKey) {
                key = Console.ReadKey();
                isHotKey = Hotkey(key.Key);
            }
            Console.WriteLine();

            return key.Key;
        }

        public static string PromptUser(string message, bool allowRestart = true) {
            Console.Write(message);

            string result = string.Empty;

            int i = 0;
            while (true) {
                ConsoleKeyInfo key = Console.ReadKey(true);

                bool isHotKey = Hotkey(key.Key, allowRestart);
                while (isHotKey) {
                    key = Console.ReadKey(true);
                    isHotKey = Hotkey(key.Key, allowRestart);
                }

                if (key.Key == ConsoleKey.Enter) {
                    Console.WriteLine();
                    return result;
                }

                if (key.Key == ConsoleKey.Backspace) {
                    if (i > 0) {
                        result = result[..^1];
                        Console.Write(key.KeyChar);
                        Console.Write(' ');
                        Console.Write(key.KeyChar);
                        i--;
                    }
                }
                else {
                    result += key.KeyChar;
                    Console.Write(key.KeyChar);
                    i++;
                    ConsoleExtension.Focus();
                }
            }
        }

        public static bool Hotkey(ConsoleKey key, bool allowRestart = true) {
            if (key == ConsoleKey.F5 && allowRestart) {
                Restart();
                return true;
            }
            if (key == ConsoleKey.F1) {
                Preview();
                return true;
            }

            // blacklist certain keys from being read by the user prompts
            ConsoleKey[] nonInputKeys = [
                ConsoleKey.VolumeUp,
                ConsoleKey.VolumeDown,
                ConsoleKey.VolumeMute,
                ConsoleKey.UpArrow,
                ConsoleKey.DownArrow,
                ConsoleKey.LeftArrow,
                ConsoleKey.RightArrow,
                ConsoleKey.Home,
                ConsoleKey.Insert,
                ConsoleKey.PageUp,
                ConsoleKey.PageDown,
                ConsoleKey.End,
                ConsoleKey.Delete,
            ];
            if (nonInputKeys.Contains(key))
                return true;

            return false;
        }

        public static void Preview() {
            List<string> args = [.. FFArgsList];
            args.RemoveAll(a => a.Contains("-vcodec"));
            args.RemoveAll(a => a.Contains("-pix_fmt"));

            if (InVideo is null)
                args.Add("-x 1280 -y 720 -r 30 -vf fps=30"); // img seq
            else
                args.Add($"-x {Math.Round(InVideo.Aspect * 720)} -y 720"); // video


            Process ffplay = new() {
                StartInfo = new() {
                    FileName = FFPlay.FullName,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    WorkingDirectory = FFMpeg.DirectoryName
                }
            };
            ffplay.Start();
        }

        public static void Restart() {
            new Process() {
                StartInfo = new() {
                    FileName = Environment.ProcessPath,
                    Arguments = InVideo!.FullPathQuoted
                }
            }.Start();
            Environment.Exit(0);
        }

        public static void WriteSeparator() => Console.WriteLine("---------------------------------------------");

        public static void PlaySound() {
            if (!PlaySoundOnCompletion) 
                return;

            Task.Run(() => {
                if (OperatingSystem.IsWindows())
                    new SoundPlayer(Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoStuff.Resources.Sound.wav")).Play();
                else
                    Console.Beep();
            });
        }
    }
}
