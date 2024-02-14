using System.Diagnostics;

namespace VideoStuff {
    internal class Program {

        public static FileInfo? InFile { get; set; }

        public static FileInfo FFMpeg { get; set; } = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));

        static readonly List<string> FFArgsList = [];
        static string FFArgs => String.Join(" ", FFArgsList);

        static string? suffix;

        static void Main(string[] args) {
            if (!File.Exists(args.FirstOrDefault()) || !FFMpeg.Exists) 
                return;

            InFile = new(args.FirstOrDefault()!);

            Console.WriteLine(InFile.Name);
            FFArgsList.Add($"-i \"{InFile.FullName}\"");

            WriteSeparator();

            ConsoleKey remuxOrConv = PromptUserKey("Remux (R) or Convert (C)? [C]: ");
            if (remuxOrConv == ConsoleKey.R)
                Remux();
            else if (remuxOrConv == ConsoleKey.C || remuxOrConv == ConsoleKey.Enter)
                Convert();

            RunFFMpeg();

            Console.WriteLine(FFArgs);

            WriteSeparator();

            Console.Write("Press Enter to Exit...");
            Console.ReadLine();
        }

        public static void Remux() {
            ConsoleKey map = PromptUserKey("Map All Audio Tracks (Y/N) [N]: ");

            suffix = ".remux";

            FFArgsList.Add("-c copy");
            if (map == ConsoleKey.Y) {
                FFArgsList.Add("-map 0");
                suffix += "Mapped";
            }
        }

        public static void Convert() {
            ConsoleKey cut = PromptUserKey("Cut Video? (Y/N) [N]: ");
            if (cut == ConsoleKey.Y) {
                string startTime = PromptUser("Start Time: ");
                if (String.IsNullOrEmpty(startTime)) 
                    startTime = "0";
                FFArgsList.Add($"-ss {startTime}");

                string? endTime = PromptUser("End Time: ");
                if (!String.IsNullOrEmpty(endTime))
                    FFArgsList.Add($"-to {endTime}");

                suffix = ".cut";
            }
            else
                suffix = ".conv";
        }

        public static void RunFFMpeg() {
            FFArgsList.Add("\"" + Path.Combine(Path.GetDirectoryName(InFile!.FullName)!, Path.GetFileNameWithoutExtension(InFile!.Name) + suffix + ".mp4") + "\"");
            /*new Process() { 
                StartInfo = new ProcessStartInfo() {
                    FileName = FFMpeg.FullName,
                    Arguments = FFArgs
                }
            }.Start();*/
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
