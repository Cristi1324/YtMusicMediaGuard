const HOST = "com.ytmusic.mediaguard";

let port = null;
let profileId = null;
let reconnectTimer = null;
let sendTimer = null;
let failures = 0;

async function getProfileId() {
  const stored = await chrome.storage.local.get("profileId");
  if (stored.profileId) return stored.profileId;
  const id = crypto.randomUUID();
  await chrome.storage.local.set({ profileId: id });
  return id;
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  const delay = Math.min(30000, 1000 * 2 ** Math.min(failures, 5));
  failures += 1;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, delay);
}

function connect() {
  if (port) return;
  try {
    port = chrome.runtime.connectNative(HOST);
  } catch {
    scheduleReconnect();
    return;
  }

  port.onMessage.addListener((message) => {
    if (message?.type === "command" && message.command) {
      runPlayerCommand(message.command);
    }
  });

  port.onDisconnect.addListener(() => {
    port = null;
    const reason = chrome.runtime.lastError?.message || "";
    if (reason.toLowerCase().includes("not found")) {
      failures = Math.max(failures, 3);
    }
    scheduleReconnect();
  });

  failures = 0;
  sendState();
}

function scheduleSend() {
  if (sendTimer) clearTimeout(sendTimer);
  sendTimer = setTimeout(() => {
    sendTimer = null;
    sendState();
  }, 200);
}

async function sendState() {
  profileId = profileId || (await getProfileId());
  const tabs = await chrome.tabs.query({ url: "*://music.youtube.com/*" });
  const audible = tabs.some((tab) => tab.audible);
  const lastActive = tabs.reduce((max, tab) => Math.max(max, tab.lastAccessed || 0), 0);
  const message = {
    type: "state",
    profileId,
    openTabs: tabs.length,
    audible,
    lastActive,
  };

  if (!port) {
    connect();
    return;
  }

  try {
    port.postMessage(message);
  } catch {
    port = null;
    scheduleReconnect();
  }
}

async function runPlayerCommand(command) {
  const tabs = await chrome.tabs.query({ url: "*://music.youtube.com/*" });
  if (!tabs.length) return;

  const audible = tabs.filter((tab) => tab.audible);
  const pool = audible.length ? audible : tabs.slice();
  pool.sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0));
  const tab = pool[0];

  try {
    await chrome.tabs.sendMessage(tab.id, { type: "playerCommand", command });
    return;
  } catch {
    // A discarded tab has no content script until it is loaded again.
  }

  try {
    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      files: ["content.js"],
    });
    await chrome.tabs.sendMessage(tab.id, { type: "playerCommand", command });
  } catch {
    // The player is not available in this tab.
  }
}

chrome.runtime.onInstalled.addListener(() => connect());
chrome.runtime.onStartup.addListener(() => connect());
chrome.tabs.onCreated.addListener(scheduleSend);
chrome.tabs.onUpdated.addListener(scheduleSend);
chrome.tabs.onRemoved.addListener(scheduleSend);
chrome.tabs.onReplaced.addListener(scheduleSend);
chrome.windows.onRemoved.addListener(scheduleSend);
chrome.alarms.create("resync", { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener(() => sendState());

connect();
