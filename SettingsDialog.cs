using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClipHarbor;

public class SettingsDialog : Dialog
{
    readonly AppSettings _s;
    readonly ComboBox _theme = new() { Width = 160, ItemsSource = new[] { "Light", "Dark", "System" } };
    readonly NumericUpDown _parallel = new() { Minimum = 1, Maximum = 32, Width = 120, FormatString = "0", Increment = 1 };
    readonly TextBox _template = new() { Width = 300 };
    readonly CheckBox _skip = new() { Content = "Skip files that are already downloaded" };
    readonly CheckBox _notify = new() { Content = "Show a notice when downloads finish" };
    readonly NumericUpDown _limit = new() { Minimum = 0, Maximum = 100000, Width = 140, FormatString = "0.#", Increment = 1 };
    readonly ComboBox _limitUnit = new() { Width = 90, ItemsSource = new[] { "MB/s", "KB/s" }, SelectedIndex = 0 };
    readonly TextBox _proxy = new() { Width = 300, Watermark = "e.g. http://127.0.0.1:8080" };
    readonly ComboBox _cookies = new() { Width = 160 };
    readonly CheckBox _thumb = new() { Content = "Use the video's thumbnail as the file's cover picture" };
    readonly CheckBox _meta = new() { Content = "Save the title, channel and upload date inside the file" };
    readonly NumericUpDown _subHours = new() { Minimum = 0, Maximum = 168, Width = 120, FormatString = "0", Increment = 1 };
    readonly CheckBox _background = new() { Content = $"Keep running in the {(Platform.IsMac ? "menu bar" : "system tray")} when I close the window" };
    readonly CheckBox _login = new() { Content = "Start ClipHarbor when I sign in to this computer (hidden, in the " + (Platform.IsMac ? "menu bar)" : "tray)") };
    readonly TextBlock _engineStatus = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 680 };
    readonly ProgressBar _engineBar = new() { Classes = { "thin" }, Minimum = 0, Maximum = 1, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
    readonly TextBox _enginePath = new() { Width = 450, Watermark = "Found automatically" };
    readonly TextBox _ffPath = new() { Width = 450, Watermark = "Found automatically" };
    readonly Button _install = Ui.Button("Install engine", null, "accent");
    readonly Button _update = Ui.Button("Update engine");
    readonly CheckBox _autoUpdate = new() { Content = "Check for new versions of ClipHarbor automatically" };
    readonly CheckBox _autoInstall = new() { Content = "Install them automatically" };
    readonly CheckBox _autoEngine =new() { Content = "Keep the download engine up to date automatically" };
    readonly Button _checkApp = Ui.Button("Check now");
    readonly TextBlock _appStatus = new() { Classes = { "sub" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };

    /// <summary>A newer ClipHarbor found with "Check now"; the main window offers it after Settings closes.</summary>
    public UpdateInfo? FoundUpdate { get; private set; }
    CancellationTokenSource? _cts;

    // Only browsers that exist on this system. On Windows, Chrome-based browsers lock their sign-ins away
    // from other programs, so Firefox comes first and the rest are marked.
    static readonly (string key, string name)[] BrowserList = Platform.IsWindows
        ? new[] { ("", "Don't use"), ("firefox", "Firefox (recommended)"), ("chrome", "Chrome (may not work)"), ("edge", "Edge (may not work)"), ("brave", "Brave (may not work)"), ("opera", "Opera (may not work)"), ("vivaldi", "Vivaldi (may not work)"), ("chromium", "Chromium (may not work)") }
        : Platform.IsMac
        ? new[] { ("", "Don't use"), ("safari", "Safari"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("edge", "Edge"), ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"), ("chromium", "Chromium") }
        : new[] { ("", "Don't use"), ("firefox", "Firefox"), ("chrome", "Chrome"), ("chromium", "Chromium"), ("brave", "Brave"), ("edge", "Edge"), ("opera", "Opera"), ("vivaldi", "Vivaldi") };
    static readonly string[] Browsers = BrowserList.Select(b => b.key).ToArray();
    const double C = 230;

    public SettingsDialog(AppSettings s) : base("Settings")
    {
        _s = s;
        var root = new StackPanel { Margin = new Thickness(24, 10, 24, 20), Width = 700 };
        void Section(string t) => root.Children.Add(new TextBlock { Text = t, Classes = { "title" }, Margin = new Thickness(0, 16, 0, 10) });
        void Row(string label, Control c, string? hint = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{C},Auto,*"), Margin = new Thickness(0, 0, 0, 10) };
            var l = Ui.Text(label);
            Grid.SetColumn(c, 1);
            g.Children.AddRange(new Control[] { l, c });
            if (hint != null) { var h = Ui.Text(hint, "sub"); h.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(h, 2); g.Children.Add(h); }
            root.Children.Add(g);
        }
        void Check(CheckBox c) { c.Margin = new Thickness(C, 0, 0, 4); root.Children.Add(c); }

        Section("General");
        _theme.SelectedItem = s.Theme;
        if (_theme.SelectedIndex < 0) _theme.SelectedIndex = 0;
        // Show the look straight away; Cancel (or closing) goes back to the saved one.
        _theme.SelectionChanged += (_, _) => App.ApplyTheme(_theme.SelectedItem?.ToString() ?? "Light");
        Closed += (_, _) => App.ApplyTheme(_s.Theme);
        Row("Look", _theme);
        _parallel.Value = Math.Clamp(s.MaxConcurrent, 1, 32);
        Row("Downloads at the same time", _parallel, "no cap");
        _template.Text = s.FileNameTemplate;
        Row("File names", _template);
        root.Children.Add(new TextBlock { Text = "Codes: %(title)s  %(uploader)s  %(upload_date)s  %(ext)s", Classes = { "sub", "small" }, Margin = new Thickness(C, -6, 0, 10) });
        _skip.IsChecked = s.SkipExisting; Check(_skip);
        _notify.IsChecked = s.NotifyWhenDone; Check(_notify);

        Section("Downloading");
        var (amount, kb) = ReadLimit(s.SpeedLimit);
        _limitUnit.SelectedIndex = kb ? 1 : 0;
        _limit.Increment = kb ? 100 : 1;
        _limit.Value = amount;
        _limitUnit.SelectionChanged += (_, _) => _limit.Increment = _limitUnit.SelectedIndex == 1 ? 100 : 1;
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _limit, _limitUnit } };
        Row("Speed limit", limitRow, "for each download · 0 = no limit");
        _proxy.Text = s.Proxy;
        Row("Proxy", _proxy);
        _cookies.Width = Platform.IsWindows ? 220 : 160;
        _cookies.ItemsSource = BrowserList.Select(b => b.name).ToList();
        _cookies.SelectedIndex = Math.Max(0, Array.IndexOf(Browsers, s.CookiesBrowser ?? ""));
        Row("Use sign-in from browser", _cookies, "for private or age-restricted videos");
        _thumb.IsChecked = s.EmbedThumbnail; Check(_thumb);
        _meta.IsChecked = s.EmbedMetadata; Check(_meta);
        ToolTip.SetTip(_thumb, "Music players, file managers and phones show it as the file's picture.\nWorks for MP4, MKV, MP3, M4A, FLAC and OPUS.");
        ToolTip.SetTip(_meta, "Music and video players, and the file's Properties/Get Info, show these details.\nThe channel is saved as the artist.");

        Section("Subscriptions");
        _subHours.Value = Math.Clamp(s.SubscriptionCheckHours, 0, 168);
        Row("Check for new videos every", _subHours, "hours (0 = only when I ask)");
        _background.IsChecked = s.RunInBackground; Check(_background);
        _login.IsChecked = s.StartAtLogin; Check(_login);
        root.Children.Add(new TextBlock { Text = "Checks only happen while ClipHarbor is running, so keep it in the tray to get new videos on time.", Classes = { "sub", "small" }, Margin = new Thickness(C, 0, 0, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left });

        Section("Download engine");
        root.Children.Add(_engineStatus);
        _engineBar.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_engineBar);
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 10), Children = { _install, _update } });
        _autoEngine.IsChecked = s.UpdateEngineAutomatically;
        _autoEngine.Margin = new Thickness(0, 0, 0, 16);
        ToolTip.SetTip(_autoEngine, "Video sites change often, and an old engine stops working on them.\nChecks when ClipHarbor starts and once a day, while nothing is downloading.");
        root.Children.Add(_autoEngine);
        _enginePath.Text = s.EnginePath; Row($"Engine file ({Platform.Exe("yt-dlp")})", _enginePath);
        _ffPath.Text = s.FfmpegPath; Row($"Converter file ({Platform.Exe("ffmpeg")})", _ffPath);
        _install.Click += async (_, _) => await Install();
        _update.Click += async (_, _) => await UpdateEngine();

        Section("About ClipHarbor");
        _appStatus.Text = $"Version {Updater.CurrentText}";
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 0, 0, 10), Children = { _checkApp, _appStatus } });
        _autoUpdate.IsChecked = s.CheckAppUpdates;
        root.Children.Add(_autoUpdate);
        _autoInstall.IsChecked = s.InstallAppUpdates;
        _autoInstall.IsEnabled = s.CheckAppUpdates;
        _autoInstall.Margin = new Thickness(28, 4, 0, 0);
        _autoUpdate.IsCheckedChanged += (_, _) => _autoInstall.IsEnabled = _autoUpdate.IsChecked == true;
        ToolTip.SetTip(_autoInstall, "New versions download in the background and start the next time ClipHarbor starts.\nWhile it's hidden in the tray with nothing to do, it restarts into the new version by itself.");
        root.Children.Add(_autoInstall);
        _checkApp.Click += async (_, _) =>
        {
            _checkApp.IsEnabled = false;
            _appStatus.Text = "Checking…";
            try
            {
                FoundUpdate = await Updater.CheckAsync(CancellationToken.None);
                _appStatus.Text = FoundUpdate == null
                    ? $"Version {Updater.CurrentText}. You have the latest version."
                    : $"Version {FoundUpdate.Version.ToString(3)} is available (you have {Updater.CurrentText}). Close Settings and press Update now.";
            }
            catch (Exception ex) { _appStatus.Text = "Couldn't check: " + ex.Message; }
            _checkApp.IsEnabled = true;
        };

        var ok = Ui.Button("Save", null, "accent"); ok.MinWidth = 120; ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        var cancel = Ui.Button("Cancel"); cancel.MinWidth = 100; cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => { Save(); Close(true); };
        cancel.Click += (_, _) => Close(false);

        var footer = new Border { Classes = { "bar" }, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12), Child = Buttons(cancel, ok) };
        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = root, MaxHeight = 640 });
        Content = dock;

        RefreshEngine();
        Closed += (_, _) => _cts?.Cancel();
    }

    async void RefreshEngine()
    {
        var y = Engine.YtDlp; var f = Engine.Ffmpeg;
        _engineStatus.Classes.Set("warn", y == null);
        _engineStatus.Classes.Set("bad", false);
        if (y == null)
        {
            _engineStatus.Text = $"The download engine isn't installed yet. It's free and open source (yt-dlp, with ffmpeg for high quality and audio). Press Install to get it — {Engine.InstallSize}.";
            _install.Content = "Install engine"; _update.IsEnabled = false;
            return;
        }
        _update.IsEnabled = true;
        _install.Content = f == null ? "Add converter" : "Reinstall";
        _engineStatus.Text = "Engine ready" + (f == null ? " — converter (ffmpeg) missing: 1080p+ and MP3 need it." : " with converter.");
        var v = await Engine.EngineVersionAsync();
        if (v.Length > 0) _engineStatus.Text += $"  Version {v}.";
    }

    async Task Install()
    {
        _cts = new CancellationTokenSource();
        _install.IsEnabled = _update.IsEnabled = false;
        _engineBar.IsVisible = true;
        _engineStatus.Classes.Set("warn", false);
        var prog = new Progress<(string text, double frac)>(p => { _engineStatus.Text = p.text; _engineBar.Value = Math.Clamp(p.frac, 0, 1); });
        bool failed = false;
        try { await Engine.InstallEngineAsync(prog, true, _cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { _engineStatus.Text = "Couldn't install: " + ex.Message; _engineStatus.Classes.Set("bad", true); failed = true; }
        _engineBar.IsVisible = false;
        _install.IsEnabled = true;
        if (!failed) RefreshEngine();
    }

    async Task UpdateEngine()
    {
        _update.IsEnabled = false;
        _engineStatus.Text = "Checking for a newer engine…";
        try { _engineStatus.Text = await Engine.UpdateEngineAsync(CancellationToken.None); }
        catch (Exception ex) { _engineStatus.Text = ex.Message; }
        _update.IsEnabled = true;
    }

    /// <summary>Reads a saved limit like "500K" or "2M" (older versions let any of these be typed) as an amount and unit.</summary>
    static (decimal amount, bool kb) ReadLimit(string? text)
    {
        var t = (text ?? "").Trim().ToUpperInvariant().Replace("/S", "").Replace("B", "").Replace(" ", "");
        if (t.Length == 0) return (0, false);
        char unit = char.IsLetter(t[^1]) ? t[^1] : ' ';
        if (unit != ' ') t = t[..^1];
        if (!decimal.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0) return (0, false);
        return unit switch
        {
            'G' => (v * 1024, false),
            'M' => (v, false),
            'K' => (v, true),
            _ => (Math.Max(1, Math.Round(v / 1024)), true), // plain bytes
        };
    }

    void Save()
    {
        _s.Theme = _theme.SelectedItem?.ToString() ?? "Light";
        _s.MaxConcurrent = (int)(_parallel.Value ?? 3);
        _s.FileNameTemplate = string.IsNullOrWhiteSpace(_template.Text) ? "%(title)s.%(ext)s" : _template.Text.Trim();
        _s.SkipExisting = _skip.IsChecked == true;
        _s.NotifyWhenDone = _notify.IsChecked == true;
        var amount = Math.Round(_limit.Value ?? 0, 1);
        _s.SpeedLimit = amount <= 0 ? "" : amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + (_limitUnit.SelectedIndex == 1 ? "K" : "M");
        _s.Proxy = (_proxy.Text ?? "").Trim();
        _s.CookiesBrowser = Browsers[Math.Max(0, _cookies.SelectedIndex)];
        _s.EmbedThumbnail = _thumb.IsChecked == true;
        _s.EmbedMetadata = _meta.IsChecked == true;
        _s.SubscriptionCheckHours = (int)(_subHours.Value ?? 0);
        _s.RunInBackground = _background.IsChecked == true;
        _s.StartAtLogin = _login.IsChecked == true;
        _s.BackgroundAsked = true;
        _s.CheckAppUpdates = _autoUpdate.IsChecked == true;
        _s.UpdateEngineAutomatically = _autoEngine.IsChecked == true;
        _s.InstallAppUpdates = _autoInstall.IsChecked == true;
        _s.EnginePath = (_enginePath.Text ?? "").Trim();
        _s.FfmpegPath = (_ffPath.Text ?? "").Trim();
    }
}
