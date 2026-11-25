using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        // Long-press (or a right-click) on a finished download.
        _list.MenuRequested += () => { if (_list.Selected.FirstOrDefault() is { } it) ShowItemMenu(it); };
        // A touch long-press is a "holding" gesture, not a context-menu request.
        _list.List.AddHandler(InputElement.HoldingEvent, (_, e) =>
        {
            if (e.HoldingState != HoldingState.Started) return;
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext is DownloadItem it) ShowItemMenu(it);
        }, handledEventsToo: true);
        _sheet.PointerPressed += (_, e) => { if (e.Source == _sheet) _sheet.IsVisible = false; };

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
        var top = TopLevel.GetTopLevel(this);
        if (top?.InsetsManager is not { } insets) return;
        var keyboard = top.InputPane;
        void Apply()
        {
            var safe = insets.SafeAreaPadding;
            _dock.Margin = safe;
            // The on-screen keyboard covers the bottom of the screen; lift the sheet above it.
            double kb = keyboard?.State == Avalonia.Controls.Platform.InputPaneState.Open ? keyboard.OccludedRect.Height : 0;
            _sheet.Padding = new Thickness(safe.Left, safe.Top, safe.Right, Math.Max(safe.Bottom, kb));
        }
        insets.SafeAreaChanged += (_, _) => Apply();
        if (keyboard != null) keyboard.StateChanged += (_, _) => Apply();
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
        var video = new RadioButton { Content = "Video", GroupName = "kind", IsChecked = o.Kind == MediaKind.Video };
        var audio = new RadioButton { Content = "Audio", GroupName = "kind", IsChecked = o.Kind == MediaKind.Audio };
        var gif = new RadioButton { Content = "GIF", GroupName = "kind", IsChecked = o.Kind == MediaKind.Animation, IsVisible = !info.IsPlaylist };
        var quality = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var heights = new List<int> { 0 };
        var labels = new List<string> { "Best available" };
        foreach (var h in (info.Heights.Count > 0 ? info.Heights : new List<int> { 2160, 1440, 1080, 720, 480, 360 })
                     .Select(h => h >= 2160 ? 2160 : h >= 1440 ? 1440 : h >= 1080 ? 1080 : h >= 720 ? 720 : h >= 480 ? 480 : h >= 360 ? 360 : 240).Distinct().OrderByDescending(h => h))
        {
            heights.Add(h);
            var size = info.SizeByHeight.Where(kv => kv.Key <= h).OrderByDescending(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
            labels.Add(DownloadOptions.QualityLabel(h) + (size > 0 ? "   ~" + Fmt.Bytes(size + info.AudioSize) : ""));
        }
        quality.ItemsSource = labels;
        var sel = heights.IndexOf(o.MaxHeight);
        // Not offered for this video: the best one that isn't above the usual choice.
        if (sel < 0 && o.MaxHeight > 0) sel = heights.FindIndex(v => v > 0 && v <= o.MaxHeight);
        quality.SelectedIndex = Math.Max(0, sel < 0 ? heights.IndexOf(1080) : sel);
        int shownFirst = quality.SelectedIndex;
        var audioFormat = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "MP3", "M4A", "OPUS" } };
        audioFormat.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "mp3", "m4a", "opus" }, o.Format));
        // GIF / animated WebP
        (string label, int width)[] gifSizes = { ("Small · 320 px", 320), ("Medium · 480 px", 480), ("Large · 720 px", 720) };
        var gifSize = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = gifSizes.Select(s => s.label).ToList() };
        gifSize.SelectedIndex = Math.Max(0, Array.FindIndex(gifSizes, s => s.width == o.AnimationWidth) is var gi && gi >= 0 ? gi : 1);
        var webp = new CheckBox { Content = "Animated WebP instead (smaller)", IsChecked = o.Kind == MediaKind.Animation && o.Format == "webp" };
        // Size limit: re-encode the finished video to fit, e.g. for Discord.
        var limit = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = DownloadOptions.SizeLimits.Select(s => s.mb == 0 ? "No size limit" : "Fit in " + s.label).ToList() };
        limit.SelectedIndex = Math.Max(0, Array.FindIndex(DownloadOptions.SizeLimits, s => s.mb == o.TargetSizeMB));
        // Part of the video
        bool canClip = !info.IsPlaylist && !info.IsDirectFile && info.Duration > 0;
        var clip = new CheckBox { Content = "Only part:", VerticalAlignment = VerticalAlignment.Center };
        var from = new TextBox { Text = "0:00", Width = 86, VerticalContentAlignment = VerticalAlignment.Center };
        var to = new TextBox { Text = Fmt.Time(info.Duration), Width = 86, VerticalContentAlignment = VerticalAlignment.Center };
        from.TextChanged += (_, _) => { if (from.IsFocused) clip.IsChecked = true; };
        to.TextChanged += (_, _) => { if (to.IsFocused) clip.IsChecked = true; };
        var clipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = canClip, Children = { clip, from, Ui.Text("to", "sub"), to } };

        void KindChanged()
        {
            quality.IsVisible = limit.IsVisible = video.IsChecked == true;
            audioFormat.IsVisible = audio.IsChecked == true;
            gifSize.IsVisible = webp.IsVisible = gif.IsChecked == true;
        }
        video.IsCheckedChanged += (_, _) => KindChanged();
        audio.IsCheckedChanged += (_, _) => KindChanged();
        gif.IsCheckedChanged += (_, _) => KindChanged();
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
            Child = new ScrollViewer { MaxHeight = 640, Content = new StackPanel { Spacing = 14, Children =
            {
                head,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { video, audio, gif } },
                quality, audioFormat, gifSize, webp, limit, clipRow,
                Ui.Text("Saved in Download/ClipHarbor", "sub", "small"),
                buttons,
            } } },
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
        _sheet.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        _sheet.Child = card;
        _sheet.IsVisible = true;
        cancel.Click += (_, _) => _sheet.IsVisible = false;
        ok.Click += (_, _) =>
        {
            // Part of the video: checked first, so a typo doesn't close the sheet.
            double cs = -1, ce = -1;
            if (canClip && clip.IsChecked == true)
            {
                double s = Engine.ParseTime(from.Text), e = Engine.ParseTime(to.Text);
                if (s < 0 || e < 0) { Banner("Type the times like 90, 1:30 or 1:02:03.", true); return; }
                e = Math.Min(e, info.Duration);
                if (e - s < 0.5) { Banner("The end of the part has to come after the start.", true); return; }
                if (s > 0.5 || e < info.Duration - 0.5) { cs = s; ce = e; }
            }
            o.Kind = audio.IsChecked == true ? MediaKind.Audio : gif.IsChecked == true ? MediaKind.Animation : MediaKind.Video;
            if (o.Kind == MediaKind.Video) { o.Format = "mp4"; o.TargetSizeMB = DownloadOptions.SizeLimits[Math.Max(0, limit.SelectedIndex)].mb; }
            else if (o.Kind == MediaKind.Audio) o.Format = new[] { "mp3", "m4a", "opus" }[Math.Max(0, audioFormat.SelectedIndex)];
            else { o.Format = webp.IsChecked == true ? "webp" : "gif"; o.AnimationWidth = gifSizes[Math.Max(0, gifSize.SelectedIndex)].width; }
            o.Folder = AppState.DefaultFolder();
            var download = o.Clone();
            if (o.Kind == MediaKind.Video)
            {
                download.MaxHeight = heights[Math.Max(0, quality.SelectedIndex)];
                // Only a quality picked by hand becomes the new usual one; a video that simply
                // doesn't offer the usual quality shouldn't lower it for the next one.
                if (quality.SelectedIndex != shownFirst) o.MaxHeight = download.MaxHeight;
            }
            _state.Settings.LastOptions = o.Clone();
            Queue(info, download, cs, ce);
            _sheet.IsVisible = false;
        };
    }

    void Queue(LinkInfo info, DownloadOptions o, double clipStart = -1, double clipEnd = -1)
    {
        bool part = clipStart >= 0 && clipEnd > clipStart;
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
                Url = info.Url, Title = info.Title, Uploader = info.Uploader, ThumbnailUrl = info.Thumbnail,
                DurationSeconds = part ? clipEnd - clipStart : info.Duration, ClipStart = part ? clipStart : -1, ClipEnd = part ? clipEnd : -1,
                // A part gets its own name, so it never replaces (or is skipped as) the whole video.
                FileName = part ? Engine.SafeName($"{info.Title} ({Engine.TimeForName(clipStart)}-{Engine.TimeForName(clipEnd)})") : "",
                Options = o.Clone(), Direct = info.IsDirectFile, TotalBytes = info.DirectSize,
            });
        Changed();
    }

    // ---------------- long-press menu for a finished download ----------------

    /// <summary>Share, make a GIF, make it smaller for Discord, or delete it.</summary>
    void ShowItemMenu(DownloadItem it)
    {
        if (it.Status != ItemStatus.Done || !File.Exists(it.FilePath)) return;
        bool isVideo = new[] { ".mp4", ".mkv", ".webm", ".mov", ".m4v" }.Contains(Path.GetExtension(it.FilePath).ToLowerInvariant());
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = it.Title, Classes = { "bold" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 4, 8) });
        void Add(string text, Geometry icon, Action act)
        {
            var b = Ui.Button(text, icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.MinHeight = 48;
            b.Click += (_, _) => { _sheet.IsVisible = false; act(); };
            panel.Children.Add(b);
        }
        Add("Share", Icons.Link, () => AndroidSetup.Share(it.FilePath));
        if (isVideo)
        {
            Add("Make a GIF", Icons.Image, () => AddConversion(it, new DownloadOptions { Kind = MediaKind.Animation, Format = "gif", AnimationWidth = 480 }, it.Title));
            Add("Make smaller for Discord (20 MB)", Icons.Video, () =>
            {
                if (new FileInfo(it.FilePath).Length <= 20_000_000) { Banner("This video is already under 20 MB, so it fits on Discord as it is.", false); return; }
                AddConversion(it, new DownloadOptions { Kind = MediaKind.Video, Format = "mp4", TargetSizeMB = 20 }, $"{it.Title} (20 MB)");
            });
        }
        Add("Delete the file", Icons.Trash, () =>
        {
            try { File.Delete(it.FilePath); } catch { }
            _state.Items.Remove(it);
            Changed();
        });
        var card = new Border
        {
            CornerRadius = new CornerRadius(16, 16, 0, 0), Padding = new Thickness(14, 18, 14, 22), VerticalAlignment = VerticalAlignment.Bottom, Child = panel,
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
        _sheet.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        _sheet.Child = card;
        _sheet.IsVisible = true;
    }

    /// <summary>A new list entry that converts a finished download (GIF, or a smaller copy).</summary>
    void AddConversion(DownloadItem src, DownloadOptions o, string title)
    {
        o.Folder = Path.GetDirectoryName(src.FilePath) ?? AppState.DefaultFolder();
        _state.Items.Add(new DownloadItem
        {
            Url = src.Url, Title = title, Uploader = src.Uploader, ThumbnailUrl = src.ThumbnailUrl, DurationSeconds = src.DurationSeconds,
            SourceFile = src.FilePath, Options = o,
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
