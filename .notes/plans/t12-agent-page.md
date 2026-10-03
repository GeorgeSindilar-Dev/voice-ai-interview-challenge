# T12: Agent Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The browser page at `/`: enter the access code, then Start/End a voice call with the agent, hear it, interrupt it, and read its captions, with a completely silent DevTools console.

**Architecture:** Static files served by the app (`UseDefaultFiles` + `UseStaticFiles`). One ES module (`js/agent.js`) asks `/access/status`, posts the code to `/access`, and on Start creates one 24 kHz `AudioContext` inside the click, opens the microphone (echo cancellation, noise suppression), loads two AudioWorklets (capture: PCM16 100 ms frames; playback: a queue that `null` flushes) and exchanges binary PCM16 frames with `/voice/ws`. Text frames from the server (`clear`, `caption`, `ended`) drive the UI. Every failure is a status line, never a console message.

**Tech Stack:** HTML, CSS, vanilla JavaScript (ES module), Web Audio `AudioWorklet`, WebSocket. No frameworks, no inline scripts or styles, `textContent` only. "step-07" = `.notes/archive/overnight/plans/step-07-voice-agent.md` (the worklets are copied from it). Depends on T10 (`/access`, `/access/status`, cookie) and T11 (`/voice/ws`, wire format, `ended` reasons). No test code (manual checks only). Commands run from `solution/`.

---

### Task 1: Markup, styles, favicon and static files

**Files:** Create `src/VoiceReset/wwwroot/index.html`, `src/VoiceReset/wwwroot/css/site.css`, `src/VoiceReset/wwwroot/favicon.svg`; modify `src/VoiceReset/Program.cs`.

- [ ] **Step 1: Write `src/VoiceReset/wwwroot/index.html`**
```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Password reset assistant</title>
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/css/site.css">
  <script type="module" src="/js/agent.js"></script>
</head>
<body>
  <main>
    <h1>Password reset assistant</h1>
    <p>Talk to the help desk's automated assistant to reset your password.
      It is an AI voice agent, not a person. Headphones give the best result.</p>

    <section id="access-section" data-testid="access-section" aria-labelledby="access-heading" hidden>
      <h2 id="access-heading">Access code</h2>
      <form id="access-form" data-testid="access-form" novalidate>
        <label for="access-code">Enter the access code you were given</label>
        <input id="access-code" data-testid="access-code" name="code" type="text"
               autocomplete="off" autocapitalize="off" spellcheck="false" required>
        <button id="access-submit" data-testid="access-submit" type="submit">Continue</button>
      </form>
      <p id="access-error" data-testid="access-error" class="error" role="alert" hidden></p>
    </section>

    <section id="call-section" data-testid="call-section" aria-labelledby="call-heading" hidden>
      <h2 id="call-heading">Call</h2>
      <div class="controls">
        <button id="start-button" data-testid="start-button" type="button">Start call</button>
        <button id="end-button" data-testid="end-button" type="button" disabled>End call</button>
      </div>
      <p id="status" data-testid="status" class="status" role="status" aria-live="polite">Press Start and allow the microphone.</p>
      <h3>What the assistant said</h3>
      <ol id="captions" data-testid="captions" class="captions" aria-live="polite"></ol>
      <p class="note">Never say your password out loud. You will type it privately in the reset form that the link opens.</p>
    </section>
  </main>
</body>
</html>
```
The access input is `type="text"` on purpose: a password field makes Chrome print `[DOM]` autocomplete hints in the console.

- [ ] **Step 2: Write `src/VoiceReset/wwwroot/css/site.css`**
```css
:root {
  color-scheme: light dark;
  --accent: #1f5fbf;
  --danger: #b3261e;
  font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  line-height: 1.5;
}

body { margin: 0; padding: 1.5rem; }
main { max-width: 40rem; margin: 0 auto; }
label { display: block; font-weight: 600; margin-bottom: 0.25rem; }
input { font: inherit; padding: 0.5rem; width: 100%; max-width: 20rem; box-sizing: border-box; }

button {
  font: inherit;
  padding: 0.6rem 1.2rem;
  margin: 0.5rem 0.5rem 0.5rem 0;
  border: none;
  border-radius: 0.4rem;
  background: var(--accent);
  color: #fff;
  cursor: pointer;
}

button:disabled { opacity: 0.5; cursor: default; }
button:focus-visible, input:focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; }
#end-button { background: var(--danger); }
.error { color: var(--danger); font-weight: 600; }
.status { font-weight: 600; }
.captions { padding-left: 1.25rem; }
.captions li { margin-bottom: 0.4rem; }
.note { font-size: 0.9rem; opacity: 0.8; }
```

- [ ] **Step 3: Write `src/VoiceReset/wwwroot/favicon.svg`** (a favicon avoids a 404 line in the console)
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="16" fill="#1f5fbf"/><rect x="12" y="6" width="8" height="13" rx="4" fill="#fff"/><path d="M9 15a7 7 0 0 0 14 0" fill="none" stroke="#fff" stroke-width="2"/><path d="M16 22v4" stroke="#fff" stroke-width="2"/></svg>
```

- [ ] **Step 4: Serve static files.** In `src/VoiceReset/Program.cs`, right after `var app = builder.Build();` (before T14's headers if they exist, else before `UseVoiceWebSockets`/`UseAuthentication`):
```csharp
app.UseDefaultFiles();   // "/" serves wwwroot/index.html
app.UseStaticFiles();    // public files: the page holds no secrets
```

- [ ] **Step 5: Build, check, commit.** `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`; `dotnet test --project tests/VoiceReset.Tests` → all pass.
```bash
git add src/VoiceReset/wwwroot/index.html src/VoiceReset/wwwroot/css/site.css src/VoiceReset/wwwroot/favicon.svg src/VoiceReset/Program.cs
git commit -m "feat(page): add the agent page markup and styles"
```

---

### Task 2: Audio worklets and the page script

**Files:** Create `src/VoiceReset/wwwroot/js/audio-worklets.js`, `src/VoiceReset/wwwroot/js/agent.js`.

- [ ] **Step 1: `js/audio-worklets.js`** — copy step-07 Task 10 Step 4 (`audio-worklets.js`, lines 4101–4176) unchanged. It registers `capture` (Float32 → PCM16 chunks of 2,400 samples = 100 ms, posted as transferable buffers) and `playback` (a queue of PCM16 chunks; the message `null` empties it; it posts `'idle'` when it has played everything). Both run on the audio thread, so audio keeps flowing in a background tab; no timers, no `console`.

- [ ] **Step 2: Write `js/agent.js`**
```js
// Agent page: access code, then one voice call over /voice/ws.
// Rules: no console output at all; every failure is shown on the page; server text only via textContent.

const ENDED_MESSAGES = {
  agent_ended: 'The assistant ended the call. Thank you.',
  time_limit: 'The call reached its time limit.',
  call_dropped: 'The call was disconnected. Press Start to try again.',
  unavailable: 'The voice service is not available right now. Please try again later.',
};

const byId = (id) => document.getElementById(id);

let call = null; // { context, stream, socket, player, playing, closing, endedReason, userEnded }

function setStatus(text) {
  byId('status').textContent = text;
}

function showAccessError(text) {
  const error = byId('access-error');
  error.textContent = text;
  error.hidden = text === '';
}

function showSignedIn(signedIn) {
  byId('access-section').hidden = signedIn;
  byId('call-section').hidden = !signedIn;
}

function setInCall(inCall) {
  byId('start-button').disabled = inCall;
  byId('end-button').disabled = !inCall;
}

async function isSignedIn() {
  const response = await fetch('/access/status', { credentials: 'same-origin' });
  const body = await response.json();
  return body.signedIn === true;
}

async function onAccessSubmit(event) {
  event.preventDefault();
  const input = byId('access-code');
  const code = input.value.trim();
  showAccessError('');
  if (code === '') {
    showAccessError('Please enter the access code.');
    return;
  }
  byId('access-submit').disabled = true;
  try {
    const response = await fetch('/access', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code }),
      credentials: 'same-origin',
    });
    if (response.status === 429) {
      showAccessError('Too many attempts. Please wait a minute and try again.');
      return;
    }
    const result = response.ok ? await response.json() : { ok: false };
    if (result.ok !== true) {
      showAccessError('That access code is not correct.');
      return;
    }
    input.value = '';
    showSignedIn(true);
  } catch {
    showAccessError('Could not reach the server. Please try again.');
  } finally {
    byId('access-submit').disabled = false;
  }
}

async function onStart() {
  if (call !== null) {
    return;
  }
  setInCall(true);
  byId('captions').replaceChildren();
  setStatus('Starting…');
  // Created inside the click, so the browser allows sound without an autoplay warning.
  const context = new AudioContext({ sampleRate: 24000 });
  let stream = null;
  try {
    if (!(await isSignedIn())) {
      // The 2-hour cookie expired: ask again instead of a WebSocket handshake that fails with 401.
      stopAudio(context, null);
      setInCall(false);
      showSignedIn(false);
      showAccessError('Your access has expired. Please enter the code again.');
      return;
    }
    await context.audioWorklet.addModule('/js/audio-worklets.js');
    stream = await navigator.mediaDevices.getUserMedia({
      audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    });
  } catch (error) {
    stopAudio(context, stream);
    setInCall(false);
    setStatus(error && (error.name === 'NotAllowedError' || error.name === 'SecurityError')
      ? 'Microphone blocked. Allow the microphone for this page and press Start again.'
      : 'Could not start the call on this device. Please try Chrome or Edge.');
    return;
  }

  const capture = new AudioWorkletNode(context, 'capture');
  const player = new AudioWorkletNode(context, 'playback');
  context.createMediaStreamSource(stream).connect(capture);
  capture.connect(context.destination); // outputs silence; being connected keeps it running
  player.connect(context.destination);

  const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
  const socket = new WebSocket(`${scheme}://${location.host}/voice/ws`); // the access cookie goes with it
  socket.binaryType = 'arraybuffer';
  call = { context, stream, socket, player, playing: false, closing: false, endedReason: null, userEnded: false };

  capture.port.onmessage = (message) => {
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(message.data); // PCM16 24 kHz mono, 100 ms
    }
  };
  player.port.onmessage = onPlayerIdle;
  socket.onopen = () => setStatus('Connected. The assistant will greet you.');
  socket.onmessage = (message) => onSocketMessage(message.data);
  socket.onclose = onSocketClosed;
}

function onSocketMessage(data) {
  if (call === null) {
    return;
  }
  if (typeof data !== 'string') {
    call.playing = true;
    call.player.port.postMessage(data, [data]); // agent audio, PCM16 24 kHz
    return;
  }
  let message;
  try {
    message = JSON.parse(data);
  } catch {
    return;
  }
  if (message.type === 'clear') {
    call.player.port.postMessage(null); // barge-in: drop audio the caller has not heard yet
  } else if (message.type === 'caption') {
    const item = document.createElement('li');
    item.textContent = String(message.text);
    byId('captions').append(item);
  } else if (message.type === 'ended') {
    call.endedReason = String(message.reason);
  }
}

function onSocketClosed() {
  if (call === null) {
    return;
  }
  call.stream.getTracks().forEach((track) => track.stop()); // the microphone stops at once
  if (call.playing && !call.userEnded) {
    call.closing = true; // let the goodbye finish playing, then close audio
    setStatus('Ending the call…');
  } else {
    finishCall();
  }
}

function onPlayerIdle() {
  if (call === null) {
    return;
  }
  call.playing = false;
  if (call.closing) {
    finishCall();
  }
}

function onEnd() {
  if (call === null) {
    return;
  }
  call.userEnded = true;
  call.player.port.postMessage(null);
  call.socket.close(1000, 'caller_ended'); // onclose finishes the call
}

function finishCall() {
  const { context, stream, endedReason, userEnded } = call;
  call = null;
  stopAudio(context, stream);
  setInCall(false);
  if (userEnded) {
    setStatus('Call ended.');
  } else if (endedReason !== null) {
    setStatus(ENDED_MESSAGES[endedReason] ?? 'The call has ended.');
  } else {
    setStatus('Connection lost. Press Start to try again.');
  }
}

function stopAudio(context, stream) {
  if (stream) {
    stream.getTracks().forEach((track) => track.stop());
  }
  if (context.state !== 'closed') {
    context.close().catch(() => {}); // nothing to report: the call is over
  }
}

async function init() {
  byId('access-form').addEventListener('submit', onAccessSubmit);
  byId('start-button').addEventListener('click', onStart);
  byId('end-button').addEventListener('click', onEnd);
  try {
    showSignedIn(await isSignedIn());
  } catch {
    showSignedIn(false);
    showAccessError('Could not reach the server. Please reload the page.');
  }
}

init();
```

- [ ] **Step 3: Static self-check** (no test code): from `solution/`, run
`Select-String -Path src/VoiceReset/wwwroot/js/*.js,src/VoiceReset/wwwroot/index.html -Pattern 'console\.|innerHTML|outerHTML|insertAdjacentHTML|eval\(|ScriptProcessor|setInterval|<style|style=|onclick='`
Expected: no output.

- [ ] **Step 4: Commit**
```bash
git add src/VoiceReset/wwwroot/js/audio-worklets.js src/VoiceReset/wwwroot/js/agent.js
git commit -m "feat(page): add the agent page script and audio worklets"
```

---

### Task 3: Manual checks (Chrome or Edge; DevTools Console open, all levels, "Preserve log" on)

- [ ] **Step 1: Run the app over HTTPS** (PowerShell, from `solution/`; the `__Host-` cookie, the microphone and `Access:AllowedOrigin` need `https://localhost:7180`):
```powershell
dotnet dev-certs https --trust   # once
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/VoiceReset --urls https://localhost:7180
```
For a real conversation also set `$env:VoiceLive__Endpoint = "<Foundry URL>"` and run `az login` first.

- [ ] **Step 2: Check each item; the Console must stay empty for all of them** (no errors, warnings, info or `[DOM]` lines; the Network tab shows no 4xx/5xx except where noted):
  1. Open `https://localhost:7180/` → access form shown, `favicon.svg` 200, keyboard focus visible on Tab.
  2. Wrong code → "That access code is not correct."; no `Set-Cookie` in Network.
  3. Empty code → "Please enter the access code." (no request).
  4. `dev-only-access-code` → call section shown; reload → still the call section (`/access/status`).
  5. Start, then block the microphone → "Microphone blocked. Allow the microphone for this page and press Start again."
  6. Start with the fake endpoint (no `VoiceLive__Endpoint`) → "The voice service is not available right now. Please try again later."
  7. With the real endpoint: Start → greeting heard and its caption listed; speak a username → answer; talk over the agent → it stops at once (`clear`); End → "Call ended."
  8. Stop the server during a call → "Connection lost. Press Start to try again."
  9. Delete the `__Host-vr-access` cookie (Application tab), press Start → access form with "Your access has expired…" and no failed WebSocket handshake.
  10. Narrow the window to 375 px → no horizontal scroll; captions wrap.
  11. Six wrong codes within a minute → "Too many attempts…" (Network shows `429`; see Question 1 about its console line).

## Questions

1. Chrome prints a red "Failed to load resource: 429" console line when the rate limit (T10) rejects the sixth code attempt. Should T10's rate limiter answer rejections with `200 {"ok":false,"tooManyAttempts":true}` instead (keeps the console clean), or is a 429 line acceptable for this abuse case?
2. One 24 kHz `AudioContext` lets the browser resample the microphone. Chrome and Edge do this; older Firefox refused (unverified for current Firefox), where the page shows "Could not start the call on this device. Please try Chrome or Edge." OK to support Chrome/Edge only?
3. Only the agent's words are captioned (the contract's `caption` has no role). Should the caller's own words be shown too?
4. The page never runs on plain HTTP; `ws:` is kept only so a local HTTP run does not throw. Remove it?

## Additions to contracts

- Element ids (= `data-testid`): `access-section`, `access-form`, `access-code`, `access-submit`, `access-error`, `call-section`, `start-button`, `end-button`, `status`, `captions`.
- `ended` reason → page text: `agent_ended`, `time_limit`, `call_dropped`, `unavailable` (T11); the browser closes with code 1000, reason `caller_ended` (End button).
- `Program.cs`: `UseDefaultFiles()` + `UseStaticFiles()` right after `Build()`, before the WebSocket and auth middleware (T14 puts its security headers before them).
- Local HTTPS URL for manual runs: `https://localhost:7180` (matches T10's dev `Access:AllowedOrigin`).
