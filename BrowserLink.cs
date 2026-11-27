namespace ClipHarbor;

/// <summary>
/// Links like clipharbor://download?url=… and clipharbor://gif?url=… that the browser extension
/// (or a bookmark) opens to send a page to ClipHarbor.
/// </summary>
public static class BrowserLink
{
    public const string Scheme = "clipharbor";
    const string DesktopFile = "io.github.clipharbor.desktop";

    public static bool IsLink(string s) => s.TrimStart().StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the page address and whether a GIF was asked for. Only http(s) pages are accepted.</summary>
    public static bool TryParse(string link, out string url, out bool gif)
    {
        url = ""; gif = false;
        try
        {
            var u = new Uri(link.Trim());
            if (!u.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            var action = (u.Host.Length > 0 ? u.Host : u.AbsolutePath.Trim('/')).ToLowerInvariant();
            gif = action == "gif";
            foreach (var part in u.Query.TrimStart('?').Split('&'))
            {
                var eq = part.IndexOf('=');
                if (eq > 0 && part[..eq] == "url") url = Uri.UnescapeDataString(part[(eq + 1)..]).Trim();
            }
            return Engine.IsUrl(url);
        }
        catch { return false; }
    }

    static string Exe => Platform.AppPath ??Path.Combine(AppContext.BaseDirectory, Platform.Exe("ClipHarbor"));

    /// <summary>
    /// Tells the system that clipharbor:// links open this copy of the app. Runs at every start so it
    /// follows the app if it's moved. The Mac app declares it in its Info.plist instead.
    /// </summary>
    public static void Register()
    {
        // Test copies with their own data folder leave the real app's entry alone.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLIPHARBOR_DATA_DIR"))) return;
        // A Flatpak's own desktop file already says it opens these links.
        if (Platform.IsFlatpak) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var command = $"\"{Exe}\" \"%1\"";
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
                using (var cmd = key.CreateSubKey(@"shell\open\command"))
                    if ((cmd.GetValue("") as string) == command) return; // already right
                key.SetValue("", "URL:ClipHarbor link");
                key.SetValue("URL Protocol", "");
                using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{Exe}\",0");
                using (var cmd = key.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
            }
            else if (OperatingSystem.IsLinux())
            {
                var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                var file = Path.Combine(data, "applications", DesktopFile);
                var text = "[Desktop Entry]\nType=Application\nName=ClipHarbor\nComment=Video and audio downloader\n" +
                           $"Exec=\"{Exe.Replace("\"", "\\\"")}\" %u\nTerminal=false\nNoDisplay=true\nMimeType=x-scheme-handler/{Scheme};\n";
                if (File.Exists(file) && File.ReadAllText(file) == text) return;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-mime", new[] { "default", DesktopFile, "x-scheme-handler/" + Scheme }) { UseShellExecute = false });
                    p?.WaitForExit(5000);
                }
                catch { }
            }
        }
        catch { }
    }
}
