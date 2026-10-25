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

function toggleVideo() {
  const video = document.querySelector("video");
  if (!video) return false;
  if (video.paused) video.play();
  else video.pause();
  return true;
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
      return (
        clickControl(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]) ||
        toggleVideo()
      );
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
}
