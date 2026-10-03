// Audio worklets for the voice page. They run on the browser's audio thread, so audio keeps
// flowing in a background tab. Both work at the AudioContext rate of 24 kHz.

// Microphone: Float32 samples -> PCM16 chunks of 100 ms (2,400 samples, 4,800 bytes).
class CaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.chunk = new Int16Array(2400);
    this.length = 0;
  }

  process(inputs) {
    const samples = inputs[0] && inputs[0][0];
    if (samples) {
      for (let i = 0; i < samples.length; i++) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        this.chunk[this.length++] = s < 0 ? s * 0x8000 : s * 0x7fff;
        if (this.length === this.chunk.length) {
          this.port.postMessage(this.chunk.buffer, [this.chunk.buffer]);
          this.chunk = new Int16Array(2400);
          this.length = 0;
        }
      }
    }
    return true;
  }
}

// Speaker: a queue of PCM16 chunks. The message null empties the queue (barge-in).
// It posts 'idle' when it has played everything, so the page can close audio after a goodbye.
class PlaybackProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.queue = [];
    this.current = null;
    this.position = 0;
    this.playing = false;
    this.port.onmessage = (event) => {
      if (event.data === null) {
        this.queue = [];
        this.current = null;
        this.position = 0;
      } else {
        // An odd byte at the end would throw in Int16Array; it can only be a broken frame.
        this.queue.push(new Int16Array(event.data, 0, Math.floor(event.data.byteLength / 2)));
      }
    };
  }

  process(inputs, outputs) {
    const output = outputs[0][0];
    let played = false;
    for (let i = 0; i < output.length; i++) {
      if (!this.current || this.position >= this.current.length) {
        this.current = this.queue.shift() || null;
        this.position = 0;
      }
      if (this.current) {
        output[i] = this.current[this.position++] / 32768;
        played = true;
      } else {
        output[i] = 0;
      }
    }
    if (this.playing && !played) {
      this.port.postMessage('idle');
    }
    this.playing = played;
    return true;
  }
}

registerProcessor('capture', CaptureProcessor);
registerProcessor('playback', PlaybackProcessor);
