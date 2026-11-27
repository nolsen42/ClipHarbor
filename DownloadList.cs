using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ClipHarbor;

public enum RowAction { None, Primary, Folder, Remove }

/// <summary>Scrollable list of downloads, with an empty-state message.</summary>
public class DownloadList : Panel
{
    public List<DownloadItem> Source = new();
    public Func<DownloadItem, bool> Filter = _ => true;
    public string Search = "";
    public event Action<DownloadItem, RowAction>? ActionClicked;
    public event Action<DownloadItem>? Opened;
    public event Action? MenuRequested;

    readonly ObservableCollection<DownloadItem> _view = new();
    public readonly ListBox List = new() { SelectionMode = SelectionMode.Multiple };
    readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 10, Margin = new Thickness(40, 0, 40, 60) };
    readonly TextBlock _emptyTitle = Ui.Text("", "title");
    readonly TextBlock _emptyHint = Ui.Text("", "sub");

    public string EmptyTitle { set => _emptyTitle.Text = value; }
    public string EmptyHint { set => _emptyHint.Text = value; }

    public DownloadList()
    {
        List.Classes.Add("plain");
        List.ItemsSource = _view;
        List.Padding = new Thickness(12, 8, 12, 8);
        List.ItemTemplate = new FuncDataTemplate<DownloadItem>((_, _) => new RowView(this));
        List.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) != null) return;
            if (List.SelectedItem is DownloadItem it) Opened?.Invoke(it);
        };
        List.ContextRequested += (_, e) => { if (List.SelectedItems?.Count > 0) { MenuRequested?.Invoke(); e.Handled = true; } };

        var circle = new Border
        {
            Classes = { "empty" }, Width = 96, Height = 96, CornerRadius = new CornerRadius(48), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Icon(Icons.Download, 40) { Classes = { "brand" }, Stroke = 1.6 },
            Margin = new Thickness(0, 0, 0, 8),
        };
        _emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.TextAlignment = TextAlignment.Center;
        _emptyHint.TextWrapping = TextWrapping.Wrap;
        _emptyHint.MaxWidth = 460;
        _empty.Children.AddRange(new Control[] { circle, _emptyTitle, _emptyHint });
        Children.Add(List);
        Children.Add(_empty);
    }

    public IEnumerable<DownloadItem> Selected => List.SelectedItems?.OfType<DownloadItem>().ToList() ?? new List<DownloadItem>();

    internal void RaiseAction(DownloadItem it, RowAction a) => ActionClicked?.Invoke(it, a);

    /// <summary>Re-applies filter and search, keeping scroll position and selection where possible.</summary>
    public void Rebuild()
    {
        var q = Search.Trim();
        var next = Source.Where(Filter)
            .Where(i => q.Length == 0 || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Url.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Uploader.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Playlist.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Added).ToList();
        for (int i = 0; i < next.Count; i++)
        {
            if (i < _view.Count && ReferenceEquals(_view[i], next[i])) continue;
            int j = -1;
            for (int k = i + 1; k < _view.Count; k++) if (ReferenceEquals(_view[k], next[i])) { j = k; break; }
            if (j >= 0) _view.Move(j, i); else _view.Insert(i, next[i]);
        }
        while (_view.Count > next.Count) _view.RemoveAt(_view.Count - 1);
        _empty.IsVisible = _view.Count == 0;
        RowView.RefreshAll();
    }

    public static string Tooltip(DownloadItem it, RowAction a) => a switch
    {
        RowAction.Primary => it.Status switch
        {
            ItemStatus.Downloading or ItemStatus.Queued => "Pause",
            ItemStatus.Done => "Play",
            ItemStatus.Paused => "Resume",
            _ => "Try again",
        },
        RowAction.Folder => "Show in " + Platform.FileManagerName,
        RowAction.Remove => "Remove from list",
        _ => ""
    };
}

/// <summary>One download in the list. Refreshed in place while it runs.</summary>
public class RowView : Border
{
    static readonly HashSet<RowView> Live = new();
    public static void RefreshAll() { foreach (var r in Live.ToList()) r.Update(); }

    readonly DownloadList _owner;
    readonly Thumbs.View _thumb = new(110, 62);
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _meta = new() { Classes = { "sub", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly ProgressBar _bar = new() { Classes = { "thin" }, Minimum = 0, Maximum = 1, Width = 280, VerticalAlignment = VerticalAlignment.Center };
    readonly Icon _statusIcon = new() { Width = 13, Height = 13, Stroke = 2 };
    readonly TextBlock _status = new() { Classes = { "small" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _primary = Ui.IconButton(Icons.Pause, "Pause");
    readonly Button _folder = Ui.IconButton(Icons.Folder, "Show in folder");
    readonly Button _remove = Ui.IconButton(Icons.Close, "Remove from list");
    DownloadItem? _item;

    /// <summary>Phone layout: smaller picture, a progress bar that fits the width, no folder button.</summary>
    public static bool Compact;

    public RowView(DownloadList owner)
    {
        _owner = owner;
        Classes.Add("card");
        Margin = new Thickness(0, 0, 0, 6);
        Padding = new Thickness(10, 7);
        Height = 78;
        if (Compact)
        {
            _thumb = new Thumbs.View(88, 50);
            _bar.Width = 90; _bar.MinWidth = 0; // (the theme's 200 minimum would push the status text away)
            _folder.IsVisible = false;
        }

        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
        statusRow.Children.Add(_bar);
        _statusIcon.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(_statusIcon, 1);
        Grid.SetColumn(_status, 2);
        statusRow.Children.Add(_statusIcon);
        statusRow.Children.Add(_status);

        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0), Children = { _title, _meta, statusRow } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _primary, _folder, _remove } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(_thumb);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        Child = grid;

        _primary.Click += (_, _) => { if (_item != null) _owner.RaiseAction(_item, RowAction.Primary); };
        _folder.Click += (_, _) => { if (_item != null) _owner.RaiseAction(_item, RowAction.Folder); };
        _remove.Click += (_, _) => { if (_item != null) _owner.RaiseAction(_item, RowAction.Remove); };
        ToolTip.SetTip(_folder, DownloadList.Tooltip(new DownloadItem(), RowAction.Folder));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _item = DataContext as DownloadItem;
        Update();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Live.Add(this); Update(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); Live.Remove(this); }

    static void SetClass(Control c, string cls) { foreach (var k in new[] { "good", "bad", "warn", "sub" }) c.Classes.Set(k, k == cls); }

    public void Update()
    {
        var it = _item;
        if (it == null) return;
        _thumb.Set(it.ThumbnailUrl, it.Options.Kind == MediaKind.Audio, it.DurationSeconds);
        _title.Text = Lang.Keep(it.Title.Length > 0 ? it.Title : it.Url);

        var bits = new List<string> { it.Direct ? "File" : it.Options.Describe() };
        if (it.IsClip) bits.Add($"Part {Fmt.Time(it.ClipStart)}–{Fmt.Time(it.ClipEnd)}");
        if (it.TotalBytes > 0) bits.Add(Fmt.Bytes(it.TotalBytes));
        if (it.Options.Subtitles && it.Options.Kind == MediaKind.Video) bits.Add("Subtitles");
        if (it.Uploader.Length > 0) bits.Add(it.Uploader);
        if (it.Playlist.Length > 0) bits.Add("▸ " + it.Playlist);
        _meta.Text = Lang.Join("  ·  ", bits);

        bool running = it.Status == ItemStatus.Downloading;
        _bar.IsVisible = running;
        _statusIcon.IsVisible = !running;
        _status.Margin = new Thickness(running ? 10 : 0, 0, 0, 0);
        if (running)
        {
            bool finishing = it.Phase == "processing";
            _bar.Classes.Set("finishing", finishing);
            _bar.Value = finishing ? 1 : it.Progress;
            SetClass(_status, "");
            if (finishing) _status.Text = "Finishing up…";
            else if (it.Phase == "converting")
                _status.Text = $"Making {it.Options.Format.ToUpperInvariant()}" + (it.Progress > 0 ? $"  ·  {it.Progress * 100:0}%" : "…");
            else if (it.Phase == "shrinking")
                _status.Text = $"Fitting to {it.Options.TargetSizeMB} MB" + (it.Progress > 0 ? $"  ·  {it.Progress * 100:0}%" : "…");
            else
            {
                var parts = new List<string> { it.Progress > 0 ? $"{it.Progress * 100:0}%" : "Starting…" };
                if (it.Phase == "audio") parts.Add("audio");
                if (it.Speed > 0) parts.Add(Fmt.Bytes(it.Speed) + "/s");
                if (it.Eta > 0) parts.Add(Fmt.Duration(it.Eta) + " left");
                _status.Text = Lang.Join("  ·  ", parts);
            }
        }
        else
        {
            var (text, cls, icon) = it.Status switch
            {
                ItemStatus.Queued => ("Waiting in line", "sub", Icons.Clock),
                ItemStatus.Paused => ("Paused" + (it.Progress > 0 ? $" at {it.Progress * 100:0}%" : ""), "warn", Icons.Pause),
                ItemStatus.Done => ("Downloaded  ·  " + Fmt.When(it.Added), "good", Icons.Check),
                ItemStatus.Canceled => ("Stopped", "sub", Icons.Close),
                _ => ("Failed: " + it.Message, "bad", Icons.Alert),
            };
            _status.Text = text;
            SetClass(_status, cls);
            SetClass(_statusIcon, cls);
            _statusIcon.Data = icon;
        }
        ToolTip.SetTip(_status, it.Status == ItemStatus.Failed ? it.Message : null);

        ((Icon)_primary.Content!).Data = it.Status switch
        {
            ItemStatus.Downloading or ItemStatus.Queued => Icons.Pause,
            ItemStatus.Done or ItemStatus.Paused => Icons.Play,
            _ => Icons.Retry,
        };
        _primary.Classes.Set("brand", it.Status == ItemStatus.Done);
        ToolTip.SetTip(_primary, DownloadList.Tooltip(it, RowAction.Primary));
    }
}
