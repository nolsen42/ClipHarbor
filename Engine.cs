using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ClipHarbor;

/// <summary>Result of looking at a link before downloading.</summary>
public class LinkInfo
{
    public string Url = "";
    public string Title = "";
    public string Uploader = "";
    public string Thumbnail = "";
    public double Duration;
    public bool IsPlaylist;
    public bool IsDirectFile;
    /// <summary>The site only answered with the browser sign-in.</summary>
    public bool NeedsSignIn;
    public long DirectSize;
    public List<int> Heights = new();
    public Dictionary<int, long> SizeByHeight = new();
    public long AudioSize;
    public List<string> SubtitleLangs = new();
    public List<string> AutoSubtitleLangs = new();
    public List<PlaylistEntry> Entries = new();
}

public class PlaylistEntry
{
    public string Url { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Uploader { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public double Duration { get; set; }
    public bool Selected { get; set; } = true;
}

/// <summary>Finds and drives the open-source download engine (yt-dlp) and ffmpeg.</summary>
public static class Engine
{
    public static AppSettings Settings = new();
    public static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClipHarbor/1.0");
        return h;
    }

    public static string ToolsDir => Platform.ToolsDir;

    static string? FindTool(string name, string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var exe = Platform.Exe(name);
        foreach (var dir in new[] { ToolsDir, AppContext.BaseDirectory })
        {
            var p = Path.Combine(dir, exe);
            if (File.Exists(p)) return p;
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(Platform.ExtraToolDirs()))
        {
            try { var p = Path.Combine(dir.Trim('"'), exe); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    // Looking for the tools means checking many folders, and the status bar asks several times a second,
    // so the answer is remembered for a few seconds (and forgotten right after installing).
    static readonly object ToolLock = new();
    static string? _ytDlp, _ffmpeg, _ytConfigured, _ffConfigured;
    static DateTime _toolsFound = DateTime.MinValue;

    static void FindTools()
    {
        lock (ToolLock)
        {
            if (DateTime.UtcNow - _toolsFound < TimeSpan.FromSeconds(5) && _ytConfigured == Settings.EnginePath && _ffConfigured == Settings.FfmpegPath) return;
            _ytConfigured = Settings.EnginePath; _ffConfigured = Settings.FfmpegPath;
            _ytDlp = FindTool("yt-dlp", _ytConfigured);
            _ffmpeg = FindTool("ffmpeg", _ffConfigured);
            _toolsFound = DateTime.UtcNow;
        }
    }

    public static void ForgetTools() { lock (ToolLock) _toolsFound = DateTime.MinValue; }

    public static string? YtDlp { get { FindTools(); return _ytDlp; } }
    public static string? Ffmpeg { get { FindTools(); return _ffmpeg; } }
    public static bool HasEngine => YtDlp != null;
    public static bool HasFfmpeg => Ffmpeg != null;

    static readonly string[] DirectExt = { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac", ".aac", ".flv", ".wmv", ".3gp", ".gif" };

    public static bool IsMediaExt(string ext) => DirectExt.Contains(ext.ToLowerInvariant());

    /// <summary>Some sites show a file inside their own viewer page (e.g. reddit.com/media?url=...).
    /// Returns the address of the file itself so it can be downloaded directly.</summary>
    public static string Unwrap(string url)
    {
        try
        {
            var u = new Uri(url.Trim());
            var host = u.Host.ToLowerInvariant();
            bool redditViewer = (host == "reddit.com" || host.EndsWith(".reddit.com")) && u.AbsolutePath.TrimEnd('/') == "/media";
            if (redditViewer)
            {
                foreach (var part in u.Query.TrimStart('?').Split('&'))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0] == "url")
                    {
                        var inner = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
                        if (IsUrl(inner)) return inner;
                    }
                }
            }
        }
        catch { }
        return url.Trim();
    }

    public static bool LooksDirect(string url)
    {
        try
        {
            var p = new Uri(url).AbsolutePath.ToLowerInvariant();
            return DirectExt.Any(p.EndsWith);
        }
        catch { return false; }
    }

    public static bool IsUrl(string s) =>
        Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    // ---------- process helpers ----------

    static ProcessStartInfo Psi(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    static bool BrowserChosen => !string.IsNullOrWhiteSpace(Settings.CookiesBrowser);

    /// <summary>Options used on every run. The browser sign-in is only added when asked for, because reading
    /// it can fail (locked browsers) and most links don't need it.</summary>
    static List<string> CommonArgs(bool signIn = false)
    {
        var a = new List<string> { "--encoding", "utf-8", "--no-warnings" };
        if (!string.IsNullOrWhiteSpace(Settings.Proxy)) { a.Add("--proxy"); a.Add(Settings.Proxy.Trim()); }
        if (signIn && BrowserChosen) { a.Add("--cookies-from-browser"); a.Add(Settings.CookiesBrowser.Trim().ToLowerInvariant()); }
        var ff = Ffmpeg;
        if (ff != null) { a.Add("--ffmpeg-location"); a.Add(Path.GetDirectoryName(ff)!); }
        return a;
    }

    static async Task<(int code, string stdout, string stderr)> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var exe = YtDlp ?? throw new InvalidOperationException("The download engine isn't installed yet.");
        using var p = Process.Start(Psi(exe, args))!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        ct.ThrowIfCancellationRequested();
        return (p.ExitCode, await o, await e);
    }

    static string LastError(string stderr)
    {
        var lines = stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var err = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? "Unknown error";
        return err.StartsWith("ERROR:") ? err[6..].Trim() : err;
    }

    static string CleanError(string stderr, bool usedSignIn = false)
    {
        var err = LastError(stderr);
        var friendly = Friendly(err, usedSignIn);
        if (friendly != null) return friendly.Value.message;
        return err.Length > 300 ? err[..300] + "…" : err;
    }

    /// <summary>The error to throw for a failed engine run. Sign-in problems get their own type so the app can offer Settings.</summary>
    static Exception Fail(string stderr, bool usedSignIn)
    {
        var err = LastError(stderr);
        var friendly = Friendly(err, usedSignIn);
        if (friendly is { } f && f.problem != SignInProblem.None) return new SignInNeededException(f.message, f.problem);
        return new InvalidOperationException(CleanError(stderr, usedSignIn));
    }

    static string BrowserName(string b) => b.Length == 0 ? "" : char.ToUpperInvariant(b[0]) + b[1..];

    /// <summary>Plain-language versions of the engine's most common problems.</summary>
    /// <param name="usedSignIn">Whether this run already used the browser sign-in.</param>
    public static (string message, SignInProblem problem)? Friendly(string err, bool usedSignIn = false)
    {
        var browser = (Settings.CookiesBrowser ?? "").Trim().ToLowerInvariant();
        bool chromeFamily = browser is "" or "chrome" or "edge" or "brave" or "opera" or "vivaldi" or "chromium";
        string tip = Platform.IsWindows && chromeFamily ? " On Windows, Firefox works best: Chrome and Edge lock their sign-ins away from other programs." : "";

        if ((err.Contains("Could not copy") && err.Contains("cookie database")) || err.Contains("Failed to decrypt") || (err.Contains("could not find") && err.Contains("cookies database")))
            return ($"This link needs you to be signed in, but ClipHarbor couldn't read your sign-in from {BrowserName(browser)}. Close {BrowserName(browser)} completely and try again, or choose another browser in Settings.{tip}", SignInProblem.BrowserUnreadable);

        var site = SiteName(err);
        bool x = site == "X";
        bool xWantsSignIn = x && (err.Contains("No video could be found") || err.Contains("NSFW") || err.Contains("age-restricted") || err.Contains("log in") || err.Contains("login"));
        // yt-dlp's own hints that signing in is the fix, on any site (Reddit, Instagram, YouTube, ...).
        string[] signInWords =
        {
            "--cookies-from-browser", "--cookies", "authentication is required", "requires authentication", "login required", "Login required",
            "log in to", "Log in to", "logged-in", "Sign in to confirm", "age-restricted", "Private video", "members-only", "This content isn't available to everyone",
        };
        bool siteWantsSignIn = signInWords.Any(err.Contains);
        if (!xWantsSignIn && !siteWantsSignIn) return null;

        string where = site.Length > 0 ? site : "the site";
        string msg = usedSignIn
            ? (x ? $"X still didn't share a video for this post, even with your sign-in from {BrowserName(browser)}. Check that you're signed in to X there and that the post really has a video or GIF."
                 : $"{Cap(where)} still refused, even with your sign-in from {BrowserName(browser)}. Check that you're signed in to {where} there and allowed to see this video.")
            : (x ? $"X only shows this post to people who are signed in (it's probably marked sensitive). Turn on \"Use sign-in from browser\" in Settings and pick a browser where you're signed in to X.{tip}"
                 : $"{Cap(where)} only shares this video with people who are signed in. Turn on \"Use sign-in from browser\" in Settings and pick a browser where you're signed in to {where}.{tip}");
        return (msg, SignInProblem.SiteWantsSignIn);
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The site's name from an engine message like "[Reddit] abc: ..." or "[youtube:tab] ...".</summary>
    static string SiteName(string err)
    {
        var m = System.Text.RegularExpressions.Regex.Match(err, @"^\[([^\]:]+)");
        if (!m.Success) return "";
        var key = m.Groups[1].Value;
        return key.ToLowerInvariant() switch
        {
            "twitter" => "X",
            "youtube" => "YouTube",
            "tiktok" => "TikTok",
            "instagram" => "Instagram",
            "facebook" => "Facebook",
            "reddit" => "Reddit",
            "twitch" or "twitch.tv" => "Twitch",
            "vimeo" => "Vimeo",
            "generic" => "",
            _ => key,
        };
    }

    /// <summary>Runs the engine without sign-in first; if the site says sign-in is needed and a browser is chosen,
    /// runs it again with the browser's sign-in. Returns the output and whether sign-in was used.</summary>
    static async Task<(string stdout, bool usedSignIn)> RunWithSignInAsync(Func<bool, List<string>> buildArgs, bool startWithSignIn, CancellationToken ct)
    {
        bool signIn = startWithSignIn && BrowserChosen;
        var (code, o, e) = await RunAsync(buildArgs(signIn), ct);
        if (code == 0 && !string.IsNullOrWhiteSpace(o)) return (o, signIn);
        var fail = Fail(e, signIn);
        if (signIn || !BrowserChosen || fail is not SignInNeededException { Problem: SignInProblem.SiteWantsSignIn }) throw fail;
        (code, o, e) = await RunAsync(buildArgs(true), ct);
        if (code == 0 && !string.IsNullOrWhiteSpace(o)) return (o, true);
        throw Fail(e, true);
    }

    // ---------- link analysis ----------

    /// <summary>
    /// A YouTube video opened from a Mix (list=RD…) is about that one video: a Mix is an endless,
    /// made-up-on-the-spot list, so drop it. Real playlists (list=PL…) are kept.
    /// </summary>
    public static string WithoutMix(string url)
    {
        try
        {
            var u = new Uri(url);
            var host = u.Host.ToLowerInvariant();
            if (!(host == "youtube.com" || host.EndsWith(".youtube.com"))) return url;
            var parts = u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
            bool hasVideo = parts.Any(p => p.StartsWith("v="));
            bool mix = parts.Any(p => p.StartsWith("list=RD", StringComparison.Ordinal));
            if (!hasVideo || !mix) return url;
            var keep = parts.Where(p => !p.StartsWith("list=") && !p.StartsWith("index=") && !p.StartsWith("start_radio=") && !p.StartsWith("pp=")).ToList();
            return new UriBuilder(u) { Query = string.Join("&", keep) }.Uri.AbsoluteUri;
        }
        catch { return url; }
    }

    public static async Task<LinkInfo> AnalyzeAsync(string url, CancellationToken ct, int playlistLimit = 0)
    {
        url = WithoutMix(Unwrap(url));
        if (!HasEngine || LooksDirect(url))
        {
            if (!HasEngine && !LooksDirect(url))
                throw new InvalidOperationException("ENGINE_MISSING");
            return await AnalyzeDirectAsync(url, ct);
        }

        List<string> Args(bool signIn)
        {
            var args = CommonArgs(signIn);
            args.AddRange(new[] { "-J", "--flat-playlist" });
            if (playlistLimit > 0) { args.Add("--playlist-end"); args.Add(playlistLimit.ToString()); }
            args.Add(url);
            return args;
        }
        var (stdout, usedSignIn) = await RunWithSignInAsync(Args, false, ct);

        using var doc = JsonDocument.Parse(stdout);
        var r = doc.RootElement;
        var info = new LinkInfo
        {
            NeedsSignIn = usedSignIn,
            Url = Str(r, "webpage_url") is { Length: > 0 } wu ? wu : url,
            Title = Str(r, "title"),
            Uploader = Str(r, "uploader") is { Length: > 0 } up ? up : Str(r, "channel"),
            Thumbnail = PickThumb(r),
            Duration = Num(r, "duration"),
        };
        if (Str(r, "_type") == "playlist" && r.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            info.IsPlaylist = true;
            foreach (var e in entries.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = Str(e, "id");
                var eu = Str(e, "url");
                if (string.IsNullOrEmpty(eu)) eu = Str(e, "webpage_url");
                var ie = Str(e, "ie_key").ToLowerInvariant();
                if ((ie == "youtube" || (eu.Length == 11 && !eu.Contains('/'))) && id.Length > 0)
                    eu = "https://www.youtube.com/watch?v=" + id;
                if (string.IsNullOrEmpty(eu)) continue;
                var thumb = PickThumb(e);
                if (thumb.Length == 0 && ie == "youtube" && id.Length > 0) thumb = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";
                info.Entries.Add(new PlaylistEntry
                {
                    Url = eu,
                    Id = id.Length > 0 ? id : eu,
                    Title = Str(e, "title") is { Length: > 0 } t ? t : eu,
                    Uploader = Str(e, "uploader") is { Length: > 0 } u2 ? u2 : Str(e, "channel"),
                    Thumbnail = thumb,
                    Duration = Num(e, "duration"),
                });
            }
            if (info.Thumbnail.Length == 0) info.Thumbnail = info.Entries.FirstOrDefault()?.Thumbnail ?? "";
            return info;
        }

        // single video: gather heights, sizes and subtitles
        long bestAudio = 0;
        if (r.TryGetProperty("formats", out var fmts) && fmts.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fmts.EnumerateArray())
            {
                var vcodec = Str(f, "vcodec");
                var acodec = Str(f, "acodec");
                long size = (long)Math.Max(Num(f, "filesize"), Num(f, "filesize_approx"));
                int h = (int)Num(f, "height");
                if (h > 0 && vcodec != "none")
                {
                    if (!info.Heights.Contains(h)) info.Heights.Add(h);
                    if (size > 0 && (!info.SizeByHeight.TryGetValue(h, out var cur) || size > cur)) info.SizeByHeight[h] = size;
                }
                else if (vcodec == "none" && acodec != "none" && size > bestAudio) bestAudio = size;
            }
        }
        info.AudioSize = bestAudio;
        info.Heights.Sort((a, b) => b.CompareTo(a));
        info.SubtitleLangs = Keys(r, "subtitles");
        info.AutoSubtitleLangs = Keys(r, "automatic_captions").Where(k => !k.Contains('-') || k.Length <= 5).Take(40).ToList();
        if (string.IsNullOrEmpty(info.Title)) info.Title = url;
        return info;
    }

    static async Task<LinkInfo> AnalyzeDirectAsync(string url, CancellationToken ct)
    {
        var info = new LinkInfo { Url = url, IsDirectFile = true };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Http.SendAsync(req, ct);
            info.DirectSize = resp.Content.Headers.ContentLength ?? 0;
            var cd = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(cd)) info.Title = cd.Trim('"');
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        if (string.IsNullOrEmpty(info.Title))
        {
            try { info.Title = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath)); } catch { }
            if (string.IsNullOrEmpty(info.Title)) info.Title = "download";
        }
        return info;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static List<string> Keys(JsonElement e, string name)
    {
        var l = new List<string>();
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var p in v.EnumerateObject()) if (p.Name != "live_chat") l.Add(p.Name);
        return l;
    }

    static string PickThumb(JsonElement e)
    {
        var t = Str(e, "thumbnail");
        if (t.Length > 0) return t;
        if (e.TryGetProperty("thumbnails", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            string best = ""; double bw = 0;
            foreach (var th in arr.EnumerateArray())
            {
                var u = Str(th, "url"); var w = Num(th, "width");
                if (u.Length == 0) continue;
                if (best.Length == 0 || (w > bw && w <= 640)) { best = u; bw = w; }
            }
            return best;
        }
        return "";
    }

    // ---------- downloading ----------

    public static List<string> BuildDownloadArgs(DownloadItem it, bool signIn = false)
    {
        var o = it.Options;
        var s = Settings;
        var a = CommonArgs(signIn);
        bool ff = HasFfmpeg;
        a.AddRange(new[] { "--newline", "--no-playlist", "--no-mtime", "--progress", "--no-simulate" });
        a.AddRange(new[] { "--progress-template", "download:@@P %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.speed)s %(progress.eta)s" });
        a.AddRange(new[] { "--print", "after_move:@@F %(filepath)s" });

        var folder = string.IsNullOrWhiteSpace(o.Folder) ? AppState.DefaultFolder() : o.Folder;
        if (o.PlaylistSubfolder && !string.IsNullOrWhiteSpace(it.Playlist)) folder = Path.Combine(folder, SafeName(it.Playlist));
        a.Add("-P"); a.Add(folder);
        a.Add("-o");
        if (it.FileName.Length > 0) a.Add(SafeName(it.FileName).Replace("%", "%%") + ".%(ext)s"); // a name typed for this download
        else a.Add(string.IsNullOrWhiteSpace(s.FileNameTemplate) ? "%(title)s.%(ext)s" : s.FileNameTemplate);
        if (!string.IsNullOrWhiteSpace(s.SpeedLimit)) { a.Add("-r"); a.Add(s.SpeedLimit.Trim()); }
        if (!s.SkipExisting) a.Add("--force-overwrites");

        if (o.Kind == MediaKind.Animation)
        {
            // Only the picture is needed; prefer a single ready-made file so nothing has to be joined.
            a.AddRange(new[] { "-f", "bv*[height<=1080][ext=mp4]/b[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]/bv*/b" });
        }
        else if (o.Kind == MediaKind.Audio)
        {
            if (ff)
            {
                a.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", o.Format, "--audio-quality", "0" });
            }
            else
            {
                a.AddRange(new[] { "-f", o.Format == "m4a" ? "ba[ext=m4a]/ba/b" : "ba/b" });
            }
        }
        else
        {
            string h = o.MaxHeight > 0 ? $"[height<={o.MaxHeight}]" : "";
            if (ff)
            {
                var sort = o.MaxHeight > 0 ? $"res:{o.MaxHeight}" : "res";
                if (o.Format == "mp4") sort += ",vcodec:h264,ext:mp4:m4a";
                a.AddRange(new[] { "-f", $"bv*{h}+ba/b{h}/bv*+ba/b", "-S", sort, "--merge-output-format", o.Format });
                if (o.Format == "mp4") { a.Add("--remux-video"); a.Add("mp4"); }
            }
            else
            {
                a.AddRange(new[] { "-f", o.Format == "mp4" ? $"b{h}[ext=mp4]/b{h}/b" : $"b{h}/b" });
            }
        }

        if (o.Subtitles && o.Kind == MediaKind.Video)
        {
            a.AddRange(new[] { "--write-subs", "--write-auto-subs", "--sub-langs", o.SubtitleLanguages.Count > 0 ? string.Join(",", o.SubtitleLanguages) : "en.*" });
            if (ff)
            {
                // WebM can only hold WebVTT subtitles; SRT would stay beside the video instead.
                a.AddRange(new[] { "--convert-subs", o.EmbedSubtitles && o.Format == "webm" ? "vtt" : "srt" });
                // yt-dlp keeps the .srt files next to the video when --write-subs is given with
                // --embed-subs; no-keep-subs deletes them once they're inside the video.
                if (o.EmbedSubtitles) a.AddRange(new[] { "--embed-subs", "--compat-options", "no-keep-subs" });
            }
        }
        if (it.IsClip && ff)
        {
            // Only that stretch is downloaded; cutting exactly at the times re-encodes a moment around each cut.
            a.AddRange(new[] { "--download-sections", $"*{Secs(it.ClipStart)}-{Secs(it.ClipEnd)}", "--force-keyframes-at-cuts" });
        }
        // SponsorBlock (YouTube only; yt-dlp ignores it elsewhere). Its times are for the whole video, so not for clips.
        if (ff && !it.IsClip && o.Kind != MediaKind.Animation && s.SponsorBlock is "mark" or "remove")
            a.AddRange(new[] { s.SponsorBlock == "remove" ? "--sponsorblock-remove" : "--sponsorblock-mark", SponsorCategories });
        if (ff && s.EmbedMetadata && o.Kind != MediaKind.Animation) a.Add("--embed-metadata");
        if (ff && s.EmbedThumbnail && (o.Kind == MediaKind.Video ? o.Format is "mp4" or "mkv" : o.Format is "mp3" or "m4a" or "flac" or "opus")) a.Add("--embed-thumbnail");
        a.Add("--");
        a.Add(it.Url);
        return a;
    }

    /// <summary>Paid promotions, self-promotion and "like and subscribe" reminders.</summary>
    const string SponsorCategories = "sponsor,selfpromo,interaction";

    static string Secs(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A time for a file name, without colons (not allowed on Windows): 1m05s, 1h02m03s.</summary>
    public static string TimeForName(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(s)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s" : $"{t.Minutes}m{t.Seconds:00}s";
    }

    /// <summary>Reads a time typed as 90, 1:30, 1:02:03 or 1:30.5. Returns -1 when it can't be read.</summary>
    public static double ParseTime(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return -1;
        double total = 0;
        foreach (var part in t.Split(':'))
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return -1;
            total = total * 60 + v;
        }
        return total;
    }

    // The Windows list on every system, so folders stay valid when copied between computers.
    static readonly char[] BadNameChars = "<>:\"/\\|?*".Concat(Enumerable.Range(0, 32).Select(i => (char)i)).ToArray();

    public static string SafeName(string s)
    {
        var bad = BadNameChars;
        var r = new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (r.Length > 120) r = r[..120];
        return r.Length == 0 ? "download" : r;
    }

    /// <summary>Runs one download; reports progress through the item fields. Returns when finished.</summary>
    public static async Task RunDownloadAsync(DownloadItem it, CancellationToken ct)
    {
        bool animation = it.Options.Kind == MediaKind.Animation;
        if (animation && !HasFfmpeg)
            throw new InvalidOperationException("Making GIF or WebP files needs the converter (ffmpeg). Open Settings → Download engine to add it.");

        bool shrink = it.Options.Kind == MediaKind.Video && it.Options.TargetSizeMB > 0;
        if (shrink && !HasFfmpeg)
            throw new InvalidOperationException("Making a video smaller needs the converter (ffmpeg). Open Settings → Download engine to add it.");
        if (it.IsClip && !HasFfmpeg)
            throw new InvalidOperationException("Downloading part of a video needs the converter (ffmpeg). Open Settings → Download engine to add it.");

        // Converting a file that's already on this computer.
        if (it.SourceFile.Length > 0)
        {
            if (!File.Exists(it.SourceFile)) throw new InvalidOperationException("The video file isn't there anymore: " + it.SourceFile);
            if (animation) await MakeAnimationAsync(it, it.SourceFile, keepSource: true, ct);
            else if (shrink) await ShrinkAsync(it, it.SourceFile, keepSource: true, ct);
            return;
        }

        if (it.Direct || (!HasEngine && LooksDirect(it.Url)))
            await DirectDownloadAsync(it, ct);
        else
            await YtDlpDownloadAsync(it, ct);

        if (animation || shrink)
        {
            if (string.IsNullOrEmpty(it.FilePath) || !File.Exists(it.FilePath))
                throw new InvalidOperationException("The video downloaded, but its file couldn't be found to convert.");
            if (animation) await MakeAnimationAsync(it, it.FilePath, it.Options.KeepVideo, ct);
            else await ShrinkAsync(it, it.FilePath, keepSource: false, ct);
        }
    }

    // ---------- fit to a size ----------

    /// <summary>Re-encodes a video (H.264 + AAC in MP4, two passes) so it fits in Options.TargetSizeMB.
    /// The picture gets smaller when the size leaves little room per second. A file that already fits is kept as it is.</summary>
    public static async Task ShrinkAsync(DownloadItem it, string source, bool keepSource, CancellationToken ct)
    {
        var ff = Ffmpeg ?? throw new InvalidOperationException("Making a video smaller needs the converter (ffmpeg). Open Settings → Download engine to add it.");
        // Sites differ on what "10 MB" means; 1 MB = 1,000,000 bytes is under the limit either way.
        long limit = (long)it.Options.TargetSizeMB * 1_000_000;
        long size = new FileInfo(source).Length;
        if (size <= limit)
        {
            // Already small enough. From the menu, say so instead of making a copy.
            if (keepSource) throw new InvalidOperationException($"This video is already under {it.Options.TargetSizeMB} MB ({Fmt.Bytes(size)}).");
            it.FilePath = source; it.TotalBytes = size;
            return;
        }

        it.Phase = "shrinking"; it.Progress = 0; it.Speed = 0; it.Eta = -1;
        var (duration, hasAudio, height) = await ProbeAsync(ff, source, ct);
        if (duration <= 0) throw new InvalidOperationException("Couldn't tell how long this video is, so it can't be fitted to a size.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
        var name = Path.GetFileNameWithoutExtension(source);
        // A copy from the menu gets its own name; after a download it simply replaces the big file.
        var baseName = keepSource ? $"{name} ({it.Options.TargetSizeMB} MB)" : name;
        var target = Path.Combine(dir, baseName + ".mp4");
        for (int n = 2; File.Exists(target) && !string.Equals(target, source, StringComparison.OrdinalIgnoreCase); n++) target = Path.Combine(dir, $"{baseName} ({n}).mp4");
        var tmp = Path.Combine(dir, baseName + ".shrinking.mp4");
        var passLog = Path.Combine(Path.GetTempPath(), "clipharbor-" + it.Id);

        // Leave about 4% for the MP4 container; try once more with less if the first result is still too big.
        double budget = 0.96;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                double totalKbps = limit * 8 * budget / 1000 / duration;
                int audioKbps = !hasAudio ? 0 : totalKbps > 1000 ? 128 : totalKbps > 400 ? 96 : totalKbps > 160 ? 64 : 32;
                int videoKbps = (int)(totalKbps - audioKbps);
                if (videoKbps < 60)
                    throw new InvalidOperationException($"This video is too long to look OK in {it.Options.TargetSizeMB} MB. Try downloading only part of it, or a bigger size.");
                // Fewer pixels when there's little room per second keeps it sharp instead of blocky.
                int maxH = videoKbps >= 3500 ? 1080 : videoKbps >= 1200 ? 720 : videoKbps >= 600 ? 480 : videoKbps >= 300 ? 360 : 240;
                if (height > 0) maxH = Math.Min(maxH, height);
                var vf = $"scale=-2:'min({maxH},ih)'";
                // 0:V is the video itself, not an embedded cover picture.
                var common = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-map", "0:V:0", "-vf", vf, "-c:v", "libx264", "-preset", "medium",
                    "-b:v", videoKbps + "k", "-maxrate", (int)(videoKbps * 1.4) + "k", "-bufsize", videoKbps * 2 + "k", "-passlogfile", passLog, "-progress", "pipe:1", "-nostats" };

                // -pass:v, not -pass: only the picture uses two passes (the sound has no first-pass log).
                var pass1 = new List<string>(common) { "-pass:v", "1", "-an", "-f", "null", "-" };
                await RunFfmpegAsync(ff, pass1, duration, 0, 0.5, it, ct);
                var pass2 = new List<string>(common) { "-pass:v", "2" };
                if (hasAudio) pass2.AddRange(new[] { "-map", "0:a:0", "-c:a", "aac", "-b:a", audioKbps + "k", "-ac", "2" }); else pass2.Add("-an");
                pass2.AddRange(new[] { "-movflags", "+faststart", "-map_metadata", "0", "-f", "mp4", tmp });
                await RunFfmpegAsync(ff, pass2, duration, 0.5, 0.5, it, ct);

                if (new FileInfo(tmp).Length <= limit) break;
                if (attempt == 1) throw new InvalidOperationException($"Couldn't get this video under {it.Options.TargetSizeMB} MB. Try a bigger size or only part of it.");
                budget *= 0.85 * limit / new FileInfo(tmp).Length;
            }
            if (!keepSource) try { File.Delete(source); } catch { }
            File.Move(tmp, target, true);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), "clipharbor-" + it.Id + "*")) try { File.Delete(f); } catch { }
        }
        it.FilePath = target;
        it.TotalBytes = new FileInfo(target).Length;
        it.Progress = 1; it.Phase = "";
    }

    /// <summary>Length in seconds, whether there's sound, and the picture's height, read from ffmpeg's description of the file.</summary>
    static async Task<(double duration, bool audio, int height)> ProbeAsync(string ff, string file, CancellationToken ct)
    {
        using var p = Process.Start(Psi(ff, new[] { "-hide_banner", "-nostdin", "-i", file }))!;
        _ = p.StandardOutput.ReadToEndAsync(ct);
        var text = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        double d = 0; int h = 0;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (m.Success) d = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var v = System.Text.RegularExpressions.Regex.Match(text, @"Stream #.*Video:.*?, (\d{2,5})x(\d{2,5})");
        if (v.Success) h = int.Parse(v.Groups[2].Value);
        return (d, text.Contains("Audio:"), h);
    }

    /// <summary>Runs ffmpeg with -progress pipe:1, reporting progress from <paramref name="from"/> over <paramref name="span"/>.</summary>
    static async Task RunFfmpegAsync(string ff, List<string> args, double duration, double from, double span, DownloadItem it, CancellationToken ct)
    {
        var err = new StringBuilder();
        using var p = Process.Start(Psi(ff, args))!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var errTask = Task.Run(async () => { string? l; while ((l = await p.StandardError.ReadLineAsync()) != null) lock (err) err.AppendLine(l); });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var us) && duration > 0)
                it.Progress = from + span * Math.Clamp(us / 1e6 / duration, 0, 1);
        await p.WaitForExitAsync();
        await errTask;
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) { string e; lock (err) e = err.ToString(); throw new InvalidOperationException("Couldn't make the video smaller: " + CleanError(e)); }
    }

    /// <summary>Downloads without sign-in unless this item needed it before; if the site asks for sign-in, tries once more with it.</summary>
    static async Task YtDlpDownloadAsync(DownloadItem it, CancellationToken ct)
    {
        bool signIn = it.UseSignIn && BrowserChosen;
        try { await YtDlpOnceAsync(it, signIn, ct); }
        catch (SignInNeededException e) when (!signIn && BrowserChosen && e.Problem == SignInProblem.SiteWantsSignIn && !ct.IsCancellationRequested)
        {
            it.UseSignIn = true;
            it.Progress = 0; it.Phase = "";
            await YtDlpOnceAsync(it, true, ct);
        }
    }

    static async Task YtDlpOnceAsync(DownloadItem it, bool signIn, CancellationToken ct)
    {
        var exe = YtDlp ?? throw new InvalidOperationException("The download engine isn't installed. Open Settings → Engine to install it.");
        Directory.CreateDirectory(string.IsNullOrWhiteSpace(it.Options.Folder) ? AppState.DefaultFolder() : it.Options.Folder);
        var psi = Psi(exe, BuildDownloadArgs(it, signIn));
        using var p = Process.Start(psi)!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = new StringBuilder();
        var errTask = Task.Run(async () =>
        {
            string? l;
            while ((l = await p.StandardError.ReadLineAsync()) != null) lock (err) { err.AppendLine(l); }
        });
        int stage = 0; long lastTotal = 0;
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
        {
            if (line.StartsWith("@@P "))
            {
                var parts = line[4..].Split(' ');
                long done = ParseL(parts, 0), total = ParseL(parts, 1), est = ParseL(parts, 2);
                double speed = ParseD(parts, 3), eta = ParseD(parts, 4);
                if (total <= 0) total = est;
                if (total > 0 && lastTotal > 0 && Math.Abs(total - lastTotal) > lastTotal / 4 && done < total / 20) stage++;
                if (total > 0) lastTotal = total;
                it.DoneBytes = done;
                if (total > 0) it.TotalBytes = total;
                it.Progress = total > 0 ? Math.Clamp((double)done / total, 0, 1) : 0;
                it.Speed = speed; it.Eta = eta > 0 ? eta : -1;
                it.Phase = stage == 0 ? "" : "audio";
            }
            else if (line.StartsWith("@@F "))
            {
                it.FilePath = line[4..].Trim();
            }
            else if (line.StartsWith("[Merger]") || line.StartsWith("[ExtractAudio]") || line.StartsWith("[VideoRemuxer]") || line.StartsWith("[EmbedSubtitle]"))
            {
                it.Phase = "processing"; it.Progress = 1;
            }
        }
        await p.WaitForExitAsync();
        await errTask;
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
        {
            string e; lock (err) e = err.ToString();
            throw Fail(e, signIn);
        }
        if (!string.IsNullOrEmpty(it.FilePath) && File.Exists(it.FilePath))
            it.TotalBytes = new FileInfo(it.FilePath).Length;
    }

    // ---------- GIF / WebP ----------

    /// <summary>Turns a video into an endlessly looping GIF or animated WebP next to it, with progress.</summary>
    public static async Task MakeAnimationAsync(DownloadItem it, string source, bool keepSource, CancellationToken ct)
    {
        var ff = Ffmpeg ?? throw new InvalidOperationException("Making GIF or WebP files needs the converter (ffmpeg). Open Settings → Download engine to add it.");
        var o = it.Options;
        bool webp = o.Format == "webp";
        var ext = webp ? ".webp" : ".gif";
        var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
        var name = Path.GetFileNameWithoutExtension(source);
        var target = Path.Combine(dir, name + ext);
        for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, $"{name} ({n}){ext}");
        var tmp = target + ".part";

        var scale = o.AnimationWidth > 0 ? $"scale='min({o.AnimationWidth},iw)':-2:flags=lanczos" : "scale=iw:-2:flags=lanczos";
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-an", "-progress", "pipe:1", "-nostats" };
        if (webp)
            args.AddRange(new[] { "-vf", $"fps=20,{scale}", "-c:v", "libwebp_anim", "-lossless", "0", "-quality", "75", "-compression_level", "4", "-loop", "0", "-f", "webp" });
        else
            // A colour palette made from this clip keeps GIFs sharp and small.
            args.AddRange(new[] { "-vf", $"fps=15,{scale},split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle", "-loop", "0", "-f", "gif" });
        args.Add(tmp);

        it.Phase = "converting"; it.Progress = 0; it.Speed = 0; it.Eta = -1;
        double duration = it.DurationSeconds;
        var err = new StringBuilder();
        using var p = Process.Start(Psi(ff, args))!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var errTask = Task.Run(async () =>
        {
            string? l;
            while ((l = await p.StandardError.ReadLineAsync()) != null)
            {
                lock (err) err.AppendLine(l);
                var m = System.Text.RegularExpressions.Regex.Match(l, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (m.Success && duration <= 0)
                    duration = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            }
        });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
        {
            if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var us) && duration > 0)
                it.Progress = Math.Clamp(us / 1e6 / duration, 0, 1);
        }
        await p.WaitForExitAsync();
        await errTask;
        if (ct.IsCancellationRequested || p.ExitCode != 0)
        {
            try { File.Delete(tmp); } catch { }
            ct.ThrowIfCancellationRequested();
            string e; lock (err) e = err.ToString();
            if (!e.Contains("Video:"))
                throw new InvalidOperationException("That file has no picture, so it can't be turned into a " + (webp ? "WebP." : "GIF."));
            throw new InvalidOperationException("Couldn't make the " + (webp ? "WebP" : "GIF") + ": " + CleanError(e));
        }
        File.Move(tmp, target, true);
        if (!keepSource) try { File.Delete(source); } catch { }
        it.FilePath = target;
        it.TotalBytes = new FileInfo(target).Length;
        it.Progress = 1; it.Phase = "";
    }

    static long ParseL(string[] p, int i) => i < p.Length && double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (long)v : 0;
    static double ParseD(string[] p, int i) => i < p.Length && double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static async Task DirectDownloadAsync(DownloadItem it, CancellationToken ct)
    {
        var folder = string.IsNullOrWhiteSpace(it.Options.Folder) ? AppState.DefaultFolder() : it.Options.Folder;
        Directory.CreateDirectory(folder);
        var name = SafeName(string.IsNullOrWhiteSpace(it.Title) ? "download" : it.Title);
        // "Trailer v1.2" has a dot but no real file type, so only trust known media endings.
        var ending = IsMediaExt(Path.GetExtension(name)) ? Path.GetExtension(name) : "";
        if (ending.Length == 0) try { ending = Path.GetExtension(new Uri(it.Url).AbsolutePath); } catch { }
        if (it.FileName.Length > 0) name = SafeName(it.FileName) + ending;
        else if (!IsMediaExt(Path.GetExtension(name))) name += ending;
        var target = string.IsNullOrEmpty(it.FilePath) ? Path.Combine(folder, name) : it.FilePath;
        if (Settings.SkipExisting && File.Exists(target) && !File.Exists(target + ".part"))
        {
            // Already downloaded earlier: use that file instead of fetching it again.
            it.FilePath = target;
            it.TotalBytes = new FileInfo(target).Length;
            return;
        }
        var part = target + ".part";
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
        if (existing > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        bool resumed = existing > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resumed) existing = 0;
        long total = (resp.Content.Headers.ContentLength ?? 0) + existing;
        it.TotalBytes = total;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, true))
        {
            var buf = new byte[1 << 16];
            long done = existing;
            var sw = Stopwatch.StartNew(); long winBytes = 0; double lastT = 0;
            long limit = ParseLimit(Settings.SpeedLimit);
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n; winBytes += n;
                var t = sw.Elapsed.TotalSeconds;
                if (t - lastT >= 0.5)
                {
                    it.Speed = winBytes / (t - lastT); winBytes = 0; lastT = t;
                    it.Eta = total > 0 && it.Speed > 0 ? (total - done) / it.Speed : -1;
                }
                it.DoneBytes = done;
                it.Progress = total > 0 ? (double)done / total : 0;
                if (limit > 0)
                {
                    var expected = (done - existing) / (double)limit;
                    if (expected > t) await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, expected - t)), ct);
                }
            }
        }
        if (File.Exists(target) && !Settings.SkipExisting) File.Delete(target);
        if (File.Exists(target)) // appeared while downloading: keep both
        {
            var b = Path.Combine(folder, Path.GetFileNameWithoutExtension(target));
            var ext = Path.GetExtension(target);
            int i = 2; while (File.Exists($"{b} ({i}){ext}")) i++;
            target = $"{b} ({i}){ext}";
        }
        File.Move(part, target);
        it.FilePath = target;
        it.TotalBytes = new FileInfo(target).Length;
    }

    static long ParseLimit(string s)
    {
        s = (s ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) return 0;
        double mul = 1;
        if (s.EndsWith("K")) { mul = 1024; s = s[..^1]; }
        else if (s.EndsWith("M")) { mul = 1024 * 1024; s = s[..^1]; }
        else if (s.EndsWith("G")) { mul = 1024 * 1024 * 1024; s = s[..^1]; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (long)(v * mul) : 0;
    }

    // ---------- engine install / update ----------

    const string YtDlpBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    const string FfmpegBase = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/";
    const string MacFfmpegBase = "https://ffmpeg.martin-riedl.de/redirect/latest/macos/";

    /// <summary>The ready-made yt-dlp program for this system.</summary>
    public static string EngineUrl =>
        Platform.IsWindows ? YtDlpBase + "yt-dlp.exe"
        : Platform.IsMac ? YtDlpBase + "yt-dlp_macos"
        : YtDlpBase + (Platform.IsArm ? "yt-dlp_linux_aarch64" : "yt-dlp_linux");

    /// <summary>Rough size of the one-time download, for messages.</summary>
    public static string InstallSize => Platform.IsMac ? "about 100 MB" : Platform.IsLinux ? "about 200 MB" : "about 140 MB";

    public static async Task InstallEngineAsync(IProgress<(string, double)> progress, bool withFfmpeg, CancellationToken ct)
    {
        var target = Path.Combine(ToolsDir, Platform.Exe("yt-dlp"));
        await FetchAsync(EngineUrl, target + ".part", "Download engine", progress, ct);
        File.Move(target + ".part", target, true);
        Platform.MakeExecutable(target);
        ForgetTools();
        if (withFfmpeg && !HasFfmpeg)
        {
            const string label = "Video converter (ffmpeg)";
            if (Platform.IsWindows)
            {
                var zip = Path.Combine(ToolsDir, "ffmpeg.zip");
                await FetchAsync(FfmpegBase + "ffmpeg-master-latest-win64-gpl.zip", zip, label, progress, ct);
                progress.Report(("Unpacking video converter…", 1));
                await Task.Run(() => ExtractFromZip(zip, "ffmpeg.exe", "ffprobe.exe"), ct);
                File.Delete(zip);
            }
            else if (Platform.IsMac)
            {
                var arch = Platform.IsArm ? "arm64" : "amd64";
                foreach (var tool in new[] { "ffmpeg", "ffprobe" })
                {
                    var zip = Path.Combine(ToolsDir, tool + ".zip");
                    await FetchAsync($"{MacFfmpegBase}{arch}/release/{tool}.zip", zip, tool == "ffmpeg" ? label : "Media reader (ffprobe)", progress, ct);
                    progress.Report(("Unpacking video converter…", 1));
                    await Task.Run(() => ExtractFromZip(zip, tool), ct);
                    File.Delete(zip);
                }
            }
            else
            {
                var archive = Path.Combine(ToolsDir, "ffmpeg.tar.xz");
                await FetchAsync(FfmpegBase + (Platform.IsArm ? "ffmpeg-master-latest-linuxarm64-gpl.tar.xz" : "ffmpeg-master-latest-linux64-gpl.tar.xz"), archive, label, progress, ct);
                progress.Report(("Unpacking video converter…", 1));
                await ExtractFromTarXzAsync(archive, ct, "ffmpeg", "ffprobe");
                File.Delete(archive);
            }
        }
        ForgetTools();
        progress.Report(("Ready", 1));
    }

    static void ExtractFromZip(string zip, params string[] names)
    {
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries)
        {
            if (!names.Contains(e.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(ToolsDir, e.Name.ToLowerInvariant());
            e.ExtractToFile(dest, true);
            Platform.MakeExecutable(dest);
        }
    }

    /// <summary>.NET can't read .xz, so Linux uses the system's tar (installed almost everywhere).</summary>
    static async Task ExtractFromTarXzAsync(string archive, CancellationToken ct, params string[] names)
    {
        var tmp = Path.Combine(ToolsDir, "unpack");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        try
        {
            var psi = new ProcessStartInfo("tar") { UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in new[] { "-xJf", archive, "-C", tmp }) psi.ArgumentList.Add(a);
            Process p;
            try { p = Process.Start(psi)!; }
            catch { throw new InvalidOperationException("Couldn't unpack the converter. Install ffmpeg with your package manager instead (for example: sudo apt install ffmpeg)."); }
            using (p)
            {
                var err = await p.StandardError.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("Couldn't unpack the converter (" + CleanError(err) + "). Install ffmpeg with your package manager instead (for example: sudo apt install ffmpeg).");
            }
            foreach (var f in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
            {
                var n = Path.GetFileName(f);
                if (!names.Contains(n)) continue;
                var dest = Path.Combine(ToolsDir, n);
                File.Move(f, dest, true);
                Platform.MakeExecutable(dest);
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static async Task FetchAsync(string url, string dest, string label, IProgress<(string, double)> progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[1 << 16]; long done = 0; int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            progress.Report(($"{label}: {Fmt.Bytes(done)}{(total > 0 ? " of " + Fmt.Bytes(total) : "")}", total > 0 ? (double)done / total : 0));
        }
    }

    public static async Task<string> UpdateEngineAsync(CancellationToken ct)
    {
        var (code, o, e) = await RunAsync(new[] { "-U" }, ct);
        var text = (o + "\n" + e).Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        return code == 0 ? (text.Length > 0 ? text : "Engine is up to date.") : CleanError(e);
    }

    /// <summary>True when yt-dlp is the copy ClipHarbor installed (not one from Homebrew, pip or a package manager).</summary>
    public static bool IsOwnEngine =>
        YtDlp is { } y && Path.GetFullPath(Path.GetDirectoryName(y) ?? "").TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(ToolsDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Quietly updates ClipHarbor's own yt-dlp. Returns the new version, or null when nothing changed.</summary>
    public static async Task<string?> UpdateEngineQuietlyAsync(CancellationToken ct)
    {
        if (!IsOwnEngine) return null;
        try
        {
            var (code, o, e) = await RunAsync(new[] { "-U" }, ct);
            if (code != 0) return null;
            var m = System.Text.RegularExpressions.Regex.Match(o + "\n" + e, @"Updated yt-dlp to (\S+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    public static async Task<string> EngineVersionAsync()
    {
        if (!HasEngine) return "";
        try { var (_, o, _) = await RunAsync(new[] { "--version" }, CancellationToken.None); return o.Trim(); }
        catch { return ""; }
    }
}

/// <summary>The site wants the person to be signed in; signing in through a browser in Settings may fix it.</summary>
public class SignInNeededException(string message, SignInProblem problem) : InvalidOperationException(message)
{
    public SignInProblem Problem { get; } = problem;
}

public enum SignInProblem { None, SiteWantsSignIn, BrowserUnreadable }
