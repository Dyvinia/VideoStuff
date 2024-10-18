using System.Runtime.InteropServices;

namespace VideoStuff {
    class ConsoleExtension {
        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        const int SW_MINIMIZE = 6;

        readonly static IntPtr handle = GetConsoleWindow();
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void Hide() => ShowWindow(handle, SW_HIDE);
        public static void Show() => ShowWindow(handle, SW_SHOW);
        public static void Minimize() => ShowWindow(handle, SW_MINIMIZE);
        public static void Focus() => SetForegroundWindow(handle);
    }
}
