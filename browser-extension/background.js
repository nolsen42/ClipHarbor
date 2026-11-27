// Send to ClipHarbor: hands pages and links to the ClipHarbor app through clipharbor:// links.
// Nothing is collected or sent anywhere else.

const api = globalThis.browser ?? globalThis.chrome;

/** A text in the browser's language (_locales/<language>/messages.json, English when missing). */
const t = (key) => api.i18n.getMessage(key);

const MENUS = [
  { id: "download", title: t("menuDownload") },
  { id: "gif", title: t("menuGif") },
];

// The page that says the ClipHarbor app is needed too, with the right download for this computer.
const GET_APP = "https://nolsen42.github.io/ClipHarbor/extension.html";

api.runtime.onInstalled.addListener(async (details) => {
  await api.contextMenus.removeAll();
  for (const m of MENUS)
    api.contextMenus.create({ id: m.id, title: m.title, contexts: ["page", "link", "video", "image", "selection"] });
  // Many people add the extension without the app, and then nothing happens; say so right away.
  if (details?.reason === "install") api.tabs.create({ url: GET_APP }).catch(() => {});
});

// Once the app has opened from a link, the "Nothing opened?" note isn't needed any more.
const appWorks = async () => (await api.storage.local.get("appWorks")).appWorks === true;
api.runtime.onMessage.addListener((m) => {
  if (m?.type === "clipharbor-app-works") api.storage.local.set({ appWorks: true });
  if (m?.type === "clipharbor-get-app") api.tabs.create({ url: GET_APP });
});

/**
 * Runs in the page. A browser opens an app link silently when the app is there (its window takes the focus)
 * and does nothing at all when it isn't, so after a moment without the focus moving this explains why.
 */
function noteIfNothingOpens(text) {
  const run = globalThis.browser?.runtime ?? globalThis.chrome?.runtime;
  document.getElementById("clipharbor-note")?.remove();
  let host = null, done = false;
  const finish = (works) => {
    if (done) return;
    done = true;
    host?.remove();
    removeEventListener("blur", onBlur);
    if (works) run?.sendMessage({ type: "clipharbor-app-works" });
  };
  // Something outside the page took the focus: the app, or the browser's "Open ClipHarbor?" question.
  // (Switching to another tab hides the page as well, so that doesn't count.)
  const onBlur = () => setTimeout(() => { if (document.visibilityState === "visible") finish(true); }, 150);
  addEventListener("blur", onBlur);
  setTimeout(() => {
    if (done) return;
    host = document.createElement("div");
    host.id = "clipharbor-note";
    host.style.cssText = "all:initial; position:fixed; z-index:2147483647; bottom:20px; inset-inline-end:20px;";
    const root = host.attachShadow({ mode: "closed" });
    const dark = matchMedia("(prefers-color-scheme: dark)").matches;
    root.innerHTML = `
      <style>
        .box { font: 14px/1.45 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; width: 330px; max-width: calc(100vw - 40px);
          background: ${dark ? "#1d2029" : "#fff"}; color: ${dark ? "#e9ebf1" : "#1b1d24"}; border: 1px solid ${dark ? "#2b2f3b" : "#e2e4ec"};
          border-radius: 12px; padding: 14px 16px; box-shadow: 0 12px 36px rgba(0,0,0,.22); }
        b { display: block; font-size: 15px; margin-bottom: 4px; }
        p { margin: 0 0 12px; color: ${dark ? "#a1a6b5" : "#5b6070"}; }
        .row { display: flex; gap: 8px; flex-wrap: wrap; }
        button { font: inherit; font-weight: 600; border-radius: 8px; padding: 7px 14px; cursor: pointer; border: 1px solid ${dark ? "#2b2f3b" : "#e2e4ec"};
          background: transparent; color: inherit; }
        button.get { background: linear-gradient(135deg, #3b6ae8, #7650e6); color: #fff; border: 0; }
        .x { position: absolute; top: 6px; inset-inline-end: 8px; border: 0; padding: 4px 8px; font-weight: 400; color: ${dark ? "#a1a6b5" : "#5b6070"}; }
      </style>
      <div class="box" dir="${text.dir}" style="position:relative">
        <button class="x" title="${text.close}">✕</button>
        <b></b><p></p>
        <div class="row"><button class="get"></button><button class="ok"></button></div>
      </div>`;
    root.querySelector("b").textContent = text.title;
    root.querySelector("p").textContent = text.body;
    root.querySelector(".get").textContent = text.get;
    root.querySelector(".ok").textContent = text.works;
    root.querySelector(".get").onclick = () => { run?.sendMessage({ type: "clipharbor-get-app" }); finish(false); };
    root.querySelector(".ok").onclick = () => finish(true);
    root.querySelector(".x").onclick = () => finish(false);
    document.documentElement.appendChild(host);
    setTimeout(() => finish(false), 20000);
  }, 3000);
}

async function noteInPage(tab) {
  if (!(tab?.id >= 0) || await appWorks()) return;
  const text = {
    title: t("noteTitle"), body: t("noteBody"), get: t("noteGet"), works: t("noteWorks"), close: t("noteClose"),
    dir: api.i18n.getMessage("@@bidi_dir") || "ltr",
  };
  try {
    await api.scripting.executeScript({ target: { tabId: tab.id }, func: noteIfNothingOpens, args: [text] });
  } catch {
    // Pages the extension can't touch (the browser's own pages, the web stores): a hint on the toolbar button instead.
    await say(tab, `${text.title} ${text.body}`);
  }
}

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
  await noteInPage(tab); // set up before the link goes out, so it sees the app taking the focus
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
