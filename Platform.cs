using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClipHarbor;

/// <summary>Everything that differs between Windows, macOS and Linux.</summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsArm => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>The .AppImage file this copy runs from (Linux), or null. Inside an AppImage the program runs
    /// from a temporary read-only folder ($APPDIR), so anything that starts or replaces the app must use
    /// the .AppImage file ($APPIMAGE) instead. Both are checked, so a variable inherited from another
    /// AppImage that started this copy isn't taken for ours.</summary>
    public static string? AppImage
    {
        get
        {
            if (!IsLinux) return null;
            var file = Environment.GetEnvironmentVariable("APPIMAGE");
            var dir = Environment.GetEnvironmentVariable("APPDIR");
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(dir) || exe == null || !File.Exists(file)) return null;
            return exe.StartsWith(dir.TrimEnd('/') + "/", StringComparison.Ordinal) ? file : null;
        }
    }

    /// <summary>The program file to start, register or replace: the .AppImage when there is one.</summary>
    public static string? AppPath => AppImage ?? Environment.ProcessPath;

    /// <summary>File name of a program: "yt-dlp.exe" on Windows, "yt-dlp" elsewhere.</summary>
    public static string Exe(string name) => IsWindows ? name + ".exe" : name;

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the download list and settings are saved.</summary>
    public static string DataDir
    {
        get
        {
            // CLIPHARBOR_DATA_DIR lets testers run a copy with its own list and settings.
            var d = Environment.GetEnvironmentVariable("CLIPHARBOR_DATA_DIR") is { Length: > 0 } custom ? custom
                : IsMac
                ? Path.Combine(Home, "Library", "Application Support", "ClipHarbor")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipHarbor");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Where the download engine and converter are installed.</summary>
    public static string ToolsDir
    {
        get
        {
            // CLIPHARBOR_TOOLS_DIR lets testers try engine installs without touching the real copy.
            var d = Environment.GetEnvironmentVariable("CLIPHARBOR_TOOLS_DIR") is { Length: > 0 } custom ? custom
                : IsMac
                ? Path.Combine(Home, "Library", "Application Support", "ClipHarbor", "tools")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipHarbor", "tools");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string VideosDir()
    {
        var v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (IsMac) v = Path.Combine(Home, "Movies");
        if (string.IsNullOrEmpty(v) || (IsLinux && !Directory.Exists(v)))
        {
            var guess = Path.Combine(Home, "Videos");
            v = Directory.Exists(guess) || !IsLinux ? guess : Home;
        }
        return v;
    }

    /// <summary>Folders to look in besides PATH. Apps started from the Dock or a menu often get a short PATH.</summary>
    public static IEnumerable<string> ExtraToolDirs()
    {
        if (IsMac) return new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin" };
        if (IsLinux) return new[] { Path.Combine(Home, ".local", "bin"), "/usr/local/bin", "/usr/bin", "/snap/bin" };
        return Array.Empty<string>();
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { }
    }

    static void Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
        }
        catch { }
    }

    /// <summary>Opens a file or web address with the program the system uses for it.</summary>
    public static void Open(string target)
    {
        if (IsWindows)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); } catch { }
        }
        else Run(IsMac ? "open" : "xdg-open", target);
    }

    public static void ShowInFolder(string file)
    {
        if (IsWindows)
        {
            // Explorer wants /select,"path" with only the path quoted; quoting the whole argument
            // (what ArgumentList does for paths with spaces) opens the folder without selecting.
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = false })?.Dispose(); } catch { }
        }
        else if (IsMac) Run("open", "-R", file);
        else Open(Path.GetDirectoryName(file) ?? file);
    }

    public static void OpenFolder(string dir)
    {
        if (IsWindows) Run("explorer.exe", dir);
        else Open(dir);
    }

    static string? _timePattern;

    /// <summary>How this computer writes a time: "h:mm tt" (7:24 AM) or "HH:mm" (07:24).
    /// Windows and Linux settings come with the region format; on a Mac the separate
    /// "24-hour time" switch is read as well.</summary>
    public static string TimePattern => _timePattern ??= FindTimePattern();

    static string FindTimePattern()
    {
        if (IsMac)
        {
            if (MacSetting("AppleICUForce24HourTime") == "1") return "HH:mm";
            if (MacSetting("AppleICUForce12HourTime") == "1") return "h:mm tt";
        }
        return System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
    }

    static string? MacSetting(string key)
    {
        try
        {
            var psi = new ProcessStartInfo("defaults") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "read", "-g", key }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return p.ExitCode == 0 ? text : null;
        }
        catch { return null; }
    }

    public static string FileManagerName => IsWindows ? "File Explorer" : IsMac ? "Finder" : "your file manager";

    /// <summary>A small system notice. On Windows the taskbar button flashes instead.</summary>
    public static void Notify(string title, string body, IntPtr windowHandle)
    {
        if (IsMac)
            Run("osascript", "-e", $"display notification {AppleText(body)} with title {AppleText(title)}");
        else if (IsLinux)
            Run("notify-send", "--app-name=ClipHarbor", title, body);
        else if (IsWindows && windowHandle != IntPtr.Zero)
            Flash(windowHandle);
    }

    static string AppleText(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    static void Flash(IntPtr hwnd)
    {
        try
        {
            var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = 3 | 12, uCount = 5 }; // ALL | TIMERNOFG
            FlashWindowEx(ref f);
        }
        catch { }
    }
}
