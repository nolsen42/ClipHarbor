# Flatpak

ClipHarbor's Flatpak is published in its own Flatpak repository on the website (GitHub Pages), not on Flathub. Linux users add it once and then get updates through Discover, GNOME Software or `flatpak update`:

```sh
flatpak install --user https://nolsen42.github.io/ClipHarbor/clipharbor.flatpakref
```

Each release also has one-file installers, `ClipHarbor-linux-x64.flatpak` and `ClipHarbor-linux-arm64.flatpak`. Those don't update themselves.

## Files

- `io.github.nolsen42.ClipHarbor.yml` is the build recipe (manifest). It builds ffmpeg and ClipHarbor from source and includes yt-dlp.
- `.desktop`, `.metainfo.xml` and `.png` are the menu entry, the store listing and the icon.
- `nuget-sources.json` lists the NuGet packages, so the build runs without internet. Make it again (step 2 below) whenever a package version in `ClipHarbor.csproj` changes.

In a Flatpak, ClipHarbor notices that it's in the sandbox (`Platform.IsFlatpak`):
- Its own app updater is turned off; the Flatpak repository updates the app.
- It sends notices and sets start-at-login through the desktop portals.
- yt-dlp comes with the app. On the first start it is copied to the app's data folder, and that copy updates itself daily as it does on other systems.

## How releases publish it

The release workflow (`.github/workflows/build.yml`) does this:

1. **`flatpak`** builds the app on an x64 and an ARM runner. Each build uses the newest yt-dlp and adds a `<release>` line for the version to the metainfo.
2. **`flatpak-publish`** merges both builds into one repository and signs it with the key in the `FLATPAK_GPG_KEY` secret. It also makes the `.flatpakref` and `.flatpakrepo` files and the one-file installers, and attaches everything to the release.
3. **`pages`** (`.github/workflows/pages.yml`) publishes the website from `docs/` together with the repository. The website workflow also runs on its own when `docs/` changes; it then reuses the repository from the latest release.

### One-time setup

1. **Signing key.** Flatpak checks that updates come from you, so the repository is signed. Make a key on Linux (for example the VM). It has no passphrase, because the workflow uses it unattended:

   ```sh
   gpg --batch --passphrase '' --quick-gen-key "ClipHarbor Flatpak" rsa4096 sign never
   gpg --armor --export-secret-keys "ClipHarbor Flatpak" > clipharbor-flatpak-private.asc
   ```

   On GitHub, go to **Settings → Secrets and variables → Actions → New repository secret**. Name it `FLATPAK_GPG_KEY` and paste the whole contents of `clipharbor-flatpak-private.asc`, including the BEGIN/END lines.

   Keep a backup of that file somewhere safe and off GitHub, then delete the copy on the VM. If the key is lost, everyone who installed ClipHarbor has to remove the repository and add it again. Never commit it.

2. **Website source.** On GitHub, go to **Settings → Pages → Build and deployment → Source** and pick **GitHub Actions**. The website then comes from `pages.yml` instead of the `docs` folder directly. The address stays the same.

3. **Publish a release** with the Run workflow button. The Flatpak jobs only run for releases; the first one takes about half an hour because of ffmpeg.

## Trying a build on Linux

Steps 1 and 2 happen once; run them in this folder (`packaging/flatpak`).

1. Install Flatpak and the build tools, and add Flathub, which provides the runtime:

   ```sh
   sudo apt install flatpak flatpak-builder plasma-discover-backend-flatpak git python3
   flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
   flatpak install --user -y flathub org.freedesktop.Sdk//26.08 org.freedesktop.Platform//26.08 org.freedesktop.Sdk.Extension.dotnet10//26.08
   ```

2. Make the NuGet list:

   ```sh
   curl -fsSLO https://raw.githubusercontent.com/flatpak/flatpak-builder-tools/master/dotnet/flatpak-dotnet-generator.py
   python3 flatpak-dotnet-generator.py nuget-sources.json ../../ClipHarbor.csproj --dotnet 10 --freedesktop 26.08 --runtime linux-x64 linux-arm64
   ```

3. Build and install it for your user:

   ```sh
   flatpak-builder --user --install --force-clean --install-deps-from=flathub build-dir io.github.nolsen42.ClipHarbor.yml
   flatpak run io.github.nolsen42.ClipHarbor
   ```

What to check:

- Downloads work, including 1080p (needs ffmpeg) and MP3.
- The tray icon appears.
- The done notice shows.
- Start-at-login works (KDE may ask once).
- Settings shows no app updater.
- `clipharbor://` links from the browser extension open it.

Remove it with `flatpak uninstall --user io.github.nolsen42.ClipHarbor`.
