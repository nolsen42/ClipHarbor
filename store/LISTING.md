# Store listings for "Send to ClipHarbor"

Copy and paste these into the store forms. Images are in this folder.

Privacy policy link (both stores): https://github.com/nolsen42/ClipHarbor/blob/main/PRIVACY.md
Home page / support link: https://github.com/nolsen42/ClipHarbor

---

## Chrome Web Store

**Package to upload:** `chrome-web-store.zip` from the **chrome-web-store-upload** artifact of the GitHub Actions build for your release (Actions tab → the release's Build run → Artifacts). It's the copy that's switched off on YouTube. Don't upload `ClipHarbor-extension-chrome.zip`.

### Store listing tab

- **Name:** Send to ClipHarbor (comes from the package)
- **Summary** (from the package, max 132 characters): Right-click a video, a link or an X post and send it to the ClipHarbor app to download it or turn it into a GIF.
- **Category:** Tools
- **Language:** English
- **Store icon:** `store-icon-128.png`
- **Screenshots** (1280×800): `screenshot-1.png`, `screenshot-2.png`
- **Small promo tile** (440×280): `promo-small-440x280.png`
- **Marquee promo tile** (optional, 1400×560): `promo-marquee-1400x560.png`
- **Description:**

```
Send to ClipHarbor connects your browser to ClipHarbor, a free and open-source desktop app for saving videos and making GIFs.

Right-click a video, a link to one, or anywhere on a page and choose:
• Download with ClipHarbor
• Make a GIF with ClipHarbor

On X (Twitter), right-click anywhere on a post, even in your timeline, and that post is sent. Great for short clips that were meant to be GIFs.

You can also press the toolbar button to send the page you're on, or use Alt+Shift+D (download) and Alt+Shift+G (GIF).

ClipHarbor then asks what you want (quality, format, GIF size, file name, folder) or, with Smart Mode on, starts straight away.

Needs the free ClipHarbor app for Windows, macOS or Linux: https://nolsen42.github.io/ClipHarbor/extension.html (the extension opens this page when it's added, and reminds you if the app doesn't open).

Private by design: the extension only acts when you click it, sends the address only to ClipHarbor on your own computer, and collects nothing. No accounts, no ads, no tracking.

Only download content you have the right to download.
```

### Privacy practices tab

- **Single purpose:**

```
Sends the address of the page, link, video or X post the person picks to the ClipHarbor desktop app on their computer, so the app can save it or turn it into a GIF.
```

- **Permission justifications:**
  - **contextMenus:** Adds the "Download with ClipHarbor" and "Make a GIF with ClipHarbor" items to the right-click menu, which is the main way the extension is used.
  - **activeTab:** Reads the address of the current tab only when the person clicks a menu item, the toolbar button or a shortcut, so that address can be sent to the ClipHarbor app.
  - **scripting:** Shows a short note in the page ("Nothing opened? You need the free ClipHarbor app") when the app doesn't open after a click, and is the fallback for opening the clipharbor:// link if the browser refuses to open it directly. Runs only after the person clicks.
  - **storage:** Remembers, on this computer only, that the ClipHarbor app has opened, so that note stops appearing. Nothing else is stored.
  - **Host permission (x.com, twitter.com):** A small script on X/Twitter remembers which post was right-clicked, so the post itself is sent instead of the timeline's address. It reads only the post's link, and only on right-click.
- **Remote code:** No, I am not using remote code.
- **Data usage:** tick nothing in the list (no data is collected), then tick all three statements (not sold, not used for unrelated purposes, not used for credit).
- **Privacy policy URL:** the link at the top.

### Notes for the reviewer (Test instructions tab, if asked)

```
This extension hands links to the free, open-source ClipHarbor desktop app (MIT license); it downloads nothing itself.

To test:
1. Install ClipHarbor from https://github.com/nolsen42/ClipHarbor/releases/latest (Windows: run ClipHarbor-windows-x64.exe; no install needed) and start it once so it registers clipharbor:// links.
2. In ClipHarbor, press "Install engine" once (it fetches yt-dlp and FFmpeg from their official GitHub releases).
3. Open any page with a video, or an X post, right-click it and choose "Download with ClipHarbor". Chrome asks whether to open ClipHarbor; allow it. ClipHarbor shows the download choices.

This store version does nothing on YouTube.
```

---

## Firefox Add-ons (addons.mozilla.org)

**Package to upload:** `ClipHarbor-extension-firefox.zip` from the release.

Two ways to publish:
- **On this site (listed):** people find it on addons.mozilla.org. Needs the listing below and a review.
- **On your own (unlisted):** Mozilla only signs it, usually within minutes, and you get a `.xpi` file. Attach it to the GitHub release; Firefox users install it by opening that file. No listing needed.

### Listing (for "On this site")

- **Name:** Send to ClipHarbor
- **Add-on URL:** send-to-clipharbor
- **Summary** (max 250 characters): Right-click a video, a link or an X post and send it to the free ClipHarbor desktop app to download it or turn it into a GIF. Collects nothing.
- **Description:** the same text as for Chrome.
- **Categories:** Download Management; Photos, Music & Videos
- **Support site:** https://github.com/nolsen42/ClipHarbor/issues
- **License:** MIT License
- **Privacy policy:** not needed (the extension declares that it collects no data), but you can paste the link.
- **Screenshots:** `screenshot-1.png`, `screenshot-2.png`
- **Does it need source code?** No. Nothing is minified or bundled; the uploaded files are the source.

### Notes to reviewer

```
Hands links to the free, open-source ClipHarbor desktop app (MIT, https://github.com/nolsen42/ClipHarbor) through clipharbor:// links; it downloads nothing itself and makes no network requests. The files are unminified source.

To test: install ClipHarbor from https://github.com/nolsen42/ClipHarbor/releases/latest, start it once (it registers clipharbor:// links), press "Install engine", then right-click a video page or an X post and choose "Download with ClipHarbor". Firefox asks whether to open ClipHarbor; allow it.
```
