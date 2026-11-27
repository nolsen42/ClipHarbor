using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;

namespace ClipHarbor;

/// <summary>The app's screen. Also appears in Android's Share menu for links, so a video can be sent
/// straight from the YouTube, X or Reddit app.</summary>
[Activity(Label = "ClipHarbor", Theme = "@style/Theme.AppCompat.DayNight.NoActionBar", Icon = "@drawable/appicon", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain", Label = "Download with ClipHarbor")]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        AndroidSetup.Init(this);
        // The phone's own font (Roboto on most phones) instead of the desktop's Inter.
        return base.CustomizeAppBuilder(builder);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SystemBars();
        HandleShare(Intent);
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        SystemBars(); // the phone switched between light and dark
    }

    /// <summary>The 3-button navigation bar otherwise stays a light strip with dark buttons, even in dark mode:
    /// let the app's background show through, with buttons that suit the phone's light or dark setting.</summary>
    void SystemBars()
    {
        if (Window is not { } w) return;
        bool dark = ((Resources?.Configuration?.UiMode ?? 0) & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
        w.SetNavigationBarColor(Android.Graphics.Color.Transparent);
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) w.NavigationBarContrastEnforced = false;
        var bars = AndroidX.Core.View.WindowCompat.GetInsetsController(w, w.DecorView);
        bars.AppearanceLightNavigationBars = !dark;
        bars.AppearanceLightStatusBars = !dark;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShare(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        PhoneView.Current.Resumed();
    }

    /// <summary>A link shared from another app ("Share → ClipHarbor").</summary>
    void HandleShare(Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend) return;
        var text = intent.GetStringExtra(Intent.ExtraText) ?? "";
        intent.SetAction(null); // don't handle it again after the screen turns
        if (text.Length > 0) PhoneView.Current.Shared(text);
    }
}
