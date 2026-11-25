using Android.Content;
using Com.Yausername.Ffmpeg;
using Com.Yausername.Youtubedl_android;

namespace ClipHarbor;

/// <summary>Runs yt-dlp on Android through youtubedl-android, which brings its own Python and ffmpeg.
/// Android doesn't let apps start programs they download, so this replaces the desktop's yt-dlp.exe.</summary>
public sealed class YtDlpRunner : IYtDlpRunner
{
    readonly Context _context;
    readonly Task _ready;

    public YtDlpRunner(Context context)
    {
        _context = context.ApplicationContext!;
        // Unpacking Python and ffmpeg takes a few seconds the first time; downloads wait for it.
        _ready = Task.Run(() =>
        {
            UseAppClassLoader();
            YoutubeDL.Instance.Init(_context);
            FFmpeg.Instance.Init(_context);
        });
        var native = _context.ApplicationInfo?.NativeLibraryDir ?? "";
        var ff = Path.Combine(native, "libffmpeg.so");
        if (File.Exists(ff))
        {
            FfmpegPath = ff;
            // ffmpeg's own libraries and some of Python's (both unpacked by Init) have to be findable.
            var packages = Path.Combine(_context.NoBackupFilesDir!.AbsolutePath, "youtubedl-android", "packages");
            FfmpegEnvironment = new Dictionary<string, string>
            {
                ["LD_LIBRARY_PATH"] = Path.Combine(packages, "ffmpeg", "usr", "lib") + ":" + Path.Combine(packages, "python", "usr", "lib"),
            };
        }
    }

    public string? FfmpegPath { get; }
    public IReadOnlyDictionary<string, string> FfmpegEnvironment { get; } = new Dictionary<string, string>();

    /// <summary>Threads started from .NET have no Java context class loader, and the library's commons-io needs one.</summary>
    void UseAppClassLoader() => Java.Lang.Thread.CurrentThread()!.ContextClassLoader = _context.ClassLoader;

    public async Task<(int code, string stdout, string stderr)> RunAsync(IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        await _ready;
        return await Task.Run(() =>
        {
            UseAppClassLoader();
            // The link is the last argument (after "--"); the library adds yt-dlp, Python and --ffmpeg-location itself.
            var req = new YoutubeDLRequest(new List<string>());
            req.AddCommands(args.ToList());
            var id = Guid.NewGuid().ToString("N");
            using var reg = ct.Register(() => { try { YoutubeDL.Instance.DestroyProcessById(id); } catch { } });
            try
            {
                var res = YoutubeDL.Instance.Execute(req, id, onLine == null ? null : new LineCallback(onLine));
                return (res.ExitCode, res.Out ?? "", res.Err ?? "");
            }
            catch (YoutubeDL.CanceledException) { return (-1, "", "Canceled"); }
            catch (YoutubeDLException ex) { return (1, "", ex.Message ?? "yt-dlp failed"); }
        }, CancellationToken.None);
    }

    /// <summary>Keeps yt-dlp current (sites change often). Returns the new version, or null if nothing changed.</summary>
    public async Task<string?> UpdateAsync()
    {
        await _ready;
        return await Task.Run(() =>
        {
            UseAppClassLoader();
            try
            {
                var st = YoutubeDL.Instance.UpdateYoutubeDL(_context, YoutubeDL.UpdateChannel.STABLE.Instance);
                return st == YoutubeDL.UpdateStatus.Done ? YoutubeDL.Instance.Version(_context) : null;
            }
            catch { return null; }
        });
    }

    /// <summary>yt-dlp's callback: (percent, seconds left, the line it printed). youtubedl-android only calls it
    /// for lines shaped like "[download]  45.3% ... ETA 01:23", which is why Engine shapes its progress line that way.</summary>
    sealed class LineCallback(Action<string> onLine) : Java.Lang.Object, Kotlin.Jvm.Functions.IFunction3
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? p0, Java.Lang.Object? p1, Java.Lang.Object? p2)
        {
            if (p2?.ToString() is { Length: > 0 } line) onLine(line);
            return null;
        }
    }
}
