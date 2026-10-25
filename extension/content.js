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
  const host = document.querySelector("ytmusic-player");
  const api = host?.playerApi;
  if (api && (typeof api.pause === "function" || typeof api.pauseVideo === "function" || typeof api.play === "function" || typeof api.playVideo === "function"))
    return api;
  const movie = document.querySelector("#movie_player");
  if (movie && (typeof movie.pauseVideo === "function" || typeof movie.playVideo === "function")) return movie;
  return null;
}

function addVideos(node, videos) {
  if (!node?.querySelectorAll) return;
  if (node instanceof HTMLVideoElement) videos.add(node);
  for (const video of node.querySelectorAll("video")) videos.add(video);
  for (const element of node.querySelectorAll("*")) {
    if (element.shadowRoot) addVideos(element.shadowRoot, videos);
  }
}

function collectVideos() {
  const videos = new Set(document.querySelectorAll("video"));
  for (const host of document.querySelectorAll("ytmusic-player, #movie_player, .html5-video-player")) {
    addVideos(host, videos);
    if (host.shadowRoot) addVideos(host.shadowRoot, videos);
  }
  return [...videos];
}

function playingVideo() {
  return collectVideos().find((video) => !video.paused && !video.ended) || null;
}

function buttonClaimsPaused() {
  const button = queryPlayer(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
  if (!button) return null;
  const inner = button.shadowRoot?.querySelector("button, tp-yt-paper-icon-button") || button.querySelector("button, tp-yt-paper-icon-button");
  const label = [button, inner]
    .filter(Boolean)
    .map((node) => `${node.getAttribute?.("aria-label") || ""} ${node.getAttribute?.("title") || ""}`)
    .join(" ")
    .toLowerCase();
  if (label.includes("pause")) return false;
  if (label.includes("play")) return true;

  const icon = [button, inner]
    .filter(Boolean)
    .map((node) => `${node.icon || ""} ${node.getAttribute?.("icon") || ""}`)
    .join(" ")
    .toLowerCase();
  if (icon.includes("pause")) return false;
  if (icon.includes("play")) return true;
  return null;
}

function uiClaimsPaused() {
  const fromButton = buttonClaimsPaused();
  if (fromButton !== null) return fromButton;
  const state = playerApi()?.getPlayerState?.();
  return state === 2 || state === 5;
}

let holdPaused = false;

function pauseVideos() {
  holdPaused = true;
  for (const video of collectVideos()) {
    if (!video.paused && !video.ended) video.pause();
  }
}

function playVideos() {
  holdPaused = false;
  const video = collectVideos().find((item) => item.paused && !item.ended) || collectVideos()[0];
  if (!video) return;
  video.play().catch(() => {});
}

function syncToButton(allowPlay) {
  const paused = uiClaimsPaused();
  if (paused === true) {
    pauseVideos();
    return;
  }
  if (paused === false && allowPlay) playVideos();
  else if (paused === false) holdPaused = false;
}

let syncTimer = 0;

function togglePlayback() {
  if (playingVideo()) {
    pauseVideos();
    return true;
  }
  if (collectVideos().length || playerApi()) {
    playVideos();
    return true;
  }
  return clickControl(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
}

function installMediaSession() {
  const session = navigator.mediaSession;
  if (!session?.setActionHandler) return;
  try {
    session.setActionHandler("pause", () => pauseVideos());
    session.setActionHandler("stop", () => pauseVideos());
    session.setActionHandler("play", () => playVideos());
  } catch {
    // The page can replace these handlers. The interval below installs them again.
  }
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
      if (!hit) return;
      const wasPlaying = Boolean(playingVideo());
      clearTimeout(syncTimer);
      syncTimer = setTimeout(() => {
        if (wasPlaying) pauseVideos();
        else playVideos();
      }, 120);
    },
    true,
  );

  document.addEventListener(
    "play",
    (event) => {
      if (!holdPaused || !(event.target instanceof HTMLVideoElement)) return;
      event.stopImmediatePropagation();
      event.target.pause();
    },
    true,
  );

  document.addEventListener(
    "timeupdate",
    (event) => {
      if (event.target instanceof HTMLVideoElement && !event.target.paused) syncToButton(false);
    },
    true,
  );

  installMediaSession();
  setInterval(installMediaSession, 2000);
}
