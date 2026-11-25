using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ClipHarbor;

/// <summary>Choose what and how to download. Also used (without a link) to set Smart Mode / subscription choices.</summary>
public class OptionsDialog : Dialog
{
    readonly LinkInfo? _info;
    public DownloadOptions Result;
    public List<PlaylistEntry> ChosenEntries = new();
    /// <summary>The file name typed for a single link, without its ending; empty means the usual name.</summary>
    public string FileName = "";

    readonly TextBox _name = new();
    readonly TextBlock _ext = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Classes = { "sub" } };
    string _defaultName = "";

    readonly RadioButton _video = new() { Content = "Video", GroupName = "kind", IsChecked = true };
    readonly RadioButton _audio = new() { Content = "Audio only", GroupName = "kind" };
    readonly RadioButton _anim = new() { Content = "GIF / WebP", GroupName = "kind" };
    readonly CheckBox _keepVideo = new() { Content = "Also keep the video file" };
    readonly StackPanel _animArea = new() { Spacing = 2, Margin = new Thickness(LabelW, -4, 0, 0) };
    readonly StackPanel _subsArea = new() { Spacing = 14 };
    readonly ComboBox _format = new() { Width = 150 };
    readonly ComboBox _quality = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _qualityLabel = new() { Text = "Quality", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 12, 0) };
    readonly CheckBox _subs = new() { Content = "Download subtitles" };
    readonly CheckBox _embed = new() { Content = "Put them inside the video", IsChecked = true };
    readonly WrapPanel _subLangs = new();
    readonly List<CheckBox> _langBoxes = new();
    readonly TextBox _subText = new() { Width = 270, Watermark = "Languages, e.g. en, es, fr" };
    readonly TextBox _folder = new();
    readonly CheckBox _subfolder = new() { Content = "Put playlist videos in their own folder", IsChecked = true };
    readonly ListBox _entries = new() { Height = 170, SelectionMode = SelectionMode.Multiple, Classes = { "compact" }, Background = Avalonia.Media.Brushes.Transparent };
    readonly List<int> _qualityValues = new();
    bool _useLangList, _noSubs;

    /// <summary>The part of the video picked, in seconds; -1 = the whole video.</summary>
    public double ClipStart = -1, ClipEnd = -1;
    readonly CheckBox _clip = new() { Content = "Only part of it, from", VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _clipFrom = new() { Width = 86, Watermark = "0:00" };
    readonly TextBox _clipTo = new() { Width = 86 };
    readonly ComboBox _size = new() { Width = 220 };
    Control? _sizeRow;

    static readonly string[] VideoFormats = { "mp4", "mkv", "webm" };
    static readonly string[] AudioFormats = { "mp3", "m4a", "opus", "flac", "wav" };
    static readonly string[] AnimFormats = { "gif", "webp" };
    static readonly (string label, int width)[] AnimSizes = { ("Small · 320 px wide", 320), ("Medium · 480 px wide", 480), ("Large · 720 px wide", 720), ("Full size (large files)", 0) };
    const double W = 600, LabelW = 130;

    public OptionsDialog(LinkInfo? info, DownloadOptions start, string heading) : base(heading)
    {
        _info = info;
        Result = start.Clone();
        bool playlist = info?.IsPlaylist == true;
        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20), Spacing = 14, Width = W };

        root.Children.Add(Header(info, heading));

        if (playlist)
        {
            var head = new DockPanel();
            var all = new Button { Content = "Select all", Classes = { "link" } };
            var none = new Button { Content = "None", Classes = { "link" } };
            all.Click += (_, _) => SetAll(true);
            none.Click += (_, _) => SetAll(false);
            var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { all, none } };
            DockPanel.SetDock(links, Dock.Right);
            head.Children.Add(links);
            head.Children.Add(Ui.Text($"Videos in this playlist ({info!.Entries.Count})", "bold"));
            _entries.ItemTemplate = new FuncDataTemplate<PlaylistEntry?>((e, _) =>
            {
                // The list asks for an empty row while it recycles rows during scrolling.
                if (e == null) return new CheckBox();
                var cb = new CheckBox { Content = EntryText(e), IsChecked = e.Selected };
                cb.IsCheckedChanged += (_, _) => e.Selected = cb.IsChecked == true;
                return cb;
            }, supportsRecycling: false);
            _entries.ItemsSource = info.Entries;
            root.Children.Add(new StackPanel { Spacing = 6, Children = { head, new Border { Classes = { "panel" }, Child = _entries, ClipToBounds = true, CornerRadius = new CornerRadius(6) } } });
        }

        var kind = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { _video, _audio, _anim } };
        root.Children.Add(Row("Download as", kind, bold: true));

        var formatRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Grid.SetColumn(_qualityLabel, 1); Grid.SetColumn(_quality, 2);
        formatRow.Children.AddRange(new Control[] { _format, _qualityLabel, _quality });
        root.Children.Add(Row("Format", formatRow));

        _keepVideo.IsChecked = Result.KeepVideo;
        _animArea.Children.Add(_keepVideo);
        _animArea.Children.Add(Ui.Text("Loops forever. Best for short clips — long videos make very large files.", "sub", "small"));
        root.Children.Add(_animArea);

        // Size limit: the finished video is re-encoded to fit (e.g. Discord's 20 MB).
        _size.ItemsSource = DownloadOptions.SizeLimits.Select(s => s.label).ToList();
        _size.Width = 300;
        _size.SelectedIndex = Math.Max(0, Array.FindIndex(DownloadOptions.SizeLimits, s => s.mb == Result.TargetSizeMB));
        ToolTip.SetTip(_size, "The video is made smaller after it downloads so it fits, for example to send on Discord.\nShort videos stay sharp; long ones get a smaller picture.");
        _sizeRow = Row("Size limit", _size);
        root.Children.Add(_sizeRow);

        // Part of the video: a single video whose length is known.
        if (info != null && !playlist && !info.IsDirectFile && info.Duration > 0)
        {
            _clipTo.Text = Fmt.Time(info.Duration);
            _clipFrom.Text = "0:00";
            var clipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _clip, _clipFrom, Ui.Text("to", "sub"), _clipTo } };
            foreach (var c in clipRow.Children) c.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(_clip, "Downloads just that stretch, which is much quicker for a moment from a long video.\nType times like 90, 1:30 or 1:02:03.");
            // Changing a time turns the option on.
            _clipFrom.TextChanged += (_, _) => { if (_clipFrom.IsFocused) _clip.IsChecked = true; };
            _clipTo.TextChanged += (_, _) => { if (_clipTo.IsFocused) _clip.IsChecked = true; };
            root.Children.Add(Row("Part", clipRow));
            if (!Engine.HasFfmpeg)
            {
                _clip.IsEnabled = _clipFrom.IsEnabled = _clipTo.IsEnabled = false;
                ToolTip.SetTip(_clip, "Needs the converter (ffmpeg): Settings → Download engine.");
            }
        }

        root.Children.Add(_subsArea);
        var subsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _subs, _embed } };
        _subsArea.Children.Add(subsRow);
        _useLangList = info != null && !playlist && !info.IsDirectFile && (info.SubtitleLangs.Count + info.AutoSubtitleLangs.Count) > 0;
        // A single video with no subtitles listed (or a direct file): don't offer them at all.
        _noSubs = info != null && !playlist && !_useLangList;
        if (_useLangList)
        {
            foreach (var l in info!.SubtitleLangs) AddLang(l, l, Result.SubtitleLanguages.Contains(l));
            foreach (var l in info.AutoSubtitleLangs.Where(l => !info.SubtitleLangs.Contains(l))) AddLang(l + " (auto)", l, false);
            if (!_langBoxes.Any(c => c.IsChecked == true) && _langBoxes.Count > 0)
                (_langBoxes.FirstOrDefault(c => ((string)c.Tag!).StartsWith("en")) ?? _langBoxes[0]).IsChecked = true;
            _subsArea.Children.Add(new Border
            {
                Classes = { "panel" }, CornerRadius = new CornerRadius(6), Margin = new Thickness(20, -6, 0, 0), Padding = new Thickness(10, 4), MaxHeight = 96,
                Child = new ScrollViewer { Content = _subLangs },
            });
        }
        else
        {
            _subText.Text = string.Join(", ", Result.SubtitleLanguages);
            var hint = Ui.Text(info == null || playlist ? "Used whenever the video has them" : "No subtitles were listed for this video", "sub");
            _subsArea.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(20, -6, 0, 0), Children = { _subText, hint } });
        }

        // A single link can be given its own file name.
        if (info != null && !playlist)
        {
            _defaultName = Engine.SafeName(info.Title.Length > 0 ? info.Title : "download");
            if (info.IsDirectFile) _defaultName = StripMediaExt(_defaultName);
            _name.Text = _defaultName;
            var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(_ext, 1);
            nameRow.Children.AddRange(new Control[] { _name, _ext });
            root.Children.Add(Row("File name", nameRow, bold: true));
        }

        _folder.Text = Result.Folder;
        var browse = Ui.Button("Browse…");
        browse.FontWeight = FontWeight.Normal;
        browse.Click += async (_, _) =>
        {
            var start = await TryFolder(_folder.Text);
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose where downloads are saved", SuggestedStartLocation = start });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) _folder.Text = path;
        };
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(browse, 1); browse.Margin = new Thickness(10, 0, 0, 0);
        folderRow.Children.AddRange(new Control[] { _folder, browse });
        root.Children.Add(Row("Save to", folderRow, bold: true));

        if (playlist || info == null)
        {
            _subfolder.IsChecked = Result.PlaylistSubfolder;
            _subfolder.Margin = new Thickness(LabelW, -6, 0, 0);
            root.Children.Add(_subfolder);
        }

        var ok = Ui.Button(info == null ? "Save" : "Download", info == null ? Icons.Check : Icons.Download, "accent");
        var cancel = Ui.Button("Cancel");
        ok.MinWidth = 140; cancel.MinWidth = 100;
        ok.HorizontalContentAlignment = cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(false);
        var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var btns = Buttons(cancel, ok);
        DockPanel.SetDock(btns, Dock.Right);
        bottom.Children.Add(btns);
        if (!Engine.HasFfmpeg && info?.IsDirectFile != true)
            bottom.Children.Add(new TextBlock { Text = "Tip: install the converter in Settings for 1080p and above,\nMP3 and subtitles inside the video.", Classes = { "sub", "small" }, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(bottom);

        // A direct file link is saved as it is, unless it's turned into a GIF/WebP.
        if (info?.IsDirectFile == true)
        {
            _video.Content = "Keep as is";
            foreach (var c in new Control[] { _audio, _subs, _embed, _subText }) c.IsEnabled = false;
        }

        Content = root;

        _video.IsCheckedChanged += (_, _) => KindChanged();
        _audio.IsCheckedChanged += (_, _) => KindChanged();
        _anim.IsCheckedChanged += (_, _) => KindChanged();
        _subs.IsCheckedChanged += (_, _) => SubsChanged();
        _format.SelectionChanged += (_, _) => { FillQuality(); ShowExt(); };
        if (Result.Kind == MediaKind.Audio && info?.IsDirectFile != true) _audio.IsChecked = true;
        if (Result.Kind == MediaKind.Animation) _anim.IsChecked = true;
        _subs.IsChecked = Result.Subtitles;
        _embed.IsChecked = Result.EmbedSubtitles;
        KindChanged();
        SubsChanged();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && FocusManager?.GetFocusedElement() is not (ListBoxItem or CheckBox or ComboBox)) { Accept(); e.Handled = true; }
        };
    }

    async Task<IStorageFolder?> TryFolder(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : await StorageProvider.TryGetFolderFromPathAsync(path); }
        catch { return null; }
    }

    static Control Row(string label, Control content, bool bold = false)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelW},*") };
        var t = Ui.Text(label);
        if (bold) t.Classes.Add("bold");
        Grid.SetColumn(content, 1);
        g.Children.AddRange(new Control[] { t, content });
        return g;
    }

    void AddLang(string text, string code, bool on)
    {
        var cb = new CheckBox { Content = text, Tag = code, IsChecked = on, Margin = new Thickness(0, 0, 14, 0), MinWidth = 90 };
        _langBoxes.Add(cb);
        _subLangs.Children.Add(cb);
    }

    void SetAll(bool on)
    {
        foreach (var e in _info!.Entries) e.Selected = on;
        _entries.ItemsSource = null;
        _entries.ItemsSource = _info.Entries;
    }

    static Control Header(LinkInfo? i, string heading)
    {
        var thumb = new Thumbs.View(152, 86, 8);
        thumb.Set(i?.Thumbnail ?? "", false, 0);
        var bits = new List<string>();
        if (i == null) bits.Add("These choices are used every time");
        else if (i.IsPlaylist) bits.Add($"Playlist · {i.Entries.Count} videos");
        else if (i.IsDirectFile) { bits.Add("Direct file link"); if (i.DirectSize > 0) bits.Add(Fmt.Bytes(i.DirectSize)); }
        if (i != null && i.Uploader.Length > 0) bits.Add(i.Uploader);
        if (i != null && i.Duration > 0) bits.Add(Fmt.Duration(i.Duration));
        var title = new TextBlock { Text = i?.Title ?? heading, Classes = { "title" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { Text = string.Join("  ·  ", bits), Classes = { "sub" }, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new DockPanel { Margin = new Thickness(16, 2, 0, 2) };
        DockPanel.SetDock(meta, Dock.Bottom);
        text.Children.AddRange(new Control[] { meta, title });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 86 };
        Grid.SetColumn(text, 1);
        g.Children.AddRange(new Control[] { thumb, text });
        return g;
    }

    static string StripMediaExt(string name)
    {
        var e = Path.GetExtension(name);
        return e.Length > 0 && Engine.IsMediaExt(e) ? name[..^e.Length] : name;
    }

    /// <summary>Shows the ending the file will get, next to the name box.</summary>
    void ShowExt()
    {
        string ext;
        if (_info?.IsDirectFile == true && _anim.IsChecked != true)
        {
            ext = Path.GetExtension(_info.Title);
            if (!Engine.IsMediaExt(ext)) try { ext = Path.GetExtension(new Uri(_info.Url).AbsolutePath); } catch { ext = ""; }
        }
        else ext = "." + (_format.SelectedItem?.ToString() ?? "").ToLowerInvariant();
        _ext.Text = ext.Length > 1 ? ext.ToLowerInvariant() : "";
    }

    static string EntryText(PlaylistEntry e) => e.Duration > 0 ? $"{e.Title}   ({Fmt.Duration(e.Duration)})" : e.Title;

    void KindChanged()
    {
        bool video = _video.IsChecked == true, anim = _anim.IsChecked == true, direct = _info?.IsDirectFile == true;
        var list = anim ? AnimFormats : video ? VideoFormats : AudioFormats;
        _format.ItemsSource = list.Select(f => f.ToUpperInvariant()).ToList();
        int idx = Array.IndexOf(list, Result.Format);
        _format.SelectedIndex = idx >= 0 ? idx : 0;
        _qualityLabel.Text = anim ? "Size" : "Quality";
        _quality.IsVisible = _qualityLabel.IsVisible = video || anim;
        _format.IsEnabled = _quality.IsEnabled = _qualityLabel.IsEnabled = !direct || anim;
        _animArea.IsVisible = anim;
        if (_sizeRow != null) _sizeRow.IsVisible = video && !direct && Engine.HasFfmpeg;
        _subsArea.IsVisible = !anim && !_noSubs;
        _subs.IsEnabled = video && !direct;
        SubsChanged();
        FillQuality();
        ShowExt();
    }

    void SubsChanged()
    {
        bool on = _subs.IsChecked == true && _subs.IsEnabled;
        _embed.IsEnabled = on && Engine.HasFfmpeg;
        _subLangs.IsEnabled = on;
        _subText.IsEnabled = on;
    }

    void FillQuality()
    {
        _qualityValues.Clear();
        var items = new List<string>();
        if (_anim.IsChecked == true)
        {
            foreach (var (label, width) in AnimSizes) { items.Add(label); _qualityValues.Add(width); }
            _quality.ItemsSource = items;
            int i = _qualityValues.IndexOf(Result.AnimationWidth);
            _quality.SelectedIndex = i >= 0 ? i : 1;
            return;
        }
        if (_video.IsChecked != true) return;
        var heights = _info?.Heights.Count > 0
            ? _info.Heights.Where(h => h >= 144).Select(h => h >= 4320 ? 4320 : h >= 2160 ? 2160 : h >= 1440 ? 1440 : h >= 1080 ? 1080 : h >= 720 ? 720 : h >= 480 ? 480 : h >= 360 ? 360 : h >= 240 ? 240 : 144).Distinct().ToList()
            : new List<int> { 4320, 2160, 1440, 1080, 720, 480, 360 };
        items.Add("Best available"); _qualityValues.Add(0);
        foreach (var h in heights)
        {
            string size = "";
            if (_info != null)
            {
                var best = _info.SizeByHeight.Where(kv => kv.Key <= h).OrderByDescending(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
                if (best > 0) size = "   ~" + Fmt.Bytes(best + _info.AudioSize);
            }
            items.Add(DownloadOptions.QualityLabel(h) + (h >= 720 && h < 1440 ? " HD" : "") + size);
            _qualityValues.Add(h);
        }
        _quality.ItemsSource = items;
        int want = Result.MaxHeight;
        int sel = _qualityValues.IndexOf(want);
        if (sel < 0 && want > 0) sel = _qualityValues.FindIndex(v => v > 0 && v <= want);
        _quality.SelectedIndex = sel >= 0 ? sel : 0;
    }

    async void Accept()
    {
        var o = Result;
        o.Kind = _anim.IsChecked == true ? MediaKind.Animation : _audio.IsChecked == true ? MediaKind.Audio : MediaKind.Video;
        o.Format = (_format.SelectedItem?.ToString() ?? "mp4").ToLowerInvariant();
        if (o.Kind == MediaKind.Video && _quality.SelectedIndex >= 0) o.MaxHeight = _qualityValues[_quality.SelectedIndex];
        if (o.Kind == MediaKind.Video)
            o.TargetSizeMB = _info?.IsDirectFile == true || !Engine.HasFfmpeg ? 0 : DownloadOptions.SizeLimits[Math.Max(0, _size.SelectedIndex)].mb;

        ClipStart = ClipEnd = -1;
        if (_clip.IsChecked == true && _clip.IsEnabled && _info != null)
        {
            double s = Engine.ParseTime(_clipFrom.Text), e = Engine.ParseTime(_clipTo.Text);
            if (s < 0 || e < 0) { await Info(this, Title ?? "", "Type the times like 90, 1:30 or 1:02:03."); return; }
            if (e > _info.Duration + 1) { await Info(this, Title ?? "", $"The video is only {Fmt.Time(_info.Duration)} long."); return; }
            e = Math.Min(e, _info.Duration);
            if (e - s < 0.5) { await Info(this, Title ?? "", "The end has to come after the start."); return; }
            // The whole video picked: just download it normally.
            if (s > 0.5 || e < _info.Duration - 0.5) { ClipStart = s; ClipEnd = e; }
        }
        if (o.Kind == MediaKind.Animation)
        {
            if (_quality.SelectedIndex >= 0) o.AnimationWidth = _qualityValues[_quality.SelectedIndex];
            o.KeepVideo = _keepVideo.IsChecked == true;
            if (!Engine.HasFfmpeg)
            {
                await Info(this, Title ?? "", "Making GIF or WebP files needs the free converter (ffmpeg). Open Settings → Download engine and press Install or Add converter first.");
                return;
            }
        }
        if (!_noSubs) // when hidden, keep the saved subtitle choices for the next video
        {
            o.Subtitles = _subs.IsChecked == true && _subs.IsEnabled;
            o.EmbedSubtitles = _embed.IsChecked == true;
            var langs = _useLangList
                ? _langBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToList()
                : (_subText.Text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (langs.Count > 0) o.SubtitleLanguages = langs;
        }
        var folder = (_folder.Text ?? "").Trim();
        if (folder.Length == 0) folder = AppState.DefaultFolder();
        try { Directory.CreateDirectory(folder); }
        catch { await Info(this, Title ?? "", "That folder can't be used. Please pick another one."); return; }
        o.Folder = folder;
        o.PlaylistSubfolder = _subfolder.IsChecked == true;

        if (_defaultName.Length > 0)
        {
            var typed = (_name.Text ?? "").Trim();
            if (typed.Length > 0 && _ext.Text is { Length: > 0 } x && typed.EndsWith(x, StringComparison.OrdinalIgnoreCase)) typed = typed[..^x.Length].TrimEnd();
            FileName = typed.Length == 0 || typed == _defaultName ? "" : Engine.SafeName(typed);
        }

        if (_info?.IsPlaylist == true)
        {
            ChosenEntries = _info.Entries.Where(e => e.Selected).ToList();
            if (ChosenEntries.Count == 0) { await Info(this, Title ?? "", "Pick at least one video from the playlist."); return; }
        }
        Close(true);
    }
}
