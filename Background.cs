using System.IO.Pipes;
using System.Text;

namespace ClipHarbor;

/// <summary>Makes sure only one ClipHarbor runs per user. Starting it again hands the links
/// (or a "show the window" request) to the copy that's already running, then exits.</summary>
public static class SingleInstance
{
    static string PipeName
    {
        get
        {
            var user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            var dataDir = Environment.GetEnvironmentVariable("CLIPHARBOR_DATA_DIR");
            // A separate data folder (used for testing) is a separate app.
            // (string.GetHashCode differs between runs, so use a fixed checksum.)
            var suffix = string.IsNullOrEmpty(dataDir) ? "" : "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dataDir)))[..12];
            return "ClipHarbor-" + (user.Length > 0 ? user : "user") + suffix;
        }
    }

    public const string Show = "--show";

    /// <summary>True when another copy is running and took the message, so this one should exit.</summary>
    public static bool TryHandOff(IEnumerable<string> messages)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(400);
            using var w = new StreamWriter(c, new UTF8Encoding(false));
            w.Write(string.Join("\n", messages));
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Listens for later starts of the app. Each message arrives as a list of lines.</summary>
    public static void Listen(Action<string[]> onMessage)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var r = new StreamReader(server, Encoding.UTF8);
                    var text = await r.ReadToEndAsync();
                    onMessage(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                catch
                {
                    // Couldn't listen (e.g. a leftover from a crash on macOS/Linux): try again later, the app works without it.
                    await Task.Delay(5000);
                }
            }
        });
    }
}

/// <summary>Starting ClipHarbor (hidden, in the tray) when the person signs in to their computer.</summary>
public static class StartAtLogin
{
    public const string Flag = "--background";
    const string Name = "ClipHarbor";

    static string Exe => Platform.AppPath ??Path.Combine(AppContext.BaseDirectory, Platform.Exe("ClipHarbor"));
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string MacAgent => Path.Combine(Home, "Library", "LaunchAgents", "io.github.clipharbor.plist");
    static string LinuxAutostart => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".config"), "autostart", "clipharbor.desktop");

    /// <summary>Turns starting at sign-in on or off. Also refreshes the entry if the app was moved. Returns false if it failed.</summary>
    public static bool Set(bool on)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (on) key.SetValue(Name, $"\"{Exe}\" {Flag}");
                else if (key.GetValue(Name) != null) key.DeleteValue(Name);
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (on)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(MacAgent)!);
                    File.WriteAllText(MacAgent, $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                        <plist version="1.0">
                        <dict>
                          <key>Label</key><string>io.github.clipharbor</string>
                          <key>ProgramArguments</key>
                          <array><string>{System.Security.SecurityElement.Escape(Exe)}</string><string>{Flag}</string></array>
                          <key>RunAtLoad</key><true/>
                        </dict>
                        </plist>
                        """);
                }
                else if (File.Exists(MacAgent)) File.Delete(MacAgent);
            }
            else
            {
                if (on)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LinuxAutostart)!);
                    File.WriteAllText(LinuxAutostart,
                        "[Desktop Entry]\nType=Application\nName=ClipHarbor\nComment=Video and audio downloader\n" +
                        $"Exec=\"{Exe.Replace("\"", "\\\"")}\" {Flag}\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
                }
                else if (File.Exists(LinuxAutostart)) File.Delete(LinuxAutostart);
            }
            return true;
        }
        catch { return false; }
    }
}
