using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace ClipHarbor;

/// <summary>Keeps ClipHarbor running while downloads are in progress, so leaving the app doesn't stop them.
/// Android requires a visible notification for this; it goes away when the last download finishes.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class DownloadService : Service
{
    public const string Channel = "active";
    const int NotificationId = 1000;
    const string ExtraText = "text";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var text = intent?.GetStringExtra(ExtraText) ?? "Downloading…";
        var n = Build(this, text);
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NotificationId, n, ForegroundService.TypeDataSync);
        else StartForeground(NotificationId, n);
        return StartCommandResult.NotSticky;
    }

    static Notification Build(Context c, string text)
    {
        var launch = c.PackageManager!.GetLaunchIntentForPackage(c.PackageName!)!;
        var tap = PendingIntent.GetActivity(c, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var b = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(c, Channel) : new Notification.Builder(c);
        return b.SetSmallIcon(Android.Resource.Drawable.StatSysDownload).SetContentTitle("ClipHarbor").SetContentText(text)
            .SetOngoing(true).SetOnlyAlertOnce(true).SetContentIntent(tap).Build()!;
    }

    static bool _running;

    /// <summary>Starts or updates the service (with the text shown in its notification), or stops it with null.</summary>
    public static void Update(Context c, string? text)
    {
        try
        {
            if (text == null)
            {
                if (_running) c.StopService(new Intent(c, typeof(DownloadService)));
                _running = false;
                return;
            }
            var intent = new Intent(c, typeof(DownloadService)).PutExtra(ExtraText, text);
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) c.StartForegroundService(intent); else c.StartService(intent);
            _running = true;
        }
        catch { } // e.g. Android refusing a start while the app is in the background; downloads still run while it lives
    }
}
