# Flatpak (Flathub)

Files for building ClipHarbor as a Flatpak:

- `io.github.nolsen42.ClipHarbor.yml`: the build recipe (manifest). It builds ffmpeg and ClipHarbor from source and includes yt-dlp.
- `.desktop`, `.metainfo.xml`, `.png`: the menu entry, the store listing and the icon.
- `nuget-sources.json`: the list of NuGet packages, so the build can run without internet. It is made by the generator (step 2) and has to be made again whenever a package version in `ClipHarbor.csproj` changes.

In a Flatpak, ClipHarbor notices that it's in the sandbox (`Platform.IsFlatpak`). It then turns off its own updater and the engine installer, since Flathub updates both. It also sends notices and sets start-at-login through the desktop portals.

## Trying it on Linux (Kubuntu)

Steps 1 and 2 happen once; run them in this folder (`packaging/flatpak`).

1. Install Flatpak and the build tools, and add Flathub:

   ```sh
   sudo apt install flatpak flatpak-builder plasma-discover-backend-flatpak git python3
   flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
   flatpak install --user -y flathub org.freedesktop.Sdk//26.08 org.freedesktop.Platform//26.08 org.freedesktop.Sdk.Extension.dotnet10//26.08
   ```

   If Discover doesn't show Flatpak apps yet, restart the session once.

2. Make the NuGet list:

   ```sh
   curl -fsSLO https://raw.githubusercontent.com/flatpak/flatpak-builder-tools/master/dotnet/flatpak-dotnet-generator.py
   python3 flatpak-dotnet-generator.py nuget-sources.json ../../ClipHarbor.csproj --dotnet 10 --freedesktop 26.08 --runtime linux-x64 linux-arm64
   ```

3. Build and install it for your user (the first build takes a while because of ffmpeg):

   ```sh
   flatpak-builder --user --install --force-clean --install-deps-from=flathub build-dir io.github.nolsen42.ClipHarbor.yml
   ```

4. Start it from the app menu, or with:

   ```sh
   flatpak run io.github.nolsen42.ClipHarbor
   ```

What to check:

- Downloads work, including 1080p (needs ffmpeg) and MP3.
- The tray icon appears.
- The done notice shows.
- Start-at-login works (KDE may ask once).
- Settings shows no updater.
- `clipharbor://` links from the browser extension open it.

Remove it with `flatpak uninstall --user io.github.nolsen42.ClipHarbor`.

Check the files the way Flathub does before submitting:

```sh
flatpak install --user -y flathub org.flatpak.Builder
flatpak run --command=flathub-build org.flatpak.Builder --install io.github.nolsen42.ClipHarbor.yml
flatpak run --command=flatpak-builder-lint org.flatpak.Builder manifest io.github.nolsen42.ClipHarbor.yml
flatpak run --command=flatpak-builder-lint org.flatpak.Builder repo repo
```

## Submitting to Flathub

The `flathub` folder holds exactly what goes to Flathub:
- the manifest, built from a release tag instead of this checkout
- `nuget-sources.json`
- the metainfo, which supplies the store listing and release notes

1. Fork https://github.com/flathub/flathub, keeping "Copy the master branch only" unticked.
2. In the fork, add the three files from `flathub` on the `new-pr` branch.
3. Open a pull request against `new-pr`, titled "Add io.github.nolsen42.ClipHarbor". See https://docs.flathub.org/docs/for-app-authors/submission.
4. After it's accepted, Flathub makes a repository `flathub/io.github.nolsen42.ClipHarbor`. Each later release is a small pull request there:
   - the new tag and its commit
   - `-p:Version=`
   - a `<release>` entry in the metainfo
   - `nuget-sources.json` made again, if packages changed

   New yt-dlp releases arrive there as pull requests from Flathub's bot.
