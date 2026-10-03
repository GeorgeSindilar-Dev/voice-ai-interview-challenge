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

// true or false, or null when the server could not be reached.
async function isSignedIn() {
  try {
    const response = await fetch('/access/status', { credentials: 'same-origin' });
    const body = await response.json();
    return body.signedIn === true;
  } catch {
    return null;
  }
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
    const result = response.ok ? await response.json() : { ok: false };
    if (result.tooManyAttempts === true) {
      showAccessError('Too many attempts. Please wait a minute and try again.');
      return;
    }
    if (result.ok !== true) {
      showAccessError('That access code is not correct.');
      return;
    }
    input.value = '';
    showSignedIn(true);
    byId('start-button').focus();
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
  // End stays disabled until the call exists, so nothing can end a call that is still starting.
  byId('start-button').disabled = true;
  byId('captions').replaceChildren();
  setStatus('Starting…');
  let context = null;
  let stream = null;
  try {
    // Created inside the click, so the browser allows sound without an autoplay warning.
    context = new AudioContext({ sampleRate: 24000 });
    const signedIn = await isSignedIn();
    if (signedIn === null) {
      failStart(context, stream, 'Could not reach the server. Please try again.');
      return;
    }
    if (!signedIn) {
      // The 2-hour cookie expired: ask again instead of a WebSocket handshake that fails with 401.
      failStart(context, stream, 'Press Start and allow the microphone.');
      showSignedIn(false);
      showAccessError('Your access has expired. Please enter the code again.');
      byId('access-code').focus();
      return;
    }
    await context.audioWorklet.addModule('/js/audio-worklets.js');
    stream = await navigator.mediaDevices.getUserMedia({
      audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    });

    const capture = new AudioWorkletNode(context, 'capture');
    const player = new AudioWorkletNode(context, 'playback');
    context.createMediaStreamSource(stream).connect(capture);
    capture.connect(context.destination); // outputs silence; being connected keeps it running
    player.connect(context.destination);

    // HTTPS only (the access cookie is Secure), so always wss; the access cookie goes with it.
    const socket = new WebSocket(`wss://${location.host}/voice/ws`);
    socket.binaryType = 'arraybuffer';
    capture.port.onmessage = (message) => {
      if (socket.readyState === WebSocket.OPEN) {
        socket.send(message.data); // PCM16 24 kHz mono, 100 ms
      }
    };
    player.port.onmessage = onPlayerIdle;
    socket.onopen = onSocketOpen;
    socket.onmessage = (message) => onSocketMessage(message.data);
    socket.onclose = onSocketClosed;
    call = { context, stream, socket, player, playing: false, closing: false, endedReason: null, userEnded: false };
    setInCall(true);
  } catch (error) {
    failStart(context, stream, startFailureMessage(error));
  }
}

function startFailureMessage(error) {
  const name = error ? error.name : '';
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'Microphone blocked. Allow the microphone for this page and press Start again.';
  }
  if (name === 'NotFoundError') {
    return 'No microphone was found.';
  }
  return 'Could not start the call on this device. Please try Chrome or Edge.';
}

function failStart(context, stream, message) {
  stopAudio(context, stream);
  setInCall(false);
  setStatus(message);
}

function onSocketOpen() {
  if (call === null) {
    return;
  }
  if (call.userEnded) {
    call.socket.close(1000, 'caller_ended'); // End was pressed while connecting
    return;
  }
  setStatus('Connected. The assistant will greet you.');
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
  if (call.socket.readyState === WebSocket.CONNECTING) {
    // Closing a socket that is still connecting prints a console error: onopen closes it instead.
    setStatus('Ending the call…');
    return;
  }
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
  if (context && context.state !== 'closed') {
    context.close().catch(() => {}); // nothing to report: the call is over
  }
}

async function init() {
  byId('access-form').addEventListener('submit', onAccessSubmit);
  byId('start-button').addEventListener('click', onStart);
  byId('end-button').addEventListener('click', onEnd);
  const signedIn = await isSignedIn();
  showSignedIn(signedIn === true);
  if (signedIn === null) {
    showAccessError('Could not reach the server. Please reload the page.');
  }
}

init();
