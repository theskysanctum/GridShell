// Routes every download Chrome starts to GridShell's local Download Manager bridge instead of
// letting Chrome handle it - GridShell.Grid listens on 127.0.0.1 only while it's running.
const BRIDGE_URL = "http://127.0.0.1:47821/add-download";

chrome.downloads.onCreated.addListener((item) => {
  // Cancel Chrome's own download immediately, then erase the now-empty entry so it doesn't
  // sit in the download shelf/history looking like a failed download.
  chrome.downloads.cancel(item.id, () => {
    chrome.downloads.erase({ id: item.id });
  });

  fetch(BRIDGE_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      url: item.finalUrl || item.url,
      filename: item.filename || null,
      referrer: item.referrer || null,
    }),
  }).catch(() => {
    // GridShell isn't running, or the bridge isn't listening - the download is already
    // cancelled, so fall back to a plain tab navigation rather than silently losing it.
    chrome.tabs.create({ url: item.finalUrl || item.url });
  });
});
