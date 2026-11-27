# ClipHarbor

A free, open-source video and audio downloader for Windows, macOS, Linux and Android. No download caps, no playlist limits, no account.

ClipHarbor is free and stays free. If it's useful to you, you can [support it on Ko-fi](https://ko-fi.com/nolsen10853).

![ClipHarbor main window](docs/screenshot.png)

## Features

- **Paste Link**: copy a link, press the button (or Ctrl+V, ⌘V on a Mac), choose your options and download. You can also drag links onto the window or pass them on the command line.
- **Video or audio only**: MP4, MKV or WEBM video up to the best quality the site offers (4K/8K included), or MP3, M4A, OPUS, FLAC or WAV audio.
- **GIF and animated WebP**: turn short clips (like videos on X/Twitter that were meant to be GIFs) into looping GIF or WebP files, in small, medium, large or full size. You can also right-click any finished video and choose **Make a GIF** or **Make an animated WebP**.
- **Only the part you want**: type a start and end time and only that stretch is downloaded, cut exactly, which is much quicker for a moment from a long video or stream. Works for GIFs too.
- **Fit to a size**: pick a size limit (20 MB for Discord, 50 MB with Nitro Basic or in a boosted server, 100 MB in a level 3 server, 500 MB with Nitro) and the finished video is re-encoded to fit, keeping it as sharp as the size allows. Right-click any finished video and choose **Make smaller** to make a smaller copy.
- **SponsorBlock**: in Settings, choose to cut out or mark as chapters the sponsor segments, self-promotion and "like and subscribe" reminders in YouTube videos, using the community-made [SponsorBlock](https://sponsor.ajay.app) list.
- **Send from your browser**: right-click a video, a link or an X post and choose **Download with ClipHarbor** or **Make a GIF with ClipHarbor** (see [below](#send-from-your-browser)).
- **Whole playlists and channels**: pick which videos you want, optionally saved into a folder named after the playlist.
- **Subtitles**: choose languages, saved as files or embedded in the video.
- **Smart Mode**: save your choices once and every link afterwards downloads in one click.
- **Subscriptions**: follow a channel or playlist and new videos download automatically. ClipHarbor keeps running in the system tray (menu bar on a Mac) after you close the window, so checks keep happening, and it can start hidden when you sign in to your computer.
- **Download list**: progress, speed and time left. Pause, resume, retry, open the file or show it in its folder. You can search and filter the list.
- **Settings**: light, dark or system look, how many downloads run at once, speed limit, proxy, file-name template, browser sign-in for private or age-restricted videos, and embedded cover art and metadata.

![Dark look](docs/screenshot-dark.png)

## Download

Get the latest version from the **[Releases page](../../releases/latest)** and download the file for your computer:

| System | File | How to start it |
| --- | --- | --- |
| Windows (64-bit) | `ClipHarbor-windows-x64-setup.exe` | Run it to install ClipHarbor for your account (no admin needed). It adds ClipHarbor to the Start menu and to Settings → Apps for uninstalling. |
| Windows, portable | `ClipHarbor-windows-x64.exe` | Run it from anywhere. Nothing is installed. |
| Mac with Apple chip (M1 and later) | `ClipHarbor-macos-apple-silicon.zip` | Unzip and move **ClipHarbor** to Applications. |
| Mac with Intel chip | `ClipHarbor-macos-intel.zip` | Same as above. |
| Linux (most PCs) | `ClipHarbor-linux-x64.AppImage` | Allow it to run (right-click → Properties → "Allow executing as program", or `chmod +x ClipHarbor-linux-x64.AppImage`) and double-click it. |
| Linux on ARM (e.g. Raspberry Pi 4/5) | `ClipHarbor-linux-arm64.AppImage` | Same as above. |
| Linux with Flatpak | [`clipharbor.flatpakref`](https://nolsen42.github.io/ClipHarbor/clipharbor.flatpakref) | Open it with Discover or GNOME Software, or run `flatpak install --user https://nolsen42.github.io/ClipHarbor/clipharbor.flatpakref`. Updates come with your other apps. Not on Flathub; see [packaging/flatpak](packaging/flatpak/README.md). |

**Android (phones with a 64-bit ARM chip, Android 7 or newer):** download `ClipHarbor-android.apk` on your phone and open it. Android asks to allow installing apps from your browser or file manager the first time. ClipHarbor isn't in the Google Play Store, which doesn't allow YouTube downloaders. To download, press **Share** in YouTube, X, Reddit or any other app and pick **ClipHarbor**, or paste a link. Files are saved in **Download/ClipHarbor**. Long-press a finished download to share it, make a GIF, or make it small enough for Discord. ClipHarbor offers its own updates; Android asks before installing them. Reading a link takes a few seconds on a phone (longer on older ones), since yt-dlp has to start up and solve YouTube's checks each time.
**Linux without AppImage:** the `.tar.gz` files have the same program. Unpack (`tar -xzf ClipHarbor-linux-x64.tar.gz`) and run `ClipHarbor/ClipHarbor`. If the AppImage doesn't start, run it once from a terminal with `--appimage-extract-and-run` after its name; that works even where AppImages can't be opened directly. To add it to your app menu, use [Gear Lever](https://flathub.org/apps/it.mijorus.gearlever) or AppImageLauncher.

**Updates:** ClipHarbor checks this repository's Releases when it starts and once a day. When there's a newer version it shows a banner; press **Update now** to see what's new, and it downloads the update, checks it against the checksum GitHub publishes, and restarts. You can turn the automatic check off, or check by hand, under **Settings → About ClipHarbor**. If ClipHarbor can't replace itself where it's installed (for example in Program Files), it opens the download page instead. The Flatpak doesn't update itself: Discover, GNOME Software or `flatpak update` installs new versions, like your other apps.

Want to try the newest changes before they're released? Every update is also built by GitHub Actions: open the **Actions** tab, pick the latest **Build** run and download your system's file from **Artifacts** (it comes wrapped in an extra `.zip`).

**First start on a Mac:** ClipHarbor isn't signed with a paid Apple developer account, so macOS will say it can't check the app. Open **System Settings → Privacy & Security**, scroll down and press **Open Anyway** next to ClipHarbor. You only need to do this once.

**On Linux** the "show a notice when downloads finish" option uses `notify-send`, installing the converter uses `tar` with xz support, and dates are shown in your local format with the `libicu` library. All three come with almost every desktop Linux.

## Send from your browser

The **Send to ClipHarbor** browser extension adds two choices to the right-click menu: **Download with ClipHarbor** and **Make a GIF with ClipHarbor**. Right-click a video, a link to one, or anywhere on a page. On X/Twitter, right-click anywhere on a post (even in your timeline) and that post is sent. The toolbar button sends the page you're on, and **Alt+Shift+D** / **Alt+Shift+G** do the same from the keyboard.

ClipHarbor must have been started at least once, so your computer knows it opens `clipharbor://` links. The first time, the browser asks whether to open ClipHarbor; tick "always allow" to skip that question afterwards. With **Smart Mode** on, the download starts straight away; otherwise you get the usual choices first.

**Installing the extension:**

- **Firefox (140 or newer):** open **[ClipHarbor-extension-firefox.xpi](../../releases/latest/download/ClipHarbor-extension-firefox.xpi)** in Firefox and press **Add**. It's signed by Mozilla, so it stays installed, and it updates itself when a new version comes out.
- **Chrome, Edge, Brave, Vivaldi, Opera:** install **[Send to ClipHarbor from the Chrome Web Store](https://chromewebstore.google.com/detail/send-to-clipharbor/lpakdanjdcpnbnllbkldcgomomakliol)**. It updates itself. The Chrome Web Store doesn't allow YouTube downloads, so the store version doesn't send YouTube pages (every other site works). For YouTube too, install the copy from GitHub instead: download `ClipHarbor-extension-chrome.zip` from the [Releases page](../../releases/latest) and unzip it, open the extensions page (`chrome://extensions`), turn on **Developer mode**, press **Load unpacked** and pick the unzipped folder. That copy doesn't update itself. Or simply copy the YouTube link and press **Paste Link** in ClipHarbor.

**No extension?** Make a bookmark with this as its address, and click it on a video page:

```
javascript:location.href='clipharbor://download?url='+encodeURIComponent(location.href)
```

Use `clipharbor://gif?url=` instead for a GIF.

The extension only works on the page you send, and only when you ask. It doesn't read your browsing or send anything anywhere except to ClipHarbor on your own computer. See the [privacy policy](PRIVACY.md).

## Download engine

ClipHarbor downloads from video sites with two free, open-source tools that are **not included** in this repository:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) (Unlicense), which reads and downloads from video sites
- [FFmpeg](https://ffmpeg.org/) (LGPL/GPL), which joins high-quality video and audio and converts formats. ClipHarbor uses the builds from [yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds) on Windows and Linux, and from [ffmpeg.martin-riedl.de](https://ffmpeg.martin-riedl.de/) on macOS.

The first time you run ClipHarbor, press **Install engine**. The app downloads both tools for your system (about 100–200 MB) into:

| System | Folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\ClipHarbor\tools` |
| macOS | `~/Library/Application Support/ClipHarbor/tools` |
| Linux | `~/.local/share/ClipHarbor/tools` |

The Flatpak comes with both, so there's nothing to install. Its yt-dlp still updates itself daily, in `~/.var/app/io.github.nolsen42.ClipHarbor/data/ClipHarbor/tools`.

If `yt-dlp` and `ffmpeg` are already installed (for example with Homebrew, apt or winget), or you point to them in Settings, ClipHarbor uses those instead. Direct links to media files (for example `.mp4` or `.mp3`) work without the engine.

## Building

You need the free [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows, macOS or Linux. The interface is built with [Avalonia](https://avaloniaui.net/) (MIT), which NuGet downloads automatically.

```
dotnet build -c Release
dotnet run -c Release
```

To make a single program file that runs without .NET installed, pick your system's ID (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64` or `linux-arm64`):

```
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

GitHub Actions (see `.github/workflows/build.yml`) builds all five versions, wraps the Mac ones as a `.app` and the Linux ones as an AppImage (with [appimagetool](https://github.com/AppImage/appimagetool)) and a `.tar.gz`, and checks that the app starts on each system. For releases it also builds the Flatpak and publishes it in ClipHarbor's own Flatpak repository on the website (see [packaging/flatpak](packaging/flatpak/README.md)). It also zips the browser extension, and for releases has Mozilla sign the Firefox copy when the repository secrets `AMO_JWT_ISSUER` and `AMO_JWT_SECRET` hold API keys from [addons.mozilla.org](https://addons.mozilla.org/developers/addon/api/key/).

### Publishing a release

Open the **Actions** tab, pick **Build**, press **Run workflow**, choose **patch** (fixes), **minor** (new features) or **major**, and press the green **Run workflow** button. The build works out the next version from the last release (for example v1.1.0 → v1.1.1), builds everything, writes the release notes from the commit titles since the last release, and publishes the release with all the files attached. Publishing a release by hand on the Releases page still works too. Choosing **flatpak** instead builds and publishes only the Flatpak for the latest release, without making a new one.

The button needs a repository secret named `RELEASE_TOKEN` (set up once). GitHub doesn't let a workflow's built-in token create a release tag when the commits being released changed the workflow file, so the release step uses this token instead:

1. On GitHub, open your profile picture → **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**.
2. Give it a name and an expiry. Under **Repository access** choose **Only select repositories** and pick this repository.
3. Under **Permissions → Repository permissions**, set **Contents** and **Workflows** to **Read and write**.
4. Generate it, copy it, and add it in this repository under **Settings → Secrets and variables → Actions → New repository secret** with the name `RELEASE_TOKEN`.

When the token expires, make a new one the same way and replace the secret.

**Android signing key (set up once, keep it forever).** Phones only install an update over ClipHarbor when it's signed with the same key as before, so releases need one permanent key. Make it with the `keytool` that comes with Java (it asks for a password; use a long one and save it in your password manager):

```
keytool -genkeypair -v -keystore clipharbor-release.keystore -alias clipharbor -keyalg RSA -keysize 4096 -validity 36500
```

Then add three repository secrets: `ANDROID_KEYSTORE` (the file as base64; in PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("clipharbor-release.keystore"))`), `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_ALIAS` (`clipharbor`). **Back up the keystore file and its password somewhere safe and never commit it**: if it's lost, existing installs can't be updated and everyone has to uninstall and reinstall.

**Flatpak signing key (set up once, keep it forever).** Flatpak only installs updates signed with the key people's computers already trust. The repository secret `FLATPAK_GPG_KEY` holds it; [packaging/flatpak](packaging/flatpak/README.md) shows how to make it. Without the secret, releases skip publishing the Flatpak. **Back the key up somewhere safe and never commit it**: if it's lost, everyone has to remove the ClipHarbor repository and add it again. Publishing the Flatpak also needs **Settings → Pages → Source** set to **GitHub Actions**, since the website workflow (`.github/workflows/pages.yml`) puts the repository online together with `docs/`.

### Building for Android

Install the Android tools once with `dotnet workload install android` (as administrator on Windows), then build the APK with `dotnet build android/ClipHarbor.Android.csproj -c Release`. The project shares the engine, models and look with the desktop app; Android-only code is in `android/`. yt-dlp, Python and ffmpeg come from [youtubedl-android](https://github.com/yausername/youtubedl-android).

### Screenshot option

`ClipHarbor --screenshot out.png` starts the app without taking focus, saves a picture of the window and exits. Automated checks use this option.

## Where your data is kept

Your download list, subscriptions and settings are saved in `state.json` in:

| System | Folder |
| --- | --- |
| Windows | `%APPDATA%\ClipHarbor` |
| macOS | `~/Library/Application Support/ClipHarbor` |
| Linux | `~/.config/ClipHarbor` |
| Linux (Flatpak) | `~/.var/app/io.github.nolsen42.ClipHarbor/config/ClipHarbor` |

By default, downloads go to a `ClipHarbor` folder inside your Videos folder (Movies on a Mac).

## Download responsibly

Only download content you have the right to download. Respect copyright and each website's terms of service. ClipHarbor is an independent project. It is not affiliated with or endorsed by any video website or any other download software.

## License

[MIT](LICENSE). The download tools named above, and Avalonia, have their own licenses.
