// Send to ClipHarbor: hands pages and links to the ClipHarbor app through clipharbor:// links.
// Nothing is collected or sent anywhere else.

const api = globalThis.browser ?? globalThis.chrome;

/** A text in the browser's language (_locales/<language>/messages.json, English when missing). */
const t = (key) => api.i18n.getMessage(key);

const MENUS = [
  { id: "download", title: t("menuDownload") },
  { id: "gif", title: t("menuGif") },
];

api.runtime.onInstalled.addListener(async () => {
  await api.contextMenus.removeAll();
  for (const m of MENUS)
    api.contextMenus.create({ id: m.id, title: m.title, contexts: ["page", "link", "video", "image", "selection"] });
});

const isWeb = (u) => typeof u === "string" && /^https?:\/\//i.test(u);

/** Asks the page which post was right-clicked (X/Twitter); null elsewhere. */
async function postFromPage(tabId) {
  try {
    const r = await api.tabs.sendMessage(tabId, { type: "clipharbor-target" });
    return isWeb(r?.url) ? r.url : null;
  } catch {
    return null; // no helper on this page
  }
}

/** The best address for what was clicked: the post, the link, the video file, or the page. */
async function pick(info, tab) {
  if (tab?.id >= 0) {
    const post = await postFromPage(tab.id);
    if (post) return post;
  }
  if (isWeb(info?.linkUrl)) return info.linkUrl;
  if (info?.mediaType === "video" && isWeb(info.srcUrl)) return info.srcUrl; // not blob: players
  if (isWeb(info?.frameUrl)) return info.frameUrl;
  if (isWeb(info?.pageUrl)) return info.pageUrl;
  return isWeb(tab?.url) ? tab.url : null;
}

// Sites this build doesn't send (skip-sites.json; the Chrome Web Store build lists YouTube there).
let skipSites;
async function skipped(url) {
  skipSites ??= await fetch(api.runtime.getURL("skip-sites.json")).then((r) => r.json()).catch(() => []);
  let host;
  try { host = new URL(url).hostname.toLowerCase(); } catch { return false; }
  return skipSites.some((s) => host === s || host.endsWith("." + s));
}

async function say(tab, text) {
  // A short note on the toolbar button instead of a pop-up.
  try {
    await api.action.setBadgeBackgroundColor({ color: "#C0392B", tabId: tab.id });
    await api.action.setBadgeText({ text: "✕", tabId: tab.id });
    await api.action.setTitle({ title: text, tabId: tab.id });
    setTimeout(() => {
      api.action.setBadgeText({ text: "", tabId: tab.id }).catch(() => {});
      api.action.setTitle({ title: t("actionTitle"), tabId: tab.id }).catch(() => {});
    }, 8000);
  } catch {}
}

async function send(kind, url, tab) {
  if (!url) return;
  if (await skipped(url) || await skipped(tab?.url)) {
    await say(tab, t("notHere"));
    return;
  }
  const link = `clipharbor://${kind}?url=${encodeURIComponent(url)}`;
  try {
    // Opening an app link doesn't leave the page; the browser asks first whether to open ClipHarbor.
    await api.tabs.update(tab.id, { url: link });
  } catch {
    await api.scripting.executeScript({ target: { tabId: tab.id }, func: (l) => { location.href = l; }, args: [link] });
  }
}

api.contextMenus.onClicked.addListener(async (info, tab) => {
  await send(info.menuItemId === "gif" ? "gif" : "download", await pick(info, tab), tab);
});

api.action.onClicked.addListener(async (tab) => {
  await send("download", isWeb(tab.url) ? tab.url : null, tab);
});

api.commands.onCommand.addListener(async (command, tab) => {
  tab ??= (await api.tabs.query({ active: true, currentWindow: true }))[0];
  if (!tab) return;
  await send(command === "gif-page" ? "gif" : "download", isWeb(tab.url) ? tab.url : null, tab);
});
