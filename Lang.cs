using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;

namespace ClipHarbor;

/// <summary>
/// Translations. The app is written in English; assets/lang/&lt;code&gt;.json maps each English text to the
/// translation. Texts with changing parts use {0}, {1}… in both ("Version {0} is available").
///
/// Instead of every window looking up its texts, every text the app puts on screen passes through here
/// (<see cref="Install"/> watches TextBlock, which buttons, check boxes, menus, lists and tooltips all
/// draw their text with). Anything without a translation simply stays English.
/// </summary>
public static class Lang
{
    /// <summary>The languages offered: code ("" = the computer's language) and name in that language.</summary>
    public static readonly (string code, string name)[] Choices =
    {
        ("", "System language"),
        ("en", "English"),
        ("es", "Español"),
        ("pt", "Português (Brasil)"),
        ("ru", "Русский"),
        ("id", "Bahasa Indonesia"),
        ("tr", "Türkçe"),
    };

    static Dictionary<string, string> _exact = new();
    static List<(Regex match, string[] parts, string to)> _patterns = new();
    static readonly Dictionary<string, string?> _cache = new();
    static readonly object _lock = new();

    /// <summary>The language in use ("en" when nothing else applies).</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>Picks the language: the one chosen in Settings, else the computer's, if ClipHarbor has it.</summary>
    public static void Load(string? chosen)
    {
        var code = string.IsNullOrEmpty(chosen) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : chosen;
        // CLIPHARBOR_LANG lets testers see a language without changing settings.
        if (Environment.GetEnvironmentVariable("CLIPHARBOR_LANG") is { Length: > 0 } test) code = test;
        Current = "en";
        _exact = new(); _patterns = new(); lock (_lock) _cache.Clear();
        if (code == "en") return;
        using var s = typeof(Lang).Assembly.GetManifestResourceStream($"ClipHarbor.lang.{code}.json");
        if (s == null) return;
        try
        {
            // Read without reflection, which trimmed (Android) builds may not have.
            using var doc = JsonDocument.Parse(s);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var en = p.Name;
                var tr = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (string.IsNullOrEmpty(tr)) continue;
                if (en.Contains("{0}")) _patterns.Add(Compile(en, tr));
                else _exact[en] = tr;
            }
            // Longest first, so "Downloading {0} of {1}" wins over "Downloading {0}".
            _patterns.Sort((a, b) => b.parts.Sum(p => p.Length).CompareTo(a.parts.Sum(p => p.Length)));
            Current = code;
        }
        catch { }
    }

    static (Regex, string[], string) Compile(string en, string tr)
    {
        var parts = Regex.Split(en, @"\{\d\}");
        var rx = new StringBuilder("^");
        var slots = Regex.Matches(en, @"\{(\d)\}");
        for (int i = 0; i < parts.Length; i++)
        {
            rx.Append(Regex.Escape(parts[i]));
            if (i < slots.Count) rx.Append($"(?<p{slots[i].Groups[1].Value}>.*?)");
        }
        rx.Append('$');
        return (new Regex(rx.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), parts.Where(p => p.Length > 0).ToArray(), tr);
    }

    /// <summary>The translation of an English text, or the text itself when there is none.</summary>
    public static string T(string? en)
    {
        if (string.IsNullOrEmpty(en) || Current == "en") return en ?? "";
        return Find(en) ?? en;
    }

    /// <summary>The translation in capitals, with the language's own rules (Turkish i → İ).</summary>
    public static string Upper(string en)
    {
        var t = T(en);
        try { return t.ToUpper(new CultureInfo(Current)); } catch { return t.ToUpperInvariant(); }
    }

    /// <summary>Like string.Format with a translated pattern: T("Version {0} is available", v).</summary>
    public static string T(string en, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(en), args);

    /// <summary>string.Join with each part translated, for texts built from pieces ("45%  ·  2 MB/s  ·  0:20 left").</summary>
    public static string Join(string separator, IEnumerable<string> parts) => string.Join(separator, parts.Select(p => T(p)));

    static string? Find(string en)
    {
        if (_exact.TryGetValue(en, out var t)) return t;
        lock (_lock) if (_cache.TryGetValue(en, out var c)) return c;
        string? found = null;
        // Only texts that hold every fixed part of a pattern are tried against it.
        foreach (var (rx, parts, to) in _patterns)
        {
            bool maybe = true;
            foreach (var p in parts) if (!en.Contains(p, StringComparison.Ordinal)) { maybe = false; break; }
            if (!maybe) continue;
            var m = rx.Match(en);
            if (!m.Success) continue;
            found = Regex.Replace(to, @"\{(\d)\}", s => m.Groups["p" + s.Groups[1].Value] is { Success: true } g ? T(g.Value) : s.Value);
            break;
        }
        lock (_lock)
        {
            if (_cache.Count > 4000) _cache.Clear(); // progress texts change all the time
            _cache[en] = found;
        }
        return found;
    }

    static readonly HashSet<string> _keep = new();

    /// <summary>Marks a text that must stay as it is on screen (a video title or channel name), and returns it.</summary>
    public static string Keep(string s)
    {
        if (Current != "en" && s.Length > 0) lock (_keep) _keep.Add(s);
        return s;
    }

    static bool Kept(string s) { lock (_keep) return _keep.Contains(s); }

    [ThreadStatic] static bool _busy;

    /// <summary>Translates every text as it's put on screen. Call once at start, after <see cref="Load"/>.</summary>
    public static void Install()
    {
        if (Current == "en") return;
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
        {
            if (_busy || e.NewValue is not string s || s.Length == 0 || Kept(s)) return;
            var t = T(s);
            if (ReferenceEquals(t, s) || t == s) return;
            _busy = true;
            try { tb.SetCurrentValue(TextBlock.TextProperty, t); }
            finally { _busy = false; }
        });
        Window.TitleProperty.Changed.AddClassHandler<Window>((w, e) =>
        {
            if (_busy || e.NewValue is not string s) return;
            var t = T(s);
            if (t == s) return;
            _busy = true;
            try { w.SetCurrentValue(Window.TitleProperty, t); }
            finally { _busy = false; }
        });
        NativeMenuItem.HeaderProperty.Changed.AddClassHandler<NativeMenuItem>((m, e) =>
        {
            if (_busy || e.NewValue is not string s) return;
            var t = T(s);
            if (t == s) return;
            _busy = true;
            try { m.SetCurrentValue(NativeMenuItem.HeaderProperty, t); }
            finally { _busy = false; }
        });
    }
}
