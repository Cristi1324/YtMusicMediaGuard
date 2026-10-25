function queryPlayer(selectors) {
  const roots = [document];
  const bar = document.querySelector("ytmusic-player-bar");
  if (bar && bar.shadowRoot) roots.push(bar.shadowRoot);
  for (const root of roots) {
    for (const selector of selectors) {
      const simplified = selector.replace("ytmusic-player-bar ", "");
      const element = root.querySelector(selector) || root.querySelector(simplified);
      if (element) return element;
    }
  }
  return null;
}

function clickControl(selectors) {
  const element = queryPlayer(selectors);
  if (!element) return false;
  const inner = element.querySelector("button, a");
  (inner || element).click();
  return true;
}

function setSliderValue(slider, next) {
  for (const name of ["value", "immediateValue"]) {
    try {
      slider[name] = next;
    } catch {
      // Some player builds expose the slider as a read-only attribute.
    }
  }
  slider.setAttribute("value", String(next));
  if (typeof slider.notifyPath === "function") slider.notifyPath("value");
  slider.dispatchEvent(new Event("input", { bubbles: true }));
  slider.dispatchEvent(new Event("change", { bubbles: true }));
  slider.dispatchEvent(
    new CustomEvent("value-changed", {
      bubbles: true,
      composed: true,
      detail: { value: next },
    }),
  );
}

function stepVolume(delta) {
  const slider = queryPlayer(["ytmusic-player-bar #volume-slider", "#volume-slider"]);
  if (slider) {
    const current = Number(slider.value ?? slider.getAttribute("value") ?? 50);
    const next = Math.max(0, Math.min(100, (Number.isFinite(current) ? current : 50) + delta));
    setSliderValue(slider, next);
    return true;
  }

  const video = document.querySelector("video");
  if (!video) return false;
  video.volume = Math.max(0, Math.min(1, video.volume + delta / 100));
  return true;
}

function playerApi() {
  const hosts = [document.querySelector("ytmusic-player"), document.querySelector("#movie_player")];
  for (const host of hosts) {
    if (!host) continue;
    const api = host.playerApi || host;
    if (typeof api.pauseVideo === "function" || typeof api.playVideo === "function") return api;
  }
  return null;
}

function playingVideo() {
  for (const video of document.querySelectorAll("video")) {
    if (!video.paused && !video.ended) return video;
  }
  return null;
}

function visitControl(root, visit) {
  const pending = [root];
  const seen = new Set();
  while (pending.length) {
    const node = pending.pop();
    if (!node || seen.has(node)) continue;
    seen.add(node);
    visit(node);
    if (node.shadowRoot) pending.push(node.shadowRoot);
    if (!node.querySelectorAll) continue;
    for (const child of node.querySelectorAll("*")) pending.push(child);
  }
}

function buttonClaimsPaused() {
  const button = queryPlayer(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
  if (!button) return null;
  const icons = [];
  const labels = [];
  const paths = [];
  visitControl(button, (node) => {
    if (node.nodeType !== 1) return;
    const icon = `${node.icon || ""} ${node.getAttribute("icon") || ""}`.trim().toLowerCase();
    if (icon) icons.push(icon);
    const label = `${node.getAttribute("aria-label") || ""} ${node.getAttribute("title") || ""}`.trim().toLowerCase();
    if (label) labels.push(label);
    const path = node.getAttribute("d");
    if (path) paths.push(path);
  });

  const iconText = icons.join(" ");
  if (iconText.includes("pause")) return false;
  if (iconText.includes("play")) return true;
  const drawn = paths.join(" ");
  if (drawn.includes("h4V5")) return false;
  if (drawn.includes("l11-7")) return true;
  const labelText = labels.join(" ");
  if (labelText.includes("pause")) return false;
  if (/\bplay\b/.test(labelText)) return true;
  return null;
}

function uiClaimsPaused() {
  const fromButton = buttonClaimsPaused();
  if (fromButton !== null) return fromButton;
  const state = playerApi()?.getPlayerState?.();
  return state === 2 || state === 5;
}

function forcePause(preferred) {
  const player = playerApi();
  if (typeof player?.pauseVideo === "function") player.pauseVideo();
  for (const video of [preferred, playingVideo()]) {
    if (video && !video.paused) video.pause();
  }
}

function forcePlay() {
  const player = playerApi();
  if (typeof player?.playVideo === "function") player.playVideo();
  const video = document.querySelector("video");
  if (video?.paused) video.play().catch(() => {});
}

function togglePlayback() {
  const video = playingVideo() || document.querySelector("video");
  const player = playerApi();
  if (!video && !player) {
    return clickControl(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
  }

  const playing = video ? !video.paused && !video.ended : player.getPlayerState?.() === 1;
  if (playing) forcePause(video);
  else forcePlay();
  return true;
}

let disagreeSince = 0;
let pauseFixes = 0;

function correctStuckPause(video) {
  const stuck = video && !video.paused && !video.ended && uiClaimsPaused();
  if (!stuck) {
    disagreeSince = 0;
    pauseFixes = 0;
    return;
  }

  const now = performance.now();
  if (disagreeSince === 0) disagreeSince = now;
  if (now - disagreeSince < 300 || pauseFixes >= 5) return;
  pauseFixes += 1;
  forcePause(video);
}

function toggleMute() {
  const video = document.querySelector("video");
  if (!video) return false;
  video.muted = !video.muted;
  return true;
}

function runPlayerCommand(command) {
  switch (command) {
    case "playPause":
      return togglePlayback();
    case "next":
      return clickControl([
        "ytmusic-player-bar .next-button",
        ".next-button",
        "ytmusic-player-bar [data-id='next']",
      ]);
    case "previous":
      return clickControl([
        "ytmusic-player-bar .previous-button",
        ".previous-button",
        "ytmusic-player-bar [data-id='previous']",
      ]);
    case "volumeUp":
      return stepVolume(5);
    case "volumeDown":
      return stepVolume(-5);
    case "mute":
      return clickControl(["ytmusic-player-bar #volume-button", "#volume-button"]) || toggleMute();
    default:
      return false;
  }
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function runWhenReady(command) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (runPlayerCommand(command)) return true;
    await delay(100);
  }
  return false;
}

if (!globalThis.__ytMusicGuardListening) {
  globalThis.__ytMusicGuardListening = true;
  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type !== "playerCommand") return;
    runWhenReady(message.command).then((ok) => sendResponse({ ok }));
    return true;
  });

  document.addEventListener(
    "click",
    (event) => {
      const path = typeof event.composedPath === "function" ? event.composedPath() : [];
      const hit = path.some((node) => node?.id === "play-pause-button") || event.target?.closest?.("#play-pause-button");
      const video = playingVideo();
      if (!hit || !video) return;
      const settle = () => {
        const current = playingVideo();
        if (current) forcePause(current);
      };
      setTimeout(settle, 80);
      setTimeout(settle, 300);
    },
    true,
  );

  document.addEventListener(
    "timeupdate",
    (event) => {
      if (event.target instanceof HTMLVideoElement) correctStuckPause(event.target);
    },
    true,
  );
}
