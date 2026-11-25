using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ClipHarbor;

/// <summary>Line icons drawn on a 24×24 grid, so they look the same on every system.</summary>
public static class Icons
{
    static Geometry G(string d) => Geometry.Parse(d);
    public static readonly Geometry Link = G("M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1.5 1.5 M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1.5-1.5");
    public static readonly Geometry Bolt = G("M13 2.5 4.5 14h7l-1 7.5L19 10h-7z");
    public static readonly Geometry Sliders = G("M4 6h9 M17 6h3 M15 4v4 M4 12h3 M11 12h9 M9 10v4 M4 18h11 M19 18h1 M17 16v4");
    public static readonly Geometry Pause = G("M8.5 5.5v13 M15.5 5.5v13");
    public static readonly Geometry Play = G("M7.5 5v14l11-7z");
    public static readonly Geometry Folder = G("M3.5 7.5a2 2 0 0 1 2-2h3.5l2 2h7.5a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z");
    public static readonly Geometry Close = G("M6.5 6.5l11 11 M17.5 6.5l-11 11");
    public static readonly Geometry Retry = G("M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.5 4.5v4.5H15");
    public static readonly Geometry List = G("M9 6.5h11 M9 12h11 M9 17.5h11 M4.5 6.5h.01 M4.5 12h.01 M4.5 17.5h.01");
    public static readonly Geometry Download = G("M12 4v11 M7 10l5 5 5-5 M5 20h14");
    public static readonly Geometry Check = G("M5 12.5l4.5 4.5L19 7.5");
    public static readonly Geometry Alert = G("M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M12 7.5v5.5 M12 16.5h.01");
    public static readonly Geometry Clock = G("M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M12 7v5l3.5 2");
    public static readonly Geometry Video = G("M3.5 7.5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-8a2 2 0 0 1-2-2z M15.5 10.5l5-3v9l-5-3");
    public static readonly Geometry Music = G("M9 17.5V5.5l11-2v12 M9 17.5a2.75 2.75 0 1 1-5.5 0a2.75 2.75 0 1 1 5.5 0z M20 15.5a2.75 2.75 0 1 1-5.5 0a2.75 2.75 0 1 1 5.5 0z");
    public static readonly Geometry Image = G("M3.5 6.5a2 2 0 0 1 2-2h13a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z M3.5 16l5-5 4 4 2.5-2.5 5.5 5 M15.5 8.5h.01");
    public static readonly Geometry Playlist =G("M4 6h12 M4 11h12 M4 16h7 M15 13.5v7l5.5-3.5z");
    public static readonly Geometry Star = G("M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.8L12 16.8l-5.2 2.8 1-5.8-4.3-4.1 5.9-.8z");
    public static readonly Geometry Trash = G("M4.5 7h15 M10 11v6 M14 11v6 M6.5 7l1 12a2 2 0 0 0 2 2h5a2 2 0 0 0 2-2l1-12 M9.5 7V4h5v3");
    public static readonly Geometry Plus = G("M12 5v14 M5 12h14");
    public static readonly Geometry Edit = G("M4.5 19.5h4l10-10-4-4-10 10z M13 7l4 4");
    public static readonly Geometry Clear = Trash;
}

/// <summary>A line icon that takes its colour from the text around it.</summary>
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    public static readonly StyledProperty<double> StrokeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Stroke), 1.8);

    static Icon() => AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeProperty);

    public Icon() { Width = 16; Height = 16; IsHitTestVisible = false; }
    public Icon(Geometry data, double size = 16) : this() { Data = data; Width = Height = size; }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        if (Data == null || Foreground == null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var m = Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - 24 * s) / 2, (Bounds.Height - 24 * s) / 2);
        using (ctx.PushTransform(m))
            ctx.DrawGeometry(null, new Pen(Foreground, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Data);
    }
}

/// <summary>The ClipHarbor logo: a rounded gradient square with a download arrow.</summary>
public class Logo : Control
{
    public Logo() { Width = Height = 32; }

    public static readonly LinearGradientBrush Gradient = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x2F, 0x6F, 0xEB), 0), new GradientStop(Color.FromRgb(0x7A, 0x4C, 0xE6), 1) },
    };

    public override void Render(DrawingContext ctx)
    {
        double w = Math.Min(Bounds.Width, Bounds.Height), cx = w / 2;
        ctx.DrawRectangle(Gradient, null, new Rect(0, 0, w, w), w * 0.26, w * 0.26);
        var pen = new Pen(Brushes.White, w * 0.11, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        ctx.DrawLine(pen, new Point(cx, w * 0.22), new Point(cx, w * 0.62));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - w * 0.18, w * 0.45), false);
            c.LineTo(new Point(cx, w * 0.63));
            c.LineTo(new Point(cx + w * 0.18, w * 0.45));
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
        ctx.DrawLine(pen, new Point(w * 0.25, w * 0.78), new Point(w - w * 0.25, w * 0.78));
    }
}

/// <summary>Downloads and caches video thumbnails.</summary>
public static class Thumbs
{
    static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();
    static readonly ConcurrentQueue<string> Order = new();
    static readonly SemaphoreSlim Gate = new(4);
    const int MaxKept = 300; // about 70 MB of pictures at most; older ones are fetched again if needed
    public static event Action? Loaded;

    public static Bitmap? Get(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Cache.TryGetValue(url, out var img)) return img;
        Cache[url] = null;
        Order.Enqueue(url);
        while (Cache.Count > MaxKept && Order.TryDequeue(out var old)) Cache.TryRemove(old, out _);
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync();
            try
            {
                var bytes = await Engine.Http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                Cache[url] = Bitmap.DecodeToWidth(ms, 320);
                Dispatcher.UIThread.Post(() => Loaded?.Invoke());
            }
            catch { }
            finally { Gate.Release(); }
        });
        return null;
    }

    /// <summary>A rounded 16:9 picture: the thumbnail, or a coloured placeholder until it arrives.</summary>
    public class View : Border
    {
        readonly Image _img = new() { Stretch = Stretch.UniformToFill };
        readonly Icon _glyph = new() { Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), Width = 22, Height = 22 };
        readonly Border _badge = new() { Classes = { "badge" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 5, 4) };
        readonly TextBlock _duration = new() { Foreground = Brushes.White, FontSize = 11 };
        string _url = "";

        public View(double width, double height, double radius = 6)
        {
            Width = width; Height = height;
            CornerRadius = new CornerRadius(radius);
            ClipToBounds = true;
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x7E, 0xA6, 0xF3), 0), new GradientStop(Color.FromRgb(0x6A, 0x5A, 0xE6), 1) },
            };
            _badge.Child = _duration;
            Child = new Panel { Children = { _glyph, _img, _badge } };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Thumbs.Loaded += Reload;
            Reload();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Thumbs.Loaded -= Reload;
        }

        void Reload() { if (_img.Source == null) _img.Source = Get(_url); }

        public void Set(string url, bool audio, double duration)
        {
            _url = url;
            _glyph.Data = audio ? Icons.Music : Icons.Play;
            _img.Source = Get(url);
            _badge.IsVisible = duration > 0;
            _duration.Text = Fmt.Duration(duration);
        }
    }
}

public static class Ui
{
    public static TextBlock Text(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    /// <summary>A button with an optional icon in front of the text.</summary>
    public static Button Button(string text, Geometry? icon = null, string cls = "flat")
    {
        var b = new Button();
        b.Classes.Add(cls);
        b.Content = Content(text, icon);
        return b;
    }

    public static object Content(string text, Geometry? icon)
    {
        if (icon == null) return text;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new Icon(icon));
        if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    public static Button IconButton(Geometry icon, string tip)
    {
        var b = new Button { Content = new Icon(icon, 17) };
        b.Classes.Add("icon");
        ToolTip.SetTip(b, tip);
        return b;
    }

    public static KeyModifiers CommandKey(TopLevel? t) =>
        t?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? (Platform.IsMac ? KeyModifiers.Meta : KeyModifiers.Control);
}

/// <summary>Base for the app's small dialogs: fixed size, centred on the main window, Esc closes.</summary>
public class Dialog : Window
{
    public Dialog(string title)
    {
        Title = title;
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
#if !ANDROID // (phones have no windows; the Android app doesn't use these dialogs)
        Icon = MainWindow.AppIcon;
#endif
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
        // Give the content the window's colour too, so screenshots of it aren't see-through.
        Opened += (_, _) => { if (Content is Panel p && p.Background == null) p[!Panel.BackgroundProperty] = this[!BackgroundProperty]; };
    }

    protected static StackPanel Buttons(params Control[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    public static Task Info(Window owner, string title, string text) => Ask(owner, title, text, "OK", null);

    public static async Task<bool> Ask(Window owner, string title, string text, string yes = "Yes", string? no = "No")
    {
        var d = new Dialog(title);
        var ok = Ui.Button(yes, null, "accent");
        ok.MinWidth = 90;
        ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => d.Close(true);
        var buttons = Buttons(ok);
        if (no != null)
        {
            var cancel = Ui.Button(no);
            cancel.MinWidth = 90;
            cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
            cancel.Click += (_, _) => d.Close(false);
            buttons.Children.Insert(0, cancel);
        }
        d.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Spacing = 20,
            Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, buttons },
        };
        d.Opened += (_, _) => ok.Focus();
        return await d.ShowDialog<bool?>(owner) == true;
    }
}

public class PromptDialog : Dialog
{
    readonly TextBox _box = new() { Width = 520 };
    public string Value => _box.Text ?? "";

    public PromptDialog(string title, string label, string value) : base(title)
    {
        _box.Text = value;
        var ok = Ui.Button("OK", null, "accent"); ok.MinWidth = 100; ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        var cancel = Ui.Button("Cancel"); cancel.MinWidth = 100; cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Close(true); };
        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 10,
            Children = { new TextBlock { Text = label }, _box, new Border { Height = 6 }, Buttons(cancel, ok) },
        };
        Opened += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}
