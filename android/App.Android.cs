using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ClipHarbor;

/// <summary>The Android side of App.axaml (shared colours and styles): one phone screen instead of windows and a tray.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Default; // follow the phone's light or dark setting
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = PhoneView.Current;
        base.OnFrameworkInitializationCompleted();
    }
}
