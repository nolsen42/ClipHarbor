using Android.App;
using Android.Content;
using Android.Media;
using Android.Webkit;
using AndroidX.Core.Content;

namespace ClipHarbor;

/// <summary>Connects the shared code to Android: yt-dlp, the download folder, opening files and notices.</summary>
public static class AndroidSetup
{
    public static Context App = null!;
    public static YtDlpRunner Runner = null!;
    const string Channel = "downloads";
    static int _notifyId = 1;

    public static void Init(Activity activity)
    {
        if (Runner != null) return;
        App = activity.ApplicationContext!;
        Runner = new YtDlpRunner(App);
        Engine.Runner = Runner;
        // Download/ClipHarbor: the folder Android lets any app save any kind of file in (yt-dlp also writes .part files).
        Platform.AndroidVideosDir = () => Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)!.AbsolutePath;
        Platform.AndroidOpen = Open;
        Platform.AndroidNotify = Notify;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var nm = (NotificationManager)App.GetSystemService(Context.NotificationService)!;
            nm.CreateNotificationChannel(new NotificationChannel(Channel, Lang.T("Finished downloads"), NotificationImportance.Default));
            nm.CreateNotificationChannel(new NotificationChannel(DownloadService.Channel, Lang.T("Downloads in progress"), NotificationImportance.Low));
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && ContextCompat.CheckSelfPermission(activity, "android.permission.POST_NOTIFICATIONS") != Android.Content.PM.Permission.Granted)
            activity.RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 1);
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) && ContextCompat.CheckSelfPermission(activity, "android.permission.WRITE_EXTERNAL_STORAGE") != Android.Content.PM.Permission.Granted)
            activity.RequestPermissions(new[] { "android.permission.WRITE_EXTERNAL_STORAGE" }, 2);
    }

    /// <summary>Opens a web page in the browser, or a downloaded file in the app that plays it.</summary>
    static void Open(string target)
    {
        try
        {
            Intent intent;
            if (target.StartsWith("http://") || target.StartsWith("https://"))
                intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(target));
            else
            {
                var uri = FileProvider.GetUriForFile(App, App.PackageName + ".files", new Java.IO.File(target));
                var ext = Path.GetExtension(target).TrimStart('.').ToLowerInvariant();
                var type = MimeTypeMap.Singleton?.GetMimeTypeFromExtension(ext) ?? "*/*";
                intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(uri, type);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            }
            intent.AddFlags(ActivityFlags.NewTask);
            App.StartActivity(Intent.CreateChooser(intent, Lang.T("Open with"))!.AddFlags(ActivityFlags.NewTask));
        }
        catch { }
    }

    /// <summary>Opens Android's "Update this app?" screen for a downloaded APK. If ClipHarbor isn't allowed
    /// to install apps yet, opens that setting instead and returns false (tap Update again afterwards).</summary>
    public static bool InstallApk(string path)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && !App.PackageManager!.CanRequestPackageInstalls())
            {
                var allow = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + App.PackageName));
                App.StartActivity(allow.AddFlags(ActivityFlags.NewTask));
                return false;
            }
            var uri = FileProvider.GetUriForFile(App, App.PackageName + ".files", new Java.IO.File(path));
            var install = new Intent(Intent.ActionView);
            install.SetDataAndType(uri, "application/vnd.android.package-archive");
            install.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            App.StartActivity(install);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Android's share sheet for a downloaded file (Discord, messages, Drive...).</summary>
    public static void Share(string path)
    {
        try
        {
            var uri = FileProvider.GetUriForFile(App, App.PackageName + ".files", new Java.IO.File(path));
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var send = new Intent(Intent.ActionSend);
            send.SetType(MimeTypeMap.Singleton?.GetMimeTypeFromExtension(ext) ?? "*/*");
            send.PutExtra(Intent.ExtraStream, uri);
            send.AddFlags(ActivityFlags.GrantReadUriPermission);
            App.StartActivity(Intent.CreateChooser(send, "Share")!.AddFlags(ActivityFlags.NewTask));
        }
        catch { }
    }

    static void Notify(string title, string body)
    {
        try
        {
            var launch = App.PackageManager!.GetLaunchIntentForPackage(App.PackageName!)!;
            var tap = PendingIntent.GetActivity(App, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var b = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(App, Channel) : new Notification.Builder(App);
            b.SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone).SetContentTitle(title).SetContentText(body).SetAutoCancel(true).SetContentIntent(tap);
            ((NotificationManager)App.GetSystemService(Context.NotificationService)!).Notify(_notifyId++, b.Build());
        }
        catch { }
    }

    /// <summary>Tells Android about a finished file, so it shows up in the gallery and music apps.</summary>
    public static void Scan(string path)
    {
        try { MediaScannerConnection.ScanFile(App, new[] { path }, null, null); } catch { }
    }
}
