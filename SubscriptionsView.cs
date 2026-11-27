using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClipHarbor;

/// <summary>Follow channels or playlists; new videos download automatically.</summary>
public class SubscriptionsView : DockPanel
{
    readonly AppState _state;
    readonly MainWindow _main;
    readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent };
    readonly TextBlock _empty = new() { Text = "No subscriptions yet. Press “Add subscription” and paste a channel or playlist link.", Classes = { "sub" }, Margin = new Thickness(16, 14), TextWrapping = TextWrapping.Wrap };
    readonly Button _add = Ui.Button("Add subscription", Icons.Plus, "accent");
    readonly Button _check = Ui.Button("Check now", Icons.Retry);
    readonly Button _edit = Ui.Button("Choices…", Icons.Edit);
    readonly Button _remove = Ui.Button("Remove", Icons.Trash);
    const string Cols = "280,130,150,*";
    bool _checking;

    public SubscriptionsView(AppState state, MainWindow main)
    {
        _state = state; _main = main;
        Classes.Add("back");

        var hint = Ui.Text("Follow a channel or playlist and new videos are downloaded for you automatically.", "sub");
        hint.TextWrapping = TextWrapping.Wrap;
        var titles = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 0), Children = { Ui.Text("Subscriptions", "title"), hint } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Children = { _add, _check, _edit, _remove } };
        var head = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.AddRange(new Control[] { buttons, titles });
        DockPanel.SetDock(head, Dock.Top);
        Children.Add(head);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(Cols), Margin = new Thickness(10, 0), Height = 36 };
        string[] names = { "Name", "Saved as", "Last checked", "Status" };
        for (int i = 0; i < names.Length; i++) { var t = Ui.Text(names[i], "sub", "bold"); t.Margin = new Thickness(6, 0); Grid.SetColumn(t, i); header.Children.Add(t); }
        var headerBar = new Border { Classes = { "bar" }, BorderThickness = new Thickness(0, 0, 0, 1), Child = header, CornerRadius = new CornerRadius(8, 8, 0, 0) };

        _list.Classes.Add("table");
        _list.ItemTemplate = new FuncDataTemplate<Subscription>((_, _) =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Cols) };
            for (int i = 0; i < 4; i++)
            {
                var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
                if (i == 0) t.FontWeight = FontWeight.SemiBold; else t.Classes.Add("sub");
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            g.DataContextChanged += (_, _) =>
            {
                if (g.DataContext is not Subscription s) return;
                ((TextBlock)g.Children[0]).Text = Lang.Keep(s.Name.Length > 0 ? s.Name : s.Url);
                ((TextBlock)g.Children[1]).Text = s.Options.Describe();
                ((TextBlock)g.Children[2]).Text = s.LastChecked == default ? "Never" : Fmt.When(s.LastChecked);
                ((TextBlock)g.Children[3]).Text = s.LastResult;
            };
            return g;
        }, supportsRecycling: false);
        _list.DoubleTapped += async (_, _) => await EditChoices();

        var table = new DockPanel();
        DockPanel.SetDock(headerBar, Dock.Top);
        table.Children.AddRange(new Control[] { headerBar, new Panel { Children = { _list, _empty } } });
        Children.Add(new Border { Classes = { "panel" }, Margin = new Thickness(20, 0, 20, 20), Child = table, ClipToBounds = true });

        _add.Click += async (_, _) => await AddSubscription();
        _check.Click += async (_, _) => await CheckAll(manual: true);
        _edit.Click += async (_, _) => await EditChoices();
        _remove.Click += (_, _) => RemoveSelected();
    }

    public void Rebuild()
    {
        var sel = Selected.ToList();
        _list.ItemsSource = null;
        _list.ItemsSource = _state.Subscriptions.ToList();
        foreach (var s in sel) _list.SelectedItems?.Add(s);
        _empty.IsVisible = _state.Subscriptions.Count == 0;
        _check.IsEnabled = !_checking && _state.Subscriptions.Count > 0;
    }

    IEnumerable<Subscription> Selected => _list.SelectedItems?.OfType<Subscription>().ToList() ?? new List<Subscription>();

    async Task AddSubscription()
    {
        if (!Engine.HasEngine)
        {
            await Dialog.Info(_main, "Subscriptions", "Subscriptions need the download engine. Open Settings and press Install engine first.");
            return;
        }
        string clip = "";
        try { if (_main.Clipboard != null) clip = (await _main.Clipboard.TryGetTextAsync() ?? "").Trim(); } catch { }
        var p = new PromptDialog("Add subscription", "Link to a channel or playlist:", Engine.IsUrl(clip) ? clip : "");
        if (await p.ShowDialog<bool?>(_main) != true) return;
        var url = p.Value.Trim();
        if (!Engine.IsUrl(url)) return;
        _add.IsEnabled = false; _add.Content = Ui.Content("Looking…", Icons.Plus);
        LinkInfo info;
        try { info = await Engine.AnalyzeAsync(url, CancellationToken.None, 50); }
        catch (Exception ex) { await Dialog.Info(_main, "Subscriptions", "Couldn't read that link: " + ex.Message); return; }
        finally { _add.IsEnabled = true; _add.Content = Ui.Content("Add subscription", Icons.Plus); }
        if (!info.IsPlaylist)
        {
            await Dialog.Info(_main, "Subscriptions", "That link is a single video. Use the link to the channel's videos page or a playlist.");
            return;
        }
        var d = new OptionsDialog(null, _state.Settings.LastOptions, "Subscribe to " + (info.Title.Length > 0 ? info.Title : "channel"));
        if (await d.ShowDialog<bool?>(_main) != true) return;
        var sub = new Subscription { Url = url, Name = info.Title.Length > 0 ? info.Title : url, Options = d.Result.Clone(), LastChecked = DateTime.Now };
        foreach (var e in info.Entries) sub.Seen.Add(e.Id);
        sub.LastResult = $"Following · {info.Entries.Count} existing videos skipped";
        _state.Subscriptions.Add(sub);
        int recent = Math.Min(5, info.Entries.Count);
        if (recent > 0 && await Dialog.Ask(_main, "Subscriptions", $"Also download the newest {recent} videos now?"))
            _main.Queue(info, sub.Options, info.Entries.Take(recent).ToList(), sub.Name);
        _main.Changed();
        Rebuild();
    }

    async Task EditChoices()
    {
        var s = Selected.FirstOrDefault();
        if (s == null) return;
        var d = new OptionsDialog(null, s.Options, "Choices for " + s.Name);
        if (await d.ShowDialog<bool?>(_main) == true) { s.Options = d.Result.Clone(); _main.Changed(); Rebuild(); }
    }

    void RemoveSelected()
    {
        var sel = Selected.ToList();
        if (sel.Count == 0) return;
        foreach (var s in sel) _state.Subscriptions.Remove(s);
        _main.Changed(); Rebuild();
    }

    public Task CheckNow() => CheckAll(manual: true);

    public async Task AutoCheck()
    {
        var hours = _state.Settings.SubscriptionCheckHours;
        if (hours <= 0 || !Engine.HasEngine) return;
        if (_state.Subscriptions.Any(s => s.Enabled && (DateTime.Now - s.LastChecked).TotalHours >= hours))
            await CheckAll(manual: false);
    }

    async Task CheckAll(bool manual)
    {
        if (_checking || !Engine.HasEngine) return;
        _checking = true; _check.IsEnabled = false; _check.Content = Ui.Content("Checking…", Icons.Retry);
        try
        {
            foreach (var s in _state.Subscriptions.ToList())
            {
                if (!s.Enabled) continue;
                if (!manual && (DateTime.Now - s.LastChecked).TotalHours < _state.Settings.SubscriptionCheckHours) continue;
                s.LastResult = "Checking…"; Rebuild();
                try
                {
                    var info = await Engine.AnalyzeAsync(s.Url, CancellationToken.None, 30);
                    var fresh = info.Entries.Where(e => !s.Seen.Contains(e.Id)).ToList();
                    foreach (var e in fresh) s.Seen.Add(e.Id);
                    if (fresh.Count > 0) _main.Queue(info, s.Options, fresh, s.Name);
                    s.LastResult = fresh.Count == 1 ? "1 new video added" : fresh.Count > 0 ? $"{fresh.Count} new videos added" : "Up to date";
                }
                catch (Exception ex) { s.LastResult = "Problem: " + ex.Message; }
                s.LastChecked = DateTime.Now;
                Rebuild();
            }
        }
        finally
        {
            _checking = false; _check.Content = Ui.Content("Check now", Icons.Retry);
            Rebuild(); _main.Changed();
        }
    }
}
