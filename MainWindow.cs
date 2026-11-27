using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClipHarbor;

public class MainWindow : Window
{
    public static readonly WindowIcon AppIcon = new(AssetLoader.Open(new Uri("avares://ClipHarbor/assets/ClipHarbor.ico")));

    readonly AppState _state;
    readonly string? _shot;
    readonly Dictionary<string, CancellationTokenSource> _running = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer _subTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    readonly NavBar _nav = new();
    readonly DownloadList _list = new();
    readonly SubscriptionsView _subs;
    readonly Border _banner = new() { Classes = { "banner" }, IsVisible = false, Padding = new Thickness(20, 10, 14, 10) };
    readonly TextBlock _bannerText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _bannerBtn = Ui.Button("", null, "accent");
    readonly Button _bannerClose = Ui.IconButton(Icons.Close, "Close");
    readonly TextBlock _status = new() { Classes = { "sub" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button _paste = Ui.Button("Paste Link", Icons.Link, "accent");
    readonly ToggleButton _smart = new() { Classes = { "flat" }, Content = Ui.Content("Smart Mode", Icons.Bolt) };
    readonly Button _settings = Ui.IconButton(Icons.Sliders, "Settings");
    readonly TextBox _search = new() { Watermark = "Search downloads", Width = 240, VerticalAlignment = VerticalAlignment.Center };
    CancellationTokenSource? _analyzeCts;
    Action? _bannerAction;
    bool _dirty, _quitting, _workStarted;
    readonly Action _shutdown;
    readonly List<string> _startLinks;

    public MainWindow(AppState state, string? screenshotPath, List<string> links, Action shutdown)
    {
        _state = state;
        _shot = screenshotPath;
        _shutdown = shutdown;
        _startLinks = links;
        Engine.Settings = state.Settings;

        Title = "ClipHarbor — video & audio downloader";
        Icon = AppIcon;
        Width = 1200; Height = 760;
        MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DragDrop.SetAllowDrop(this, true);
        if (_shot != null) ShowActivated = false;

        _subs = new SubscriptionsView(state, this) { IsVisible = false };

        _list.Source = state.Items;
        _list.EmptyTitle = "Copy a video link, then press Paste Link";
        _list.EmptyHint = DefaultHint;
        _list.ActionClicked += OnRowAction;
        _list.Opened += it => { if (it.Status == ItemStatus.Done) OpenFile(it); };
        _list.MenuRequested += () => BuildMenu().Open(_list.List);
        _list.List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete || (Platform.IsMac && e.Key == Key.Back)) { _ = Remove(_list.Selected, false); e.Handled = true; }
            else if (e.Key == Key.Enter) { var one = _list.Selected.FirstOrDefault(); if (one?.Status == ItemStatus.Done) OpenFile(one); }
        };

        Content = BuildLayout();

        _tick.Tick += (_, _) => Tick();
        _subTimer.Tick += (_, _) => _ = _subs.AutoCheck();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.Text) ? DragDropEffects.Copy : DragDropEffects.None; });
        AddHandler(DragDrop.DropEvent, (_, e) => { var t = e.DataTransfer.TryGetText(); if (!string.IsNullOrWhiteSpace(t)) _ = AddLinks(t); });
        Closing += OnClosing;

        Navigate("all");
        Changed();
        CheckEngineBanner();
        _tick.Start();
        Opened += (_, _) =>
        {
            if (_shot != null) { _ = ScreenshotAsync(); return; }
            StartWork();
        };
    }

    // ---------------- background (tray) ----------------

    /// <summary>Starts subscription checks and handles links given at start. Runs once, shown or hidden.</summary>
    void StartWork()
    {
        if (_workStarted) return;
        _workStarted = true;
        _subTimer.Start();
        _subTimer.Tick += (_, _) =>
        {
            if (DateTime.Now - _lastUpdateCheck > TimeSpan.FromHours(24)) _ = CheckForUpdateAsync(manual: false);
            if (DateTime.Now - _lastEngineUpdate > TimeSpan.FromHours(24)) _ = UpdateEngineQuietlyAsync();
            if (_readyProgram != null && !IsVisible && Idle) RestartIntoUpdate(hidden: true);
        };
        if (_state.Settings.SubscriptionCheckHours > 0) _ = _subs.AutoCheck();
        if (_startLinks.Count > 0) _ = HandleIncoming(_startLinks);
        if (Updater.JustUpdated)
            ShowBanner($"ClipHarbor was updated to {Updater.CurrentText}.", "What's new", () => Platform.Open(Updater.CurrentReleasePage));
        _ = CheckForUpdateAsync(manual: false);
        _ = UpdateEngineQuietlyAsync();
    }

    DateTime _lastEngineUpdate = DateTime.MinValue;
    bool _engineUpdating;

    /// <summary>Sites change often and old yt-dlp versions stop working, so ClipHarbor keeps its own copy
    /// current. It waits while downloads run, since they use the program being replaced.</summary>
    async Task UpdateEngineQuietlyAsync()
    {
        if (!_state.Settings.UpdateEngineAutomatically || _running.Count > 0 || (!Engine.IsOwnEngine && !Engine.HasOldStaticFfmpeg)) return;
        _lastEngineUpdate = DateTime.Now;
        _engineUpdating = true;
        string? v;
        try
        {
            v = await Engine.UpdateEngineQuietlyAsync(CancellationToken.None);
            // Older versions installed a converter twice the size; swap it for the shared build once.
            await Engine.MoveToSharedFfmpegAsync(CancellationToken.None);
        }
        finally { _engineUpdating = false; }
        if (v != null && !_banner.IsVisible) ShowBanner($"The download engine was updated to {v}.", null, null);
    }

    // ---------------- app updates ----------------

    UpdateInfo? _update;
    DateTime _lastUpdateCheck = DateTime.MinValue;
    bool _updating;

    /// <summary>Looks for a newer release on GitHub. Automatic checks stay quiet unless one is found.</summary>
    public async Task CheckForUpdateAsync(bool manual)
    {
        if (Platform.IsFlatpak)
        {
            // Flathub updates the app (and its engine) with the rest of the system.
            if (manual) await Dialog.Info(this, "Updates", $"This is version {Updater.CurrentText}. Updates come through your software center (Discover or GNOME Software) or the \"flatpak update\" command.");
            return;
        }
        if (_readyProgram != null) { if (manual) ShowRestartBanner(); return; }
        if (_updating || (!manual && !_state.Settings.CheckAppUpdates)) return;
        _lastUpdateCheck = DateTime.Now;
        try
        {
            _update = await Updater.CheckAsync(CancellationToken.None);
            if (_update != null && !manual && _state.Settings.InstallAppUpdates) await InstallQuietlyAsync(_update);
            else if (_update != null) { if (manual || !_banner.IsVisible) ShowUpdateBanner(); }
            else if (manual) await Dialog.Info(this, "Updates", $"You have the latest version of ClipHarbor ({Updater.CurrentText}).");
        }
        catch (Exception ex)
        {
            if (manual) await Dialog.Info(this, "Updates", "Couldn't check for updates: " + ex.Message);
        }
    }

    /// <summary>The new version, already in place on disk, waiting for a restart.</summary>
    string? _readyProgram;

    bool Idle => _running.Count == 0 && !_state.Items.Any(i => i.Status == ItemStatus.Queued);

    /// <summary>Downloads and puts a new version in place without asking. The running copy keeps going
    /// (it was moved aside), and the new one takes over at the next restart: right away when ClipHarbor
    /// is hidden in the tray with nothing to do, otherwise when the person chooses or next starts it.</summary>
    async Task InstallQuietlyAsync(UpdateInfo u)
    {
        var exe = Platform.AppPath ?? "";
        if (exe.Length == 0 || !Updater.CanInstall(u, exe)) { if (!_banner.IsVisible) ShowUpdateBanner(); return; }
        _updating = true; // also stops further checks: the update is on disk from here on
        try { _readyProgram = await Updater.InstallAsync(u, exe, new Progress<(string, double)>(_ => { }), CancellationToken.None); }
        catch { _updating = false; if (!_banner.IsVisible) ShowUpdateBanner(); return; }
        _readyVersion = u.Version.ToString(3);
        if (!IsVisible && Idle) RestartIntoUpdate(hidden: true);
        else ShowRestartBanner();
    }

    string _readyVersion = "";

    void ShowRestartBanner() =>
        ShowBanner($"ClipHarbor {_readyVersion} is installed and starts next time.", "Restart now", () => RestartIntoUpdate(hidden: false));

    void RestartIntoUpdate(bool hidden)
    {
        if (_readyProgram is not { } program) return;
        _readyProgram = null;
        _quitting = true;
        foreach (var c in _running.Values) c.Cancel();
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Downloading)) it.Status = ItemStatus.Queued;
        _state.Save();
        Updater.Launch(program, hidden);
        Close();
        _shutdown();
    }

    void ShowUpdateBanner()
    {
        if (_update is not { } u) return;
        ShowBanner($"ClipHarbor {u.Version.ToString(3)} is available. You have {Updater.CurrentText}.", "Update now", () => _ = UpdateNowAsync());
    }

    async Task UpdateNowAsync()
    {
        if (_readyProgram != null) { RestartIntoUpdate(hidden: false); return; }
        if (_update is not { } u || _updating) return;
        var exe = Platform.AppPath ?? "";
        var notes = u.Notes.Trim();
        if (notes.Length > 800) notes = notes[..800].TrimEnd() + "…";
        bool canInstall = exe.Length > 0 && Updater.CanInstall(u, exe);
        var text = (notes.Length > 0 ? "What's new:\n" + notes + "\n\n" : "")
            + (canInstall
                ? "ClipHarbor will download the update, restart, and carry on. Running downloads pause and continue afterwards."
                : "ClipHarbor can't replace itself where it's installed, so the download page will open instead.");
        if (!await Dialog.Ask(this, $"Update to ClipHarbor {u.Version.ToString(3)}", text, canInstall ? "Update" : "Open download page", "Not now")) return;
        if (!canInstall) { Platform.Open(u.PageUrl); return; }

        _updating = true;
        _bannerBtn.IsVisible = false;
        var prog = new Progress<(string text, double frac)>(p => _bannerText.Text = p.text + (p.frac > 0 && p.frac < 1 ? $"  ({p.frac * 100:0}%)" : ""));
        try
        {
            var program = await Updater.InstallAsync(u, exe, prog, CancellationToken.None);
            // Hand over: save everything, start the new version, and close this one.
            _quitting = true;
            foreach (var c in _running.Values) c.Cancel();
            foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Downloading)) it.Status = ItemStatus.Queued;
            _state.Save();
            Updater.Launch(program);
            Close();
            _shutdown();
        }
        catch (Exception ex)
        {
            _updating = false;
            ShowBanner("Couldn't update: " + ex.Message, "Open download page", () => Platform.Open(u.PageUrl), true);
        }
    }

    /// <summary>Started at sign-in: work in the tray without showing the window.</summary>
    public void StartInBackground() => StartWork();

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void CheckSubscriptionsNow() => _ = _subs.CheckNow();

    static string TrayName => Platform.IsMac ? "menu bar" : "system tray";

    /// <summary>The computer is signing out or shutting down: stop without asking anything. Running downloads
    /// go back in the queue, so they carry on when ClipHarbor next starts.</summary>
    public void EndForSessionEnd()
    {
        if (_quitting) return;
        _quitting = true;
        foreach (var c in _running.Values) c.Cancel();
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Downloading)) it.Status = ItemStatus.Queued;
        if (_shot == null) try { _state.Save(); } catch { }
    }

    /// <summary>Really ends the app: pauses running downloads (they resume next time) and saves.</summary>
    public async Task QuitAsync()
    {
        if (_quitting) return;
        if (_shot == null && _running.Count > 0)
        {
            ShowFromTray();
            if (!await Dialog.Ask(this, "ClipHarbor", "Downloads are still running. They'll pause and you can resume them next time. Quit now?", "Quit", "Keep downloading")) return;
        }
        _quitting = true;
        foreach (var c in _running.Values) c.Cancel();
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Downloading)) it.Status = ItemStatus.Paused;
        if (_shot == null) _state.Save();
        Close();
        _shutdown();
    }

    // ---------------- layout ----------------

    Control BuildLayout()
    {
        // top bar
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { new Logo(), Ui.Text("ClipHarbor", "brand") } };
        _paste.Padding = new Thickness(18, 9); _paste.MinWidth = 140;
        _smart.Padding = new Thickness(18, 9);
        _settings.Width = _settings.Height = 38;
        _settings.Classes.Add("flat");
        ToolTip.SetTip(_paste, $"Download the link you copied ({(Platform.IsMac ? "⌘" : "Ctrl+")}V)");
        ToolTip.SetTip(_smart, "One click: download right away with your saved choices");
        _paste.Click += async (_, _) => await PasteFromClipboard();
        _smart.IsChecked = _state.Settings.SmartMode;
        _smart.Click += async (_, _) => await ToggleSmart();
        _settings.Click += async (_, _) => await OpenSettings();
        _search.TextChanged += (_, _) => { _list.Search = _search.Text ?? ""; _list.Rebuild(); };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("220,Auto,Auto,*,Auto,Auto"), Height = 64 };
        top.Children.Add(brand);
        Grid.SetColumn(_paste, 1); _paste.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(_paste);
        Grid.SetColumn(_smart, 2); _smart.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(_smart);
        Grid.SetColumn(_search, 4); _search.Margin = new Thickness(10, 0); top.Children.Add(_search);
        Grid.SetColumn(_settings, 5); _settings.Margin = new Thickness(0, 0, 20, 0); top.Children.Add(_settings);
        foreach (var c in new Control[] { _paste, _smart, _settings }) c.VerticalAlignment = VerticalAlignment.Center;
        top.SizeChanged += (_, e) => _search.IsVisible = e.NewSize.Width > 900;
        var topBar = new Border { Classes = { "bar" }, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };

        // sidebar
        _nav.Items.AddRange(new[]
        {
            new NavItem { Header = true, Text = "Downloads" },
            new NavItem { Key = "all", Text = "All", Glyph = Icons.List },
            new NavItem { Key = "active", Text = "In progress", Glyph = Icons.Download },
            new NavItem { Key = "done", Text = "Finished", Glyph = Icons.Check },
            new NavItem { Key = "failed", Text = "Needs attention", Glyph = Icons.Alert },
            new NavItem { Header = true, Text = "Library" },
            new NavItem { Key = "video", Text = "Videos", Glyph = Icons.Video },
            new NavItem { Key = "audio", Text = "Audio", Glyph = Icons.Music },
            new NavItem { Key = "gifs", Text = "GIFs", Glyph = Icons.Image },
            new NavItem { Key = "playlists", Text = "From playlists", Glyph = Icons.Playlist },
            new NavItem { Header = true, Text = "Follow" },
            new NavItem { Key = "subs", Text = "Subscriptions", Glyph = Icons.Star },
        });
        _nav.Footer = "Free and unlimited: no download caps, no playlist limits, no sign-up.";
        _nav.Build();
        _nav.Selected += Navigate;

        // banner
        _bannerBtn.Margin = new Thickness(12, 0, 6, 0);
        _bannerBtn.VerticalAlignment = _bannerClose.VerticalAlignment = VerticalAlignment.Center;
        _bannerBtn.Click += (_, _) => _bannerAction?.Invoke();
        _bannerClose.Click += (_, _) => { _analyzeCts?.Cancel(); _banner.IsVisible = false; };
        var bannerDock = new DockPanel();
        DockPanel.SetDock(_bannerClose, Dock.Right); DockPanel.SetDock(_bannerBtn, Dock.Right);
        bannerDock.Children.AddRange(new Control[] { _bannerClose, _bannerBtn, _bannerText });
        _banner.Child = bannerDock;
        _banner.MinHeight = 52;

        // bottom bar
        var pauseAll = Ui.Button("Pause all", Icons.Pause);
        var resumeAll = Ui.Button("Resume all", Icons.Play);
        var clearDone = Ui.Button("Clear finished", Icons.Clear);
        foreach (var b in new[] { pauseAll, resumeAll, clearDone }) { b.FontWeight = FontWeight.Normal; b.Padding = new Thickness(12, 6); }
        pauseAll.Click += (_, _) => { foreach (var it in _state.Items.Where(i => i.Status is ItemStatus.Downloading or ItemStatus.Queued).ToList()) Pause(it); Changed(); };
        resumeAll.Click += (_, _) => { foreach (var it in _state.Items.Where(i => i.Status is ItemStatus.Paused or ItemStatus.Failed)) it.Status = ItemStatus.Queued; Changed(); };
        clearDone.Click += (_, _) => { _state.Items.RemoveAll(i => i.Status is ItemStatus.Done or ItemStatus.Canceled); Changed(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { pauseAll, resumeAll, clearDone } };
        var bottomGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 48, Margin = new Thickness(20, 0, 16, 0) };
        Grid.SetColumn(buttons, 1);
        bottomGrid.Children.AddRange(new Control[] { _status, buttons });
        var bottom = new Border { Classes = { "bar" }, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottomGrid };

        // main area
        var main = new DockPanel { Classes = { "back" } };
        DockPanel.SetDock(_banner, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        main.Children.AddRange(new Control[] { _banner, bottom, new Panel { Children = { _list, _subs } } });

        var body = new DockPanel();
        DockPanel.SetDock(_nav, Dock.Left);
        body.Children.AddRange(new Control[] { _nav, main });

        var root = new DockPanel { Classes = { "back" } };
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.AddRange(new Control[] { topBar, body });
        return root;
    }

    void ShowBanner(string text, string? button, Action? action, bool warn = false)
    {
        _bannerText.Text = text;
        _banner.Classes.Set("warn", warn);
        _bannerBtn.IsVisible = button != null;
        if (button != null) _bannerBtn.Content = button;
        _bannerAction = action;
        _banner.IsVisible = true;
    }

    // ---------------- navigation ----------------

    void Navigate(string key)
    {
        _nav.SelectedKey = key;
        _nav.Refresh();
        bool subs = key == "subs";
        _subs.IsVisible = subs;
        _list.IsVisible = !subs;
        _list.Filter = key switch
        {
            "active" => i => i.Status is ItemStatus.Downloading or ItemStatus.Queued or ItemStatus.Paused,
            "done" => i => i.Status == ItemStatus.Done,
            "failed" => i => i.Status is ItemStatus.Failed or ItemStatus.Canceled,
            "video" => i => i.Options.Kind == MediaKind.Video && !IsAnimation(i) && (!i.Direct || VideoExt.Contains(FileExt(i))),
            "audio" => i => i.Options.Kind == MediaKind.Audio,
            "gifs" => IsAnimation,
            "playlists" => i => i.Playlist.Length > 0,
            _ => _ => true,
        };
        _list.EmptyTitle = key switch
        {
            "active" => "Nothing downloading right now",
            "done" => "No finished downloads yet",
            "failed" => "Everything went fine",
            "playlists" => "No playlist downloads yet",
            "gifs" => "No GIFs yet",
            _ => "Copy a video link, then press Paste Link",
        };
        _list.EmptyHint = key == "gifs"
            ? "Choose GIF / WebP when you download a clip, or right-click a finished video and choose Make a GIF."
            : DefaultHint;
        _list.Rebuild();
        if (subs) _subs.Rebuild();
    }

    void UpdateCounts()
    {
        var items = _state.Items;
        _nav.SetCount("all", items.Count);
        _nav.SetCount("active", items.Count(i => i.Status is ItemStatus.Downloading or ItemStatus.Queued or ItemStatus.Paused));
        _nav.SetCount("done", items.Count(i => i.Status == ItemStatus.Done));
        _nav.SetCount("failed", items.Count(i => i.Status is ItemStatus.Failed));
        _nav.SetCount("subs", _state.Subscriptions.Count);
    }

    // ---------------- adding links ----------------

    static readonly Regex UrlRx = new(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase);

    async Task PasteFromClipboard()
    {
        string text = "";
        try { if (Clipboard != null) text = await Clipboard.TryGetTextAsync() ?? ""; } catch { }
        if (UrlRx.Matches(text).Count == 0)
        {
            var p = new PromptDialog("Paste a link", "Copy a link from your browser, or type it here:", text.Trim());
            if (await p.ShowDialog<bool?>(this) != true) return;
            text = p.Value;
        }
        await AddLinks(text);
    }

    /// <summary>Links given on the command line, by a second start, or sent from the browser (clipharbor://).</summary>
    public async Task HandleIncoming(IEnumerable<string> links)
    {
        var plain = new List<string>();
        foreach (var l in links)
        {
            if (!BrowserLink.IsLink(l)) { plain.Add(l); continue; }
            if (!BrowserLink.TryParse(l, out var url, out var gif))
            {
                ShowBanner("The browser sent a link ClipHarbor couldn't read.", null, null, true);
                continue;
            }
            if (!gif) { plain.Add(url); continue; }
            // "Make a GIF": the usual choices, switched to GIF (or the WebP picked last time).
            var s = _state.Settings;
            var o = (s.SmartMode ? s.SmartOptions : s.LastOptions).Clone();
            if (o.Kind != MediaKind.Animation)
            {
                o.Kind = MediaKind.Animation;
                o.Format = s.LastOptions.Kind == MediaKind.Animation ? s.LastOptions.Format : "gif";
                o.AnimationWidth = s.LastOptions.Kind == MediaKind.Animation ? s.LastOptions.AnimationWidth : 480;
            }
            await AnalyzeAndQueue(url, o, quiet: s.SmartMode);
        }
        if (plain.Count > 0) await AddLinks(string.Join("\n", plain));
    }

    public async Task AddLinks(string text)
    {
        var urls = UrlRx.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')')).Distinct().ToList();
        if (urls.Count == 0)
        {
            ShowBanner("That doesn't look like a link. Copy the address of a video page and try again.", null, null, true);
            return;
        }
        if (urls.Count > 1 && !_state.Settings.SmartMode)
        {
            // several links: one choice for all
            var d = new OptionsDialog(null, _state.Settings.LastOptions, $"Download {urls.Count} links");
            if (await d.ShowDialog<bool?>(this) != true) return;
            _state.Settings.LastOptions = d.Result.Clone();
            foreach (var u in urls) await AnalyzeAndQueue(u, d.Result, quiet: true);
            return;
        }
        foreach (var u in urls)
            await AnalyzeAndQueue(u, _state.Settings.SmartMode ? _state.Settings.SmartOptions : null, quiet: _state.Settings.SmartMode);
    }

    async Task AnalyzeAndQueue(string url, DownloadOptions? preset, bool quiet)
    {
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        ShowBanner("Looking at the link…  " + Shorten(url), null, null);
        LinkInfo info;
        try { info = await Engine.AnalyzeAsync(url, cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (InvalidOperationException ex) when (ex.Message == "ENGINE_MISSING")
        {
            ShowBanner($"To download from video sites, install the free download engine first (one time, {Engine.InstallSize}).", "Install now", () => _ = InstallEngine(url), true);
            return;
        }
        catch (SignInNeededException ex)
        {
            // Settings first, then try the link again with the new sign-in choice.
            ShowBanner(ex.Message, "Open Settings", async () => { await OpenSettings(); await AnalyzeAndQueue(url, preset, quiet); }, true);
            return;
        }
        catch (Exception ex)
        {
            ShowBanner("Couldn't read that link: " + ex.Message, "Try again", () => _ = AnalyzeAndQueue(url, preset, quiet), true);
            return;
        }
        if (cts.IsCancellationRequested) return;
        _banner.IsVisible = false;

        DownloadOptions opts;
        List<PlaylistEntry> entries;
        string fileName = "";
        double clipStart = -1, clipEnd = -1;
        if (preset != null && quiet)
        {
            opts = preset.Clone();
            entries = info.Entries;
        }
        else
        {
            var d = new OptionsDialog(info, preset ?? _state.Settings.LastOptions, info.IsPlaylist ? "Download playlist" : "Download");
            if (await d.ShowDialog<bool?>(this) != true) return;
            opts = d.Result.Clone();
            entries = d.ChosenEntries;
            fileName = d.FileName;
            clipStart = d.ClipStart; clipEnd = d.ClipEnd;
            _state.Settings.LastOptions = opts.Clone();
        }
        Queue(info, opts, entries, fileName: fileName, clipStart: clipStart, clipEnd: clipEnd);
    }

    public void Queue(LinkInfo info, DownloadOptions opts, List<PlaylistEntry> entries, string? playlistName = null, string fileName = "", double clipStart = -1, double clipEnd = -1)
    {
        // A part of a video gets its own name, so it never replaces (or is skipped as) the whole video.
        bool clip = clipStart >= 0 && clipEnd > clipStart;
        if (clip && fileName.Length == 0)
            fileName = Engine.SafeName($"{(info.Title.Length > 0 ? info.Title : "clip")} ({Engine.TimeForName(clipStart)}-{Engine.TimeForName(clipEnd)})");
        if (info.IsPlaylist)
        {
            var name = playlistName ?? (info.Title.Length > 0 ? info.Title : "Playlist");
            var t = DateTime.Now;
            int k = 0;
            foreach (var e in entries)
            {
                _state.Items.Add(new DownloadItem
                {
                    Url = e.Url, Title = e.Title, Uploader = e.Uploader.Length > 0 ? e.Uploader : info.Uploader, ThumbnailUrl = e.Thumbnail,
                    DurationSeconds = e.Duration, Playlist = name, Options = opts.Clone(), Added = t.AddMilliseconds(-(k++)), UseSignIn = info.NeedsSignIn,
                });
            }
            if (_shot == null && _state.Settings.SmartMode) ShowBanner($"Added {entries.Count} videos from “{name}”.", null, null);
        }
        else
        {
            _state.Items.Add(new DownloadItem
            {
                Url = info.Url, Title = fileName.Length > 0 ? fileName : info.Title, Uploader = info.Uploader, ThumbnailUrl = info.Thumbnail,
                DurationSeconds = clip ? clipEnd - clipStart : info.Duration, ClipStart = clip ? clipStart : -1, ClipEnd = clip ? clipEnd : -1,
                Options = opts.Clone(), Direct = info.IsDirectFile, TotalBytes = info.DirectSize, UseSignIn = info.NeedsSignIn, FileName = fileName,
            });
        }
        if (_nav.SelectedKey is "done" or "failed" or "subs") Navigate("all");
        Changed();
    }

    async Task InstallEngine(string? retryUrl)
    {
        _bannerBtn.IsVisible = false;
        var prog = new Progress<(string text, double frac)>(p => { _bannerText.Text = "Installing download engine — " + p.text + (p.frac > 0 && p.frac < 1 ? $"  ({p.frac * 100:0}%)" : ""); });
        try
        {
            await Engine.InstallEngineAsync(prog, true, CancellationToken.None);
            ShowBanner("Download engine installed. You're all set.", null, null);
            if (retryUrl != null) await AnalyzeAndQueue(retryUrl, _state.Settings.SmartMode ? _state.Settings.SmartOptions : null, _state.Settings.SmartMode);
        }
        catch (Exception ex)
        {
            ShowBanner("Couldn't install the engine: " + ex.Message, "Try again", () => _ = InstallEngine(retryUrl), true);
        }
    }

    void CheckEngineBanner()
    {
        if (!Engine.HasEngine)
            ShowBanner("One-time setup: install the free download engine to save videos from YouTube and 1,000+ other sites. Direct file links work already.", "Install engine", () => _ = InstallEngine(null), true);
    }

    static string Shorten(string u) => u.Length > 70 ? u[..67] + "…" : u;

    async Task ToggleSmart()
    {
        if (_smart.IsChecked == true)
        {
            var d = new OptionsDialog(null, _state.Settings.SmartOptions, "Smart Mode choices");
            if (await d.ShowDialog<bool?>(this) != true) { _smart.IsChecked = false; return; }
            _state.Settings.SmartOptions = d.Result.Clone();
            _state.Settings.SmartMode = true;
            ShowBanner($"Smart Mode is on: links download right away as {_state.Settings.SmartOptions.Describe()}.", "Change", () => { _smart.IsChecked = true; _ = ToggleSmart(); });
        }
        else
        {
            _state.Settings.SmartMode = false;
            _banner.IsVisible = false;
        }
        Changed();
    }

    async Task OpenSettings()
    {
        var d = new SettingsDialog(_state.Settings);
        var ok = await d.ShowDialog<bool?>(this) == true;
        if (ok)
        {
            App.ApplyTheme(_state.Settings.Theme);
            Changed();
            if (!StartAtLogin.Set(_state.Settings.StartAtLogin) && _state.Settings.StartAtLogin)
                await Dialog.Info(this, "Settings", "ClipHarbor couldn't set itself to start when you sign in. Your other settings were saved.");
        }
        if (Engine.HasEngine && _banner.IsVisible && (_bannerText.Text ?? "").StartsWith("One-time")) _banner.IsVisible = false;
        if (_readyProgram != null && d.FoundUpdate != null) ShowRestartBanner();
        else if (d.FoundUpdate != null) { _update = d.FoundUpdate; ShowUpdateBanner(); }
    }

    // ---------------- row actions ----------------

    void OnRowAction(DownloadItem it, RowAction a)
    {
        switch (a)
        {
            case RowAction.Primary:
                if (it.Status is ItemStatus.Downloading or ItemStatus.Queued) Pause(it);
                else if (it.Status == ItemStatus.Done) OpenFile(it);
                else { it.Status = ItemStatus.Queued; it.Message = ""; }
                break;
            case RowAction.Folder: ShowInFolder(it); break;
            case RowAction.Remove: _ = Remove(new[] { it }, false); break;
        }
        Changed();
    }

    void Pause(DownloadItem it)
    {
        if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
        it.Status = ItemStatus.Paused;
    }

    async Task Remove(IEnumerable<DownloadItem> items, bool deleteFiles)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        if (deleteFiles && !await Dialog.Ask(this, "Delete files", $"Delete {list.Count} file(s) from your computer?", "Delete", "Cancel")) return;
        foreach (var it in list)
        {
            if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
            if (deleteFiles && File.Exists(it.FilePath)) try { File.Delete(it.FilePath); } catch { }
            _state.Items.Remove(it);
        }
        Changed();
    }

    void OpenFile(DownloadItem it)
    {
        if (File.Exists(it.FilePath)) Platform.Open(it.FilePath);
        else FileMissing(it, play: true);
    }

    void ShowInFolder(DownloadItem it)
    {
        if (File.Exists(it.FilePath)) { Platform.ShowInFolder(it.FilePath); return; }
        if (it.Status == ItemStatus.Done) { FileMissing(it, play: false); return; }
        var f = ItemFolder(it);
        if (Directory.Exists(f)) Platform.OpenFolder(f);
    }

    static string ItemFolder(DownloadItem it)
    {
        var d = Path.GetDirectoryName(it.FilePath);
        if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
        var f = it.Options.Folder;
        if (it.Options.PlaylistSubfolder && it.Playlist.Length > 0) { var sub = Path.Combine(f, Engine.SafeName(it.Playlist)); if (Directory.Exists(sub)) f = sub; }
        return f;
    }

    /// <summary>The file was renamed, moved or deleted outside ClipHarbor: say so and offer to point to it.</summary>
    void FileMissing(DownloadItem it, bool play)
    {
        var name = string.IsNullOrEmpty(it.FilePath) ? it.Title : Path.GetFileName(it.FilePath);
        ShowBanner($"Can't find \"{name}\". It may have been renamed, moved or deleted.", "Find it…", () => _ = FindFile(it, play), true);
    }

    async Task FindFile(DownloadItem it, bool play)
    {
        IStorageFolder? start = null;
        try { var f = ItemFolder(it); if (Directory.Exists(f)) start = await StorageProvider.TryGetFolderFromPathAsync(f); } catch { }
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Find " + it.Title, SuggestedStartLocation = start });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path == null || !File.Exists(path)) return;
        it.FilePath = path;
        it.TotalBytes = new FileInfo(path).Length;
        Changed();
        if (play) Platform.Open(path); else Platform.ShowInFolder(path);
    }

    ContextMenu BuildMenu()
    {
        var sel = _list.Selected.ToList();
        var m = new ContextMenu();
        void Add(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
        void Sep() { if (m.Items.Count > 0 && m.Items[^1] is not Separator) m.Items.Add(new Separator()); }
        var one = sel.Count == 1 ? sel[0] : null;
        if (one?.Status == ItemStatus.Done) Add("Play", () => OpenFile(one));
        if (one != null) Add("Show in " + Platform.FileManagerName, () => ShowInFolder(one));
        if (one != null) Add("Copy link", () => { try { Clipboard?.SetTextAsync(one.Url); } catch { } });
        if (one != null) Add("Open page in browser", () => Platform.Open(one.Url));
        Sep();
        if (sel.Any(i => i.Status is ItemStatus.Downloading or ItemStatus.Queued))
            Add("Pause", () => { foreach (var i in sel) if (i.Status is ItemStatus.Downloading or ItemStatus.Queued) Pause(i); Changed(); });
        if (sel.Any(i => i.Status is ItemStatus.Paused or ItemStatus.Failed or ItemStatus.Canceled))
            Add("Resume / try again", () => { foreach (var i in sel) if (i.Status is ItemStatus.Paused or ItemStatus.Failed or ItemStatus.Canceled) { i.Status = ItemStatus.Queued; i.Message = ""; } Changed(); });
        if (sel.Any(i => i.Status == ItemStatus.Done))
            Add("Download again", () => { foreach (var i in sel.Where(i => i.Status == ItemStatus.Done)) { i.Status = ItemStatus.Queued; i.FilePath = ""; i.Progress = 0; } Changed(); });
        if (one?.Status == ItemStatus.Done && IsVideoFile(one.FilePath))
        {
            Sep();
            Add("Make a GIF", () => MakeAnimation(one, "gif"));
            Add("Make an animated WebP", () => MakeAnimation(one, "webp"));
            var smaller = new MenuItem { Header = "Make smaller" };
            foreach (var (label, mb) in DownloadOptions.SizeLimits.Where(s => s.mb > 0))
            {
                var mi = new MenuItem { Header = "Under " + label };
                mi.Click += (_, _) => MakeSmaller(one, mb);
                smaller.Items.Add(mi);
            }
            m.Items.Add(smaller);
            Sep();
        }
        if (one != null && !one.Direct && one.SourceFile.Length == 0)
            Add("Change quality or format…", async () =>
            {
                var d = new OptionsDialog(null, one.Options, "Choices for this download");
                if (await d.ShowDialog<bool?>(this) == true) { if (_running.TryGetValue(one.Id, out var c)) c.Cancel(); one.Options = d.Result.Clone(); one.Status = ItemStatus.Queued; one.Progress = 0; Changed(); }
            });
        Sep();
        Add("Remove from list", () => _ = Remove(sel, false));
        if (sel.Any(i => File.Exists(i.FilePath))) Add("Remove and delete file", () => _ = Remove(sel, true));
        return m;
    }

    const string DefaultHint = "Videos, whole playlists, channels, audio and subtitles — as many as you like, in any quality the site offers. No limits, no account.";

    static readonly string[] VideoExt = { ".mp4", ".mkv", ".webm", ".mov", ".m4v", ".avi", ".flv", ".ts", ".3gp", ".wmv" };

    /// <summary>The ending of the item's file, or of its link before it's downloaded.</summary>
    static string FileExt(DownloadItem i)
    {
        var p = i.FilePath.Length > 0 ? i.FilePath : i.Url;
        try { if (p.Contains("://")) p = new Uri(p).AbsolutePath; } catch { }
        return Path.GetExtension(p).ToLowerInvariant();
    }

    /// <summary>GIFs and animated WebPs: made by ClipHarbor, or downloaded straight from a .gif/.webp link.</summary>
    static bool IsAnimation(DownloadItem i) =>
        i.Options.Kind == MediaKind.Animation || (i.Direct && FileExt(i) is ".gif" or ".webp");

    static bool IsVideoFile(string path) => File.Exists(path) && VideoExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Adds a list entry that turns a finished video into a GIF or WebP next to it.</summary>
    async void MakeAnimation(DownloadItem src, string format)
    {
        if (!Engine.HasFfmpeg)
        {
            await Dialog.Info(this, "Make a " + format.ToUpperInvariant(), "This needs the free converter (ffmpeg). Open Settings → Download engine and press Install or Add converter first.");
            return;
        }
        var last = _state.Settings.LastOptions;
        _state.Items.Add(new DownloadItem
        {
            Url = src.Url, Title = src.Title, Uploader = src.Uploader, ThumbnailUrl = src.ThumbnailUrl, DurationSeconds = src.DurationSeconds,
            SourceFile = src.FilePath,
            Options = new DownloadOptions
            {
                Kind = MediaKind.Animation, Format = format, Folder = Path.GetDirectoryName(src.FilePath) ?? src.Options.Folder,
                AnimationWidth = last.Kind == MediaKind.Animation ? last.AnimationWidth : 480,
            },
        });
        if (_nav.SelectedKey is "done" or "failed" or "subs" or "audio" or "video") Navigate("all");
        Changed();
    }

    /// <summary>Adds a list entry that makes a smaller copy of a finished video, e.g. to fit Discord's 10 MB.</summary>
    async void MakeSmaller(DownloadItem src, int mb)
    {
        if (!Engine.HasFfmpeg)
        {
            await Dialog.Info(this, "Make smaller", "This needs the free converter (ffmpeg). Open Settings → Download engine and press Install or Add converter first.");
            return;
        }
        if (File.Exists(src.FilePath) && new FileInfo(src.FilePath).Length <= (long)mb * 1_000_000)
        {
            await Dialog.Info(this, "Make smaller", $"This video is already under {mb} MB ({Fmt.Bytes(new FileInfo(src.FilePath).Length)}).");
            return;
        }
        _state.Items.Add(new DownloadItem
        {
            Url = src.Url, Title = $"{src.Title} ({mb} MB)", Uploader = src.Uploader, ThumbnailUrl = src.ThumbnailUrl, DurationSeconds = src.DurationSeconds,
            SourceFile = src.FilePath,
            Options = new DownloadOptions { Kind = MediaKind.Video, Format = "mp4", TargetSizeMB = mb, Folder = Path.GetDirectoryName(src.FilePath) ?? src.Options.Folder },
        });
        if (_nav.SelectedKey is "done" or "failed" or "subs" or "audio" or "gifs") Navigate("all");
        Changed();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        var cmd = Ui.CommandKey(this);
        if (!e.KeyModifiers.HasFlag(cmd)) return;
        if (e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox) { _ = PasteFromClipboard(); e.Handled = true; }
        else if (e.Key == Key.F) { _search.Focus(); e.Handled = true; }
        else if (e.Key == Key.A && FocusManager?.GetFocusedElement() is not TextBox && _list.IsVisible) { _list.List.SelectAll(); e.Handled = true; }
    }

    // ---------------- scheduler ----------------

    public void Changed() { _dirty = true; _list.Rebuild(); UpdateCounts(); if (_subs.IsVisible) _subs.Rebuild(); }

    void Tick()
    {
        int max = Math.Max(1, _state.Settings.MaxConcurrent);
        int running = _running.Count;
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Queued).OrderBy(i => i.Added).ToList())
        {
            if (running >= max || _shot != null || _engineUpdating) break;
            if (_state.MustWait(it, _running)) continue;
            Start(it);
            running++;
        }
        var active = _state.Items.Where(i => i.Status == ItemStatus.Downloading).ToList();
        double speed = active.Sum(i => i.Speed);
        int queued = _state.Items.Count(i => i.Status == ItemStatus.Queued);
        var parts = new List<string>();
        if (active.Count > 0) parts.Add($"{active.Count} downloading" + (speed > 0 ? $" at {Fmt.Bytes(speed)}/s" : ""));
        if (queued > 0) parts.Add($"{queued} waiting");
        if (parts.Count == 0) parts.Add(_state.Items.Count == 0 ? "Ready" : $"{_state.Items.Count(i => i.Status == ItemStatus.Done)} of {_state.Items.Count} downloaded");
        if (!Engine.HasEngine) parts.Add("engine not installed");
        else if (!Engine.HasFfmpeg) parts.Add("converter missing (Settings)");
        _status.Text = string.Join("   ·   ", parts);
        App.SetTrayText("ClipHarbor — " + parts[0]);
        if (active.Count > 0) RowView.RefreshAll();
        if (_dirty && _shot == null)
        {
            _dirty = false;
            string? text = null;
            try { text = _state.Snapshot(); } catch { _dirty = true; }
            if (text != null) _ = Task.Run(() => AppState.Write(text));
        }
    }

    void Start(DownloadItem it)
    {
        var cts = new CancellationTokenSource();
        _running[it.Id] = cts;
        it.Status = ItemStatus.Downloading;
        it.Message = ""; it.Speed = 0; it.Eta = -1; it.Phase = "";
        Changed();
        _ = Task.Run(async () =>
        {
            string? error = null; bool canceled = false;
            try { await Engine.RunDownloadAsync(it, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            catch (Exception ex) { if (cts.IsCancellationRequested) canceled = true; else error = ex.Message; }
            Dispatcher.UIThread.Post(() =>
            {
                // Only this run's entry (a newer run of the same item must stay tracked).
                if (_running.GetValueOrDefault(it.Id) == cts) _running.Remove(it.Id);
                if (canceled) { if (it.Status == ItemStatus.Downloading) it.Status = ItemStatus.Paused; }
                else if (error != null) { it.Status = ItemStatus.Failed; it.Message = error; }
                else if (_state.TakeFreeNameIfShared(it)) { it.Speed = 0; it.Phase = ""; }
                else
                {
                    it.Status = ItemStatus.Done; it.Progress = 1; it.Speed = 0; it.Phase = "";
                    NotifyDone(it);
                }
                Changed();
            });
        });
    }

    void NotifyDone(DownloadItem it)
    {
        if (!_state.Settings.NotifyWhenDone || IsActive) return;
        if (_state.Items.Any(i => i.Status is ItemStatus.Downloading or ItemStatus.Queued)) return;
        Platform.Notify("Downloads finished", it.Title, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting || _shot != null) return;
        // Signing out or shutting down: never hold it up (that cancels the logout). Save and go.
        if (e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown) { EndForSessionEnd(); return; }
        e.Cancel = true;
        var s = _state.Settings;
        // No tray icon to come back from (the desktop refused it): hiding would leave no way back in.
        if (App.TrayFailed) { await QuitAsync(); return; }
        if (!s.BackgroundAsked)
        {
            // Ask once; the answer is kept and can be changed in Settings.
            s.BackgroundAsked = true;
            s.RunInBackground = await Dialog.Ask(this, "Keep ClipHarbor running?",
                $"ClipHarbor can keep running in the {TrayName} after you close this window, so it keeps checking your subscriptions and finishing downloads. " +
                $"Open it again from the icon in the {TrayName}.\n\nYou can change this later in Settings.",
                "Keep running", "Quit");
            Changed();
        }
        // The setting is followed as written: on means closing always goes to the tray.
        if (s.RunInBackground) { Hide(); return; }
        await QuitAsync();
    }

    // ---------------- screenshot mode ----------------

    /// <summary>Renders the window (and an optional dialog) to PNG files, then quits. Used by automated checks.</summary>
    async Task ScreenshotAsync()
    {
        Window? dialog = null;
        try
        {
            await Task.Delay(300);
            var preview = Environment.GetEnvironmentVariable("CLIPHARBOR_PREVIEW");
            if (preview != null) dialog = Preview(preview);
            await Task.Delay(1500);
            Save(this, _shot!);
            if (dialog != null) Save(dialog, Path.ChangeExtension(_shot!, ".dialog.png"));
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        dialog?.Close();
        _quitting = true;
        Close();
        _shutdown();
    }

    static void Save(Window w, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // The window's own root (its background plus everything inside), placed at 0,0.
        var root = w.GetVisualChildren().OfType<Visual>().First();
        var size = w.ClientSize;
        var scale = w.RenderScaling;
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bmp.Render(root);
        bmp.Save(path);
    }

    /// <summary>Developer preview (screenshot mode only): sample rows and dialogs.</summary>
    Window? Preview(string what)
    {
        if (what.Contains("dark")) { _state.Settings.Theme = "Dark"; App.ApplyTheme("Dark"); }
        // Pictures for publishing can show a neutral folder instead of the real user name.
        var folder = Environment.GetEnvironmentVariable("CLIPHARBOR_PREVIEW_FOLDER") is { Length: > 0 } pf ? pf : AppState.DefaultFolder();
        var o = new DownloadOptions { Folder = folder };
        var now = DateTime.Now;
        _state.Items.AddRange(new[]
        {
            new DownloadItem { Title = "Mountain trail timelapse — sunrise to sunset", Uploader = "Sample Channel", DurationSeconds = 754, Options = o.Clone(), Status = ItemStatus.Downloading, Progress = .42, Speed = 3.4e6, Eta = 38, TotalBytes = 214_000_000, Added = now },
            new DownloadItem { Title = "Lo-fi beats for studying (1 hour mix)", Uploader = "Another Creator", DurationSeconds = 3612, Options = new DownloadOptions { Kind = MediaKind.Audio, Format = "mp3" }, Status = ItemStatus.Queued, Added = now.AddMinutes(-1) },
            new DownloadItem { Title = "How to bake sourdough bread at home", Uploader = "Kitchen Notes", DurationSeconds = 1265, Options = new DownloadOptions { MaxHeight = 2160, Subtitles = true }, Status = ItemStatus.Done, TotalBytes = 1_480_000_000, Added = now.AddMinutes(-5), Playlist = "Weekend baking" },
            new DownloadItem { Title = "Episode 12: Building a tiny cabin", Uploader = "Workshop", DurationSeconds = 932, Options = new DownloadOptions { MaxHeight = 720 }, Status = ItemStatus.Paused, Progress = .63, TotalBytes = 98_000_000, Added = now.AddMinutes(-7) },
            new DownloadItem { Title = "Private video", Options = o.Clone(), Status = ItemStatus.Failed, Message = "This video is private. Turn on 'Use sign-in from browser' in Settings.", Added = now.AddMinutes(-9) },
        });
        _banner.IsVisible = false;
        Window? d = null;
        if (what.Contains("gif"))
        {
            _state.Items.Add(new DownloadItem { Title = "Cat discovers the cucumber", Uploader = "@funnyclips", DurationSeconds = 9, Options = new DownloadOptions { Kind = MediaKind.Animation, Format = "gif" }, Status = ItemStatus.Downloading, Phase = "converting", Progress = .55, Added = now.AddSeconds(1) });
            var info = new LinkInfo { Title = "Cat discovers the cucumber", Uploader = "@funnyclips", Duration = 9, Heights = new() { 720, 480, 360 } };
            d = new OptionsDialog(info, new DownloadOptions { Kind = MediaKind.Animation, Format = "gif", Folder = folder }, "Download");
        }
        Changed();
        if (d == null && what.Contains("options"))
        {
            var info = new LinkInfo { Title = "How to bake sourdough bread at home", Uploader = "Kitchen Notes", Duration = 1265, Heights = new() { 2160, 1440, 1080, 720, 480, 360 }, SizeByHeight = new() { [2160] = 1_400_000_000, [1080] = 310_000_000, [720] = 150_000_000 }, AudioSize = 20_000_000, SubtitleLangs = new() { "en", "es", "fr", "de" } };
            d = new OptionsDialog(info, o, "Download");
        }
        else if (what.Contains("nosubs"))
        {
            var info = new LinkInfo { Title = "Mountain trail timelapse — sunrise to sunset", Uploader = "Sample Channel", Duration = 754, Heights = new() { 1080, 720, 480 } };
            d = new OptionsDialog(info, o, "Download");
        }
        else if (what.Contains("playlist"))
        {
            var info = new LinkInfo { Title = "Weekend baking", Uploader = "Kitchen Notes", IsPlaylist = true };
            for (int i = 1; i <= 12; i++) info.Entries.Add(new PlaylistEntry { Title = $"Baking lesson {i}", Duration = 300 + i * 41, Url = "x" + i, Id = "x" + i });
            d = new OptionsDialog(info, o, "Download playlist");
        }
        else if (what.Contains("settings")) d = new SettingsDialog(_state.Settings);
        else if (what.Contains("subs")) Navigate("subs");
        else if (what.Contains("gifs")) Navigate("gifs");
        if (d != null) { d.ShowActivated = false; d.Show(this); }
        return d;
    }
}
