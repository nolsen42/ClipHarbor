using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ClipHarbor;

public partial class App : Application
{
    MainWindow? _window;
    TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shot = Program.ScreenshotPath;
            // In screenshot mode use a fresh, empty state so the picture is predictable and nothing is changed.
            var state = shot != null ? new AppState() : AppState.Load();
            if (shot != null)
                foreach (var o in new[] { state.Settings.SmartOptions, state.Settings.LastOptions }) o.Folder = AppState.DefaultFolder();
            ApplyTheme(state.Settings.Theme);

            // The window decides itself when the app ends (Quit), so hiding it to the tray keeps the app running.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = new MainWindow(state, shot, Program.Links, () => desktop.Shutdown());
            // Sign-out or shutdown (Windows, macOS, and X11 session managers such as KDE's): let it happen.
            // (Quitting from the app calls Shutdown() directly, which doesn't raise this.)
            desktop.ShutdownRequested += (_, _) => _window.EndForSessionEnd();
            if (shot == null)
            {
                MakeTray();
                SingleInstance.Listen(lines => Dispatcher.UIThread.Post(() => OnHandOff(lines)));
                if (state.Settings.StartAtLogin) StartAtLogin.Set(true); // keeps the entry right if the app was moved
                _ = Task.Run(BrowserLink.Register); // clipharbor:// links from the browser open this app
                // macOS: clicking the Dock icon while the window is hidden brings it back,
                // and clipharbor:// links arrive here instead of on the command line.
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime act)
                    act.Activated += (_, e) =>
                    {
                        if (e.Kind == ActivationKind.Reopen) _window.ShowFromTray();
                        else if (e is ProtocolActivatedEventArgs { Uri: { } uri } && BrowserLink.IsLink(uri.OriginalString))
                            Dispatcher.UIThread.Post(() => OnHandOff(new[] { uri.OriginalString }));
                    };
            }
            if (Program.StartHidden && shot == null) _window.StartInBackground();
            else desktop.MainWindow = _window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    void OnHandOff(string[] lines)
    {
        if (_window == null) return;
        var links = lines.Where(l => Engine.IsUrl(l) || BrowserLink.IsLink(l)).ToList();
        if (lines.Contains(SingleInstance.Show) || links.Count > 0) _window.ShowFromTray();
        if (links.Count > 0) _ = _window.HandleIncoming(links);
    }

    void MakeTray()
    {
        var open = new NativeMenuItem("Open ClipHarbor");
        var check = new NativeMenuItem("Check subscriptions now");
        var quit = new NativeMenuItem("Quit ClipHarbor");
        open.Click += (_, _) => _window?.ShowFromTray();
        check.Click += (_, _) => _window?.CheckSubscriptionsNow();
        quit.Click += (_, _) => _ = _window?.QuitAsync();
        _tray = new TrayIcon
        {
            Icon = MainWindow.AppIcon,
            ToolTipText = "ClipHarbor",
            Menu = new NativeMenu { Items = { open, check, new NativeMenuItemSeparator(), quit } },
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => _window?.ShowFromTray();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    /// <summary>Short status shown when hovering the tray icon.</summary>
    public static void SetTrayText(string text)
    {
        if (Current is App { _tray: { } t }) t.ToolTipText = text.Length > 120 ? text[..120] : text;
    }

    public static void ApplyTheme(string name)
    {
        if (Current == null) return;
        Current.RequestedThemeVariant = name switch
        {
            "Dark" => ThemeVariant.Dark,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Light,
        };
    }
}
