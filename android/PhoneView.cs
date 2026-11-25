using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClipHarbor;

/// <summary>ClipHarbor's phone screen: a link box at the top, the downloads below, and a sheet with the
/// choices for a link. Links usually arrive from Android's Share menu.</summary>
public class PhoneView : Grid
{
    static PhoneView? _current;
    public static PhoneView Current => _current ??= new PhoneView();

    static readonly Regex UrlRx = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

    readonly AppState _state;
    readonly DownloadList _list = new();
    readonly TextBox _link = new() { Watermark = "Paste a video link", VerticalContentAlignment = VerticalAlignment.Center };
    readonly Border _banner = new() { Classes = { "banner" }, IsVisible = false, Padding = new Thickness(14, 10), Margin = new Thickness(12, 0, 12, 8), CornerRadius = new CornerRadius(8) };
    readonly TextBlock _bannerText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _sheet = new() { IsVisible = false };
    readonly Dictionary<string, CancellationTokenSource> _running = new();
    CancellationTokenSource? _analyzeCts;
    bool _dirty;
    DateTime _lastUpdate = DateTime.MinValue;

    PhoneView()
    {
        RowView.Compact = true;
        _state = AppState.Load();
        Engine.Settings = _state.Settings;
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Downloading)) it.Status = ItemStatus.Queued;
        Bind(BackgroundProperty, this.GetResourceObservable("Back"));
        Avalonia.Controls.Documents.TextElement.SetFontSize(this, 15);

        // Header
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(16, 14, 16, 10), Children = { new Logo { Width = 30, Height = 30 }, Ui.Text("ClipHarbor", "title") } };

        // Link box
        var paste = Ui.Button("Paste", Icons.Link);
        var go = Ui.Button("", Icons.Download, "accent");
        paste.Margin = new Thickness(8, 0, 0, 0); go.Margin = new Thickness(8, 0, 0, 0);
        paste.MinHeight = go.MinHeight = _link.MinHeight = 44;
        paste.Click += async (_, _) => { var t = await ClipboardText(); if (t.Length > 0) { _link.Text = t; await Analyze(t); } };
        go.Click += async (_, _) => await Analyze(_link.Text ?? "");
        _link.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Analyze(_link.Text ?? ""); } };
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(12, 0, 12, 10) };
        Grid.SetColumn(paste, 1); Grid.SetColumn(go, 2);
        input.Children.AddRange(new Control[] { _link, paste, go });

        _banner.Child = _bannerText;
        _banner.Tapped += (_, _) => _banner.IsVisible = false;

        // Downloads
        _list.Source = _state.Items;
        _list.EmptyTitle = "Nothing downloaded yet";
        _list.EmptyHint = "In YouTube, X, Reddit or another app, press Share and pick ClipHarbor. Or paste a link above.";
        _list.ActionClicked += OnAction;
        _list.Opened += it => { if (it.Status == ItemStatus.Done && File.Exists(it.FilePath)) Platform.Open(it.FilePath); };

        var top = new StackPanel { Children = { title, input, _banner } };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_list);
        dock.Classes.Add("phoneback");
        Children.Add(dock);
        Children.Add(_sheet);

        _dock = dock;
        _list.Rebuild();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    readonly DockPanel _dock = null!;

    /// <summary>Android draws apps under the status bar and the navigation bar; keep the content clear of them.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this)?.InsetsManager is not { } insets) return;
        void Apply() { _dock.Margin = insets.SafeAreaPadding; _sheet.Padding = insets.SafeAreaPadding; }
        insets.SafeAreaChanged += (_, _) => Apply();
        Apply();
    }

    // ---------------- links ----------------

    async Task<string> ClipboardText()
    {
        try { return (await TopLevel.GetTopLevel(this)!.Clipboard!.GetTextAsync())?.Trim() ?? ""; } catch { return ""; }
    }

    /// <summary>A link shared from another app.</summary>
    public void Shared(string text) => Dispatcher.UIThread.Post(async () => { _link.Text = text; await Analyze(text); });

    /// <summary>The app came to the front: keep yt-dlp current, once a day.</summary>
    public void Resumed()
    {
        if (DateTime.Now - _lastUpdate < TimeSpan.FromHours(24)) return;
        _lastUpdate = DateTime.Now;
        _ = Task.Run(async () =>
        {
            var v = await AndroidSetup.Runner.UpdateAsync();
            if (v != null) Dispatcher.UIThread.Post(() => { if (!_banner.IsVisible) Banner($"The download engine was updated to {v}.", false); });
        });
    }

    void Banner(string text, bool warn)
    {
        _bannerText.Text = text;
        _banner.Classes.Set("warn", warn);
        _banner.IsVisible = true;
    }

    async Task Analyze(string text)
    {
        var url = UrlRx.Match(text).Value.TrimEnd('.', ',', ')');
        if (url.Length == 0) { Banner("That doesn't look like a link. Copy the address of a video and try again.", true); return; }
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        Banner("Looking at the link…", false);
        LinkInfo info;
        try { info = await Engine.AnalyzeAsync(url, cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { if (!cts.IsCancellationRequested) Banner("Couldn't read that link: " + ex.Message, true); return; }
        if (cts.IsCancellationRequested) return;
        _banner.IsVisible = false;
        _link.Text = "";
        ShowChoices(info);
    }

    // ---------------- the choices sheet ----------------

    void ShowChoices(LinkInfo info)
    {
        var o = _state.Settings.LastOptions.Clone();
        if (o.Kind == MediaKind.Animation) o.Kind = MediaKind.Video; // GIFs come later on phones
        var video = new RadioButton { Content = "Video", GroupName = "kind", IsChecked = o.Kind == MediaKind.Video };
        var audio = new RadioButton { Content = "Audio only", GroupName = "kind", IsChecked = o.Kind == MediaKind.Audio };
        var quality = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var heights = new List<int> { 0 };
        var labels = new List<string> { "Best available" };
        foreach (var h in (info.Heights.Count > 0 ? info.Heights : new List<int> { 2160, 1440, 1080, 720, 480, 360 })
                     .Select(h => h >= 2160 ? 2160 : h >= 1440 ? 1440 : h >= 1080 ? 1080 : h >= 720 ? 720 : h >= 480 ? 480 : h >= 360 ? 360 : 240).Distinct())
        {
            heights.Add(h);
            var size = info.SizeByHeight.Where(kv => kv.Key <= h).OrderByDescending(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
            labels.Add(DownloadOptions.QualityLabel(h) + (size > 0 ? "   ~" + Fmt.Bytes(size + info.AudioSize) : ""));
        }
        quality.ItemsSource = labels;
        var sel = heights.IndexOf(o.MaxHeight);
        if (sel < 0 && o.MaxHeight > 0) sel = heights.FindIndex(v => v > 0 && v <= o.MaxHeight);
        quality.SelectedIndex = Math.Max(0, sel < 0 ? heights.IndexOf(1080) : sel);
        var audioFormat = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "MP3", "M4A", "OPUS" } };
        audioFormat.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "mp3", "m4a", "opus" }, o.Format));
        void KindChanged() { quality.IsVisible = video.IsChecked == true; audioFormat.IsVisible = audio.IsChecked == true; }
        video.IsCheckedChanged += (_, _) => KindChanged();
        audio.IsCheckedChanged += (_, _) => KindChanged();
        KindChanged();

        var thumb = new Thumbs.View(120, 68);
        thumb.Set(info.Thumbnail, false, info.Duration);
        var sub = info.IsPlaylist ? $"Playlist · {info.Entries.Count} videos" : string.Join("  ·  ", new[] { info.Uploader, Fmt.Duration(info.Duration) }.Where(s => s.Length > 0));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children =
        {
            new TextBlock { Text = info.Title, Classes = { "bold" }, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(sub, "sub", "small"),
        } };
        Grid.SetColumn(text, 1);
        head.Children.AddRange(new Control[] { thumb, text });

        var cancel = Ui.Button("Cancel");
        var ok = Ui.Button("Download", Icons.Download, "accent");
        cancel.HorizontalAlignment = ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancel.HorizontalContentAlignment = ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(ok, 1);
        cancel.Margin = new Thickness(0, 0, 5, 0); ok.Margin = new Thickness(5, 0, 0, 0);
        cancel.MinHeight = ok.MinHeight = 46;
        cancel.VerticalContentAlignment = ok.VerticalContentAlignment = VerticalAlignment.Center;
        buttons.Children.AddRange(new Control[] { cancel, ok });

        var card = new Border
        {
            Classes = { "card", "sheet" }, CornerRadius = new CornerRadius(16, 16, 0, 0), Padding = new Thickness(18, 18, 18, 22), VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel { Spacing = 14, Children =
            {
                head,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { video, audio } },
                quality, audioFormat,
                Ui.Text("Saved in Download/ClipHarbor", "sub", "small"),
                buttons,
            } },
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
        _sheet.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        _sheet.Child = card;
        _sheet.IsVisible = true;
        _sheet.PointerPressed += (_, e) => { if (e.Source == _sheet) _sheet.IsVisible = false; };
        cancel.Click += (_, _) => _sheet.IsVisible = false;
        ok.Click += (_, _) =>
        {
            o.Kind = audio.IsChecked == true ? MediaKind.Audio : MediaKind.Video;
            if (o.Kind == MediaKind.Video) { o.Format = "mp4"; o.MaxHeight = heights[Math.Max(0, quality.SelectedIndex)]; }
            else o.Format = new[] { "mp3", "m4a", "opus" }[Math.Max(0, audioFormat.SelectedIndex)];
            o.TargetSizeMB = 0;
            o.Folder = AppState.DefaultFolder();
            _state.Settings.LastOptions = o.Clone();
            Queue(info, o);
            _sheet.IsVisible = false;
        };
    }

    void Queue(LinkInfo info, DownloadOptions o)
    {
        if (info.IsPlaylist)
        {
            var t = DateTime.Now; int k = 0;
            foreach (var e in info.Entries)
                _state.Items.Add(new DownloadItem
                {
                    Url = e.Url, Title = e.Title, Uploader = e.Uploader.Length > 0 ? e.Uploader : info.Uploader, ThumbnailUrl = e.Thumbnail,
                    DurationSeconds = e.Duration, Playlist = info.Title, Options = o.Clone(), Added = t.AddMilliseconds(-(k++)),
                });
        }
        else
            _state.Items.Add(new DownloadItem
            {
                Url = info.Url, Title = info.Title, Uploader = info.Uploader, ThumbnailUrl = info.Thumbnail, DurationSeconds = info.Duration,
                Options = o.Clone(), Direct = info.IsDirectFile, TotalBytes = info.DirectSize,
            });
        Changed();
    }

    // ---------------- downloads ----------------

    void OnAction(DownloadItem it, RowAction a)
    {
        if (a == RowAction.Remove)
        {
            if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
            _state.Items.Remove(it);
        }
        else if (a == RowAction.Primary)
        {
            if (it.Status is ItemStatus.Downloading or ItemStatus.Queued)
            {
                if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
                it.Status = ItemStatus.Paused;
            }
            else if (it.Status == ItemStatus.Done) { if (File.Exists(it.FilePath)) Platform.Open(it.FilePath); else Banner("That file isn't in Download/ClipHarbor anymore.", true); }
            else { it.Status = ItemStatus.Queued; it.Message = ""; }
        }
        Changed();
    }

    void Changed() { _dirty = true; _list.Rebuild(); }

    void Tick()
    {
        int max = Math.Clamp(_state.Settings.MaxConcurrent, 1, 3);
        foreach (var it in _state.Items.Where(i => i.Status == ItemStatus.Queued).OrderBy(i => i.Added).ToList())
        {
            if (_running.Count >= max) break;
            Start(it);
        }
        if (_running.Count > 0) RowView.RefreshAll();
        if (_dirty)
        {
            _dirty = false;
            try { var text = _state.Snapshot(); _ = Task.Run(() => AppState.Write(text)); } catch { _dirty = true; }
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
                _running.Remove(it.Id);
                if (canceled) { if (it.Status == ItemStatus.Downloading) it.Status = ItemStatus.Paused; }
                else if (error != null) { it.Status = ItemStatus.Failed; it.Message = error; }
                else
                {
                    it.Status = ItemStatus.Done; it.Progress = 1; it.Speed = 0; it.Phase = "";
                    if (File.Exists(it.FilePath)) AndroidSetup.Scan(it.FilePath);
                    if (_state.Settings.NotifyWhenDone && !_state.Items.Any(i => i.Status is ItemStatus.Downloading or ItemStatus.Queued))
                        Platform.Notify("Downloads finished", it.Title, IntPtr.Zero);
                }
                Changed();
            });
        });
    }
}
