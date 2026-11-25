using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipHarbor;

/// <summary>A newer release found on GitHub.</summary>
public record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes, string AssetUrl, long Size, string? Sha256);

/// <summary>Finds newer ClipHarbor releases on GitHub and installs them in place.</summary>
public static class Updater
{
    /// <summary>"owner/name" of the GitHub repository. The build fills it in; this is the fallback.</summary>
    public static string Repo =>
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateRepo")?.Value
        is { Length: > 0 } r ? r : "nolsen42/ClipHarbor";

    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));
    public static string CurrentText => Current.ToString(3);

    public const string AfterUpdateFlag = "--after-update";

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>The release file made for this computer.</summary>
    static string AssetName =>
        Platform.IsWindows ? "ClipHarbor-windows-x64.exe"
        : Platform.IsMac ? (Platform.IsArm ? "ClipHarbor-macos-apple-silicon.zip" : "ClipHarbor-macos-intel.zip")
        : (Platform.IsArm ? "ClipHarbor-linux-arm64.tar.gz" : "ClipHarbor-linux-x64.tar.gz");

    /// <summary>Asks GitHub for the latest release. Returns it if it's newer than this copy, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Engine.Http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        v = Normalize(v);
        if (v <= Current) return null;

        string url = "", sha = ""; long size = 0;
        if (r.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                if (a.GetProperty("name").GetString() != AssetName) continue;
                url = a.GetProperty("browser_download_url").GetString() ?? "";
                size = a.GetProperty("size").GetInt64();
                if (a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:")) sha = ds[7..];
            }
        var notes = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : $"https://github.com/{Repo}/releases/latest";
        return new UpdateInfo(v, tag, page, notes, url, size, sha.Length > 0 ? sha : null);
    }

    /// <summary>What gets replaced: the .app bundle on a Mac, the program file elsewhere.</summary>
    public static string InstallTarget(string exePath)
    {
        if (Platform.IsMac)
        {
            var macOs = Path.GetDirectoryName(exePath);
            var contents = macOs == null ? null : Path.GetDirectoryName(macOs);
            var bundle = contents == null ? null : Path.GetDirectoryName(contents);
            if (bundle != null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return bundle;
        }
        return exePath;
    }

    /// <summary>Whether this copy can replace itself (it can't from a read-only place such as Program Files).</summary>
    public static bool CanInstall(UpdateInfo u, string exePath)
    {
        if (u.AssetUrl.Length == 0) return false;
        if (Platform.IsMac && !InstallTarget(exePath).EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var dir = Path.GetDirectoryName(InstallTarget(exePath))!;
            var probe = Path.Combine(dir, ".clipharbor-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Downloads the update, checks it, and puts it in place of <paramref name="exePath"/>.
    /// Returns the program to start. The old copy is kept next to it as ".old" until the new one runs.</summary>
    public static async Task<string> InstallAsync(UpdateInfo u, string exePath, IProgress<(string, double)> progress, CancellationToken ct)
    {
        var target = InstallTarget(exePath);
        var dir = Path.GetDirectoryName(target)!;
        // Work next to the app, so moving files never has to cross drives.
        var work = Path.Combine(dir, ".clipharbor-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        try
        {
            var file = Path.Combine(work, Path.GetFileName(new Uri(u.AssetUrl).AbsolutePath));
            await DownloadCheckedAsync(u, file, progress, ct);
            progress.Report(("Installing…", 1));

            if (Platform.IsWindows)
            {
                Swap(target, () => File.Move(file, target));
                return target;
            }
            if (Platform.IsMac)
            {
                await RunAsync("ditto", ct, "-x", "-k", file, work);
                var app = Directory.EnumerateDirectories(work, "*.app").FirstOrDefault() ?? throw new InvalidOperationException("The update didn't contain the app.");
                Swap(target, () => Directory.Move(app, target));
                return Path.Combine(target, "Contents", "MacOS", Path.GetFileName(exePath));
            }
            await RunAsync("tar", ct, "-xzf", file, "-C", work);
            var bin = Directory.EnumerateFiles(work, "ClipHarbor", SearchOption.AllDirectories).FirstOrDefault() ?? throw new InvalidOperationException("The update didn't contain the app.");
            Swap(target, () => File.Move(bin, target));
            Platform.MakeExecutable(target);
            return target;
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>Moves the current copy aside, puts the new one in, and puts the old one back if that fails.
    /// (A running program can be renamed, but not overwritten, on Windows.)</summary>
    static void Swap(string target, Action putNew)
    {
        var old = target + ".old";
        DeleteOld(target);
        if (Directory.Exists(target)) Directory.Move(target, old); else File.Move(target, old);
        try { putNew(); }
        catch
        {
            try { if (Directory.Exists(old)) Directory.Move(old, target); else File.Move(old, target, true); } catch { }
            throw;
        }
    }

    /// <summary>Removes the previous version left behind by an update.</summary>
    public static void DeleteOld(string target)
    {
        var old = target + ".old";
        try { if (Directory.Exists(old)) Directory.Delete(old, true); else if (File.Exists(old)) File.Delete(old); } catch { }
    }

    static async Task DownloadCheckedAsync(UpdateInfo u, string dest, IProgress<(string, double)> progress, CancellationToken ct)
    {
        using var resp = await Engine.Http.GetAsync(u.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? u.Size;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(dest))
        {
            var buf = new byte[1 << 16]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                hash.AppendData(buf, 0, n);
                done += n;
                progress.Report(($"Downloading update: {Fmt.Bytes(done)}{(total > 0 ? " of " + Fmt.Bytes(total) : "")}", total > 0 ? (double)done / total : 0));
            }
        }
        var got = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        bool sizeOk = u.Size <= 0 || new FileInfo(dest).Length == u.Size;
        bool hashOk = u.Sha256 == null || string.Equals(got, u.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!sizeOk || !hashOk)
            throw new InvalidOperationException("The downloaded update was damaged (its checksum didn't match), so it wasn't installed. Please try again.");
    }

    static async Task RunAsync(string exe, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException($"Couldn't unpack the update ({exe}): {err.Trim()}");
    }

    /// <summary>Starts the new version; it waits for this copy to close before taking over.</summary>
    public static void Launch(string program, bool hidden = false)
    {
        var args = new List<string> { AfterUpdateFlag, Environment.ProcessId.ToString() };
        if (hidden) args.Add(StartAtLogin.Flag); // stay in the tray, like the copy it replaces
        if (Platform.IsMac && InstallTarget(program) is var bundle && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            foreach (var a in new[] { "-n", bundle, "--args" }.Concat(args)) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
            return;
        }
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }

    /// <summary>This copy was just started by an update.</summary>
    public static bool JustUpdated { get; private set; }

    /// <summary>The release page of the running version, for "What's new".</summary>
    public static string CurrentReleasePage => $"https://github.com/{Repo}/releases/tag/v{CurrentText}";

    /// <summary>Called early by the new version: wait for the old one to exit, then tidy up its leftovers.</summary>
    public static void FinishUpdate(int oldPid)
    {
        JustUpdated = true;
        try { using var old = Process.GetProcessById(oldPid); old.WaitForExit(20000); } catch { }
        if (Environment.ProcessPath is { } p) DeleteOld(InstallTarget(p));
    }
}
