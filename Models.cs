using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipHarbor;

public enum MediaKind { Video, Audio, Animation }

public enum ItemStatus { Queued, Downloading, Paused, Done, Failed, Canceled }

/// <summary>What to download and how. Used per item, for Smart Mode and for subscriptions.</summary>
public class DownloadOptions
{
    public MediaKind Kind { get; set; } = MediaKind.Video;
    /// <summary>Container: mp4/mkv/webm for video, mp3/m4a/opus/wav/flac for audio.</summary>
    public string Format { get; set; } = "mp4";
    /// <summary>Max video height, 0 = best available.</summary>
    public int MaxHeight { get; set; } = 1080;
    public bool Subtitles { get; set; }
    public bool EmbedSubtitles { get; set; } = true;
    public List<string> SubtitleLanguages { get; set; } = new() { "en" };
    public string Folder { get; set; } = "";
    public bool PlaylistSubfolder { get; set; } = true;
    /// <summary>GIF/WebP: largest width in pixels, 0 = keep the video's size.</summary>
    public int AnimationWidth { get; set; } = 480;
    /// <summary>GIF/WebP: also keep the downloaded video file.</summary>
    public bool KeepVideo { get; set; }
    /// <summary>Video: shrink the finished file to fit this many MB (e.g. 20 for Discord), 0 = no limit.</summary>
    public int TargetSizeMB { get; set; }

    public DownloadOptions Clone()
    {
        var c = (DownloadOptions)MemberwiseClone();
        c.SubtitleLanguages = new List<string>(SubtitleLanguages);
        return c;
    }

    public string Describe()
    {
        if (Kind == MediaKind.Audio) return $"{Format.ToUpperInvariant()} audio";
        if (Kind == MediaKind.Animation) return $"{Format.ToUpperInvariant()} · {(AnimationWidth > 0 ? AnimationWidth + " px" : "full size")}";
        if (TargetSizeMB > 0) return $"{Format.ToUpperInvariant()} · under {TargetSizeMB} MB";
        var q = MaxHeight <= 0 ? "Best" : QualityLabel(MaxHeight);
        return $"{Format.ToUpperInvariant()} · {q}";
    }

    /// <summary>The size limits offered: label and MB.</summary>
    public static readonly (string label, int mb)[] SizeLimits =
        { ("No limit", 0),("20 MB · Discord", 20),("50 MB · Nitro Basic or boosted server", 50), ("100 MB · server boost level 3", 100), ("500 MB · Discord Nitro", 500) };

    public static string QualityLabel(int h) => h switch
    {
        >= 4320 => "8K",
        >= 2160 => "4K",
        >= 1440 => "1440p",
        _ => h + "p"
    };
}

public class DownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Uploader { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string Playlist { get; set; } = "";
    public DownloadOptions Options { get; set; } = new();
    public ItemStatus Status { get; set; } = ItemStatus.Queued;
    public string Message { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DoneBytes { get; set; }
    public bool Direct { get; set; }
    /// <summary>Set when converting a file already on this computer (no download).</summary>
    public string SourceFile { get; set; } = "";
    /// <summary>The site only allowed this download with the browser sign-in.</summary>
    public bool UseSignIn { get; set; }
    /// <summary>A file name chosen for this download (without its ending); empty uses the usual name.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Only this part of the video, in seconds; -1 = the whole video.</summary>
    public double ClipStart { get; set; } = -1;
    public double ClipEnd { get; set; } = -1;
    [JsonIgnore] public bool IsClip => ClipStart >= 0 && ClipEnd > ClipStart;
    public DateTime Added { get; set; } = DateTime.Now;

    [JsonIgnore] public double Progress { get; set; }
    [JsonIgnore] public double Speed { get; set; }
    [JsonIgnore] public double Eta { get; set; } = -1;
    [JsonIgnore] public string Phase { get; set; } = "";
}

public class Subscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public DownloadOptions Options { get; set; } = new();
    public HashSet<string> Seen { get; set; } = new();
    public DateTime LastChecked { get; set; }
    public string LastResult { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class AppSettings
{
    public string Theme { get; set; } = "System";
    /// <summary>Set once Light (the old default) was moved to System, so a Light chosen afterwards stays.</summary>
    public bool ThemeMovedToSystem { get; set; }
    /// <summary>Language code (Lang.Choices); "" follows the computer.</summary>
    public string Language { get; set; } = "";
    public int MaxConcurrent { get; set; } = 3;
    public string SpeedLimit { get; set; } = "";
    /// <summary>Pieces downloaded at once for each download, on sites that send video in pieces (yt-dlp -N).</summary>
    public int Connections { get; set; } = 4;
    public string Proxy { get; set; } = "";
    public string FileNameTemplate { get; set; } = "%(title)s.%(ext)s";
    public string CookiesBrowser { get; set; } = "";
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedMetadata { get; set; } = true;
    public bool SkipExisting { get; set; } = true;
    public int SubscriptionCheckHours { get; set; } = 6;
    public string EnginePath { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public bool SmartMode { get; set; }
    public DownloadOptions SmartOptions { get; set; } = new();
    public DownloadOptions LastOptions { get; set; } = new();
    public bool NotifyWhenDone { get; set; } = true;
    /// <summary>Closing the window keeps ClipHarbor in the tray while there are subscriptions or downloads.</summary>
    public bool RunInBackground { get; set; } = true;
    /// <summary>Start hidden in the tray when the person signs in to their computer.</summary>
    public bool StartAtLogin { get; set; }
    /// <summary>The "keep running in the background?" question was answered once.</summary>
    public bool BackgroundAsked { get; set; }
    /// <summary>Look for a newer ClipHarbor on GitHub at start and once a day.</summary>
    public bool CheckAppUpdates { get; set; } = true;
    /// <summary>Download and put new versions in place by itself; they start at the next restart.</summary>
    public bool InstallAppUpdates { get; set; } = true;
    /// <summary>Update ClipHarbor's own copy of yt-dlp at start and once a day; sites change often.</summary>
    public bool UpdateEngineAutomatically { get; set; } = true;
    /// <summary>SponsorBlock on YouTube: "" off, "mark" as chapters, "remove" cut out.</summary>
    public string SponsorBlock { get; set; } = "";
}

public class AppState
{
    public AppSettings Settings { get; set; } = new();
    public List<DownloadItem> Items { get; set; } = new();
    public List<Subscription> Subscriptions { get; set; } = new();

    public static string DataDir => Platform.DataDir;

    static string FilePath => Path.Combine(DataDir, "state.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultFolder() => Path.Combine(Platform.VideosDir(), "ClipHarbor");

    public static AppState Load()
    {
        AppState s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), Json) ?? new AppState()
                : new AppState();
        }
        catch
        {
            // Keep a copy of a damaged file instead of overwriting it on the next save.
            try { File.Copy(FilePath, Path.Combine(DataDir, $"state.damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
            s = new AppState();
        }

        foreach (var o in new[] { s.Settings.SmartOptions, s.Settings.LastOptions })
            if (string.IsNullOrWhiteSpace(o.Folder)) o.Folder = DefaultFolder();
        foreach (var it in s.Items)
            if (it.Status == ItemStatus.Downloading) it.Status = ItemStatus.Paused;
        return s;
    }

    static readonly object WriteLock = new();

    /// <summary>The state as text. Call on the UI thread, where the lists are changed, so it's never caught mid-change.</summary>
    public string Snapshot() => JsonSerializer.Serialize(this, Json);

    /// <summary>Writes a snapshot to disk safely (whole file or nothing). Fine to call from a background thread.</summary>
    public static void Write(string text)
    {
        try
        {
            lock (WriteLock)
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, text);
                File.Move(tmp, FilePath, true);
            }
        }
        catch { /* never crash on save */ }
    }

    public void Save()
    {
        try { Write(Snapshot()); } catch { }
    }

    static StringComparison PathCase => Platform.IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Whether a queued item has to wait before it starts: its last run is still stopping (after a quick
    /// pause and resume), or another link with the same title is downloading into the same folder right now
    /// (both would write the same file at once).</summary>
    public bool MustWait<T>(DownloadItem it, IReadOnlyDictionary<string, T> running) =>
        running.ContainsKey(it.Id) || Items.Any(o => o != it && o.Status == ItemStatus.Downloading && o.Url != it.Url
            && string.Equals(o.Title, it.Title, StringComparison.OrdinalIgnoreCase) && o.Playlist == it.Playlist
            && string.Equals(o.Options.Folder, it.Options.Folder, PathCase));

    /// <summary>A finished download ended up with a file that belongs to a different link in the list: two
    /// videos with the same title, so the engine skipped this one as "already downloaded". Gives it a free
    /// name ("Title (2)") and queues it again. Returns whether it did.</summary>
    public bool TakeFreeNameIfShared(DownloadItem it)
    {
        // (With "skip files that already exist" off, the engine replaces files instead; nothing to fix up then.)
        if (!Settings.SkipExisting || it.SourceFile.Length > 0 || string.IsNullOrEmpty(it.FilePath)) return false;
        if (!Items.Any(o => o != it && o.Status == ItemStatus.Done && o.Url != it.Url && string.Equals(o.FilePath, it.FilePath, PathCase))) return false;
        var dir = Path.GetDirectoryName(it.FilePath) ?? "";
        var name = it.FileName.Length > 0 ? it.FileName : Path.GetFileNameWithoutExtension(it.FilePath);
        string free;
        int n = 2;
        do free = $"{name} ({n++})";
        while (Directory.Exists(dir) && Directory.EnumerateFiles(dir, Engine.SafeName(free) + ".*").Any());
        it.FileName = free;
        it.FilePath = "";
        it.Progress = 0; it.DoneBytes = 0; it.TotalBytes = 0;
        it.Status = ItemStatus.Queued;
        return true;
    }
}

public static class Fmt
{
    public static string Bytes(double b)
    {
        if (b <= 0) return "";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i == 0 ? $"{b:0} {u[i]}" : $"{b:0.#} {u[i]}";
    }

    /// <summary>A short date and time the way this computer shows them, e.g. "Nov 15, 7:24 AM" or "15 Nov, 07:24".</summary>
    public static string When(DateTime d)
    {
        var f = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        // Month and day in the local order, with a short month name ("MMMM d" -> "MMM d").
        var day = f.MonthDayPattern.Replace("MMMM", "MMM");
        return $"{d.ToString(day)}, {d.ToString(Platform.TimePattern)}";
    }

    public static string Duration(double s)
    {
        if (s <= 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>A point in a video: like Duration, but 0 shows as 0:00.</summary>
    public static string Time(double s) => s < 0.5 ? "0:00" : Duration(s);
}
