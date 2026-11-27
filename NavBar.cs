using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClipHarbor;

public class NavItem
{
    public string Key = "", Text = "";
    public Geometry? Glyph;
    public bool Header;
    internal Border? View;
    internal Border? Pill;
    internal TextBlock? CountText;
}

/// <summary>Left sidebar with sections.</summary>
public class NavBar : Border
{
    public readonly List<NavItem> Items = new();
    public string SelectedKey = "all";
    public event Action<string>? Selected;
    readonly StackPanel _stack = new() { Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock _footerTop = new() { Classes = { "sub", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _footerBottom = new() { Classes = { "sub", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
    /// <summary>The version lines at the bottom were clicked.</summary>
    public event Action? FooterClicked;

    public NavBar()
    {
        Classes.Add("sidebar");
        Width = 220;
        var dock = new DockPanel();
        var footer = new Border
        {
            Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Padding = new Thickness(20, 8, 16, 14),
            Child = new StackPanel { Spacing = 2, Children = { _footerTop, _footerBottom } },
        };
        ToolTip.SetTip(footer, "Settings");
        footer.PointerPressed += (_, _) => FooterClicked?.Invoke();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = _stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Child = dock;
    }

    public void SetFooter(string top, string bottom) { _footerTop.Text = top; _footerBottom.Text = bottom; }

    public void Build()
    {
        _stack.Children.Clear();
        foreach (var it in Items)
        {
            if (it.Header)
            {
                _stack.Children.Add(new TextBlock { Text = Lang.Upper(it.Text), Classes = { "sub", "small" }, Margin = new Thickness(20, 14, 0, 6) });
                continue;
            }
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            var mark = new Border { Classes = { "navmark" }, Width = 3, Height = 17, VerticalAlignment = VerticalAlignment.Center };
            var icon = new Icon(it.Glyph!, 17) { Margin = new Thickness(7, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = it.Text, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
            it.CountText = new TextBlock { FontSize = 11 };
            it.Pill = new Border { Classes = { "pill" }, Child = it.CountText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsVisible = false };
            Grid.SetColumn(icon, 1); Grid.SetColumn(label, 2); Grid.SetColumn(it.Pill, 3);
            grid.Children.AddRange(new Control[] { mark, icon, label, it.Pill });
            var b = new Border { Classes = { "nav" }, Child = grid };
            var key = it.Key;
            b.PointerPressed += (_, _) => { SelectedKey = key; Refresh(); Selected?.Invoke(key); };
            it.View = b;
            _stack.Children.Add(b);
        }
        Refresh();
    }

    public void SetCount(string key, int count)
    {
        var it = Items.FirstOrDefault(i => i.Key == key);
        if (it?.Pill == null) return;
        it.Pill.IsVisible = count > 0;
        it.CountText!.Text = count.ToString();
    }

    public void Refresh()
    {
        foreach (var it in Items) it.View?.Classes.Set("selected", it.Key == SelectedKey);
    }
}
