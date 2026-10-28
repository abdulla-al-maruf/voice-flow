'use strict';
const { validKey } = require('./sarvam-engine.cjs');
// Chariot-1-ASR Realtime over its streaming WebSocket. Node's WebSocket cannot set headers, so the key goes in the query (documented fallback).
const ENDPOINT = 'wss://api.chariot.in/v1/asr/ws';
const FRAME = 3200;
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
function transcribeChariot({ apiKey, pcmSource, signal, onText = () => {}, timeoutMs = 340000, WebSocketImpl = globalThis.WebSocket }) {
  if (!validKey(apiKey)) throw Error('Invalid Chariot key');
  const query = new URLSearchParams({ audio_format: 'pcm_16000', language: 'bn', mode: 'high_accuracy', style: 'codemix', api_key: apiKey });
  return new Promise(resolve => {
    const ws = new WebSocketImpl(ENDPOINT + '?' + query);
    let sentBytes = 0, done = false, started = false, committed = '', partial = '';
    const finish = (result) => {
      if (done) return; done = true; clearTimeout(timer); signal?.removeEventListener('abort', abort);
      try { ws.close(); } catch { }
      resolve({ completed: false, empty: false, transcript: '', sentBytes, grpcStatus: null, ...result });
    };
    const abort = () => finish({});
    const timer = setTimeout(() => finish({}), timeoutMs);
    if (signal?.aborted) { finish({}); return; }
    signal?.addEventListener('abort', abort, { once: true });
    async function sendAudio() {
      try {
        for await (const chunk of pcmSource) {
          for (let at = 0; at < chunk.length; at += FRAME) {
            if (done) return;
            while (ws.bufferedAmount > 1 << 20) await pause(10);
            const frame = chunk.subarray(at, at + FRAME); ws.send(frame); sentBytes += frame.length;
          }
        }
        if (!done) ws.send(JSON.stringify({ type: 'input.done' }));
      } catch { finish({}); }
    }
    ws.addEventListener('message', event => {
      if (typeof event.data !== 'string') return;
      let message; try { message = JSON.parse(event.data); } catch { return; }
      if (message.type === 'session.started' && !started) { started = true; void sendAudio(); }
      else if (message.type === 'transcript.delta') { partial += String(message.text || ''); onText((committed + partial).trim(), false); }
      else if (message.type === 'transcript.committed') { committed += (committed ? ' ' : '') + String(message.text || '').trim(); partial = ''; onText(committed, false); }
      else if (message.type === 'transcript.done') {
        const last = String(message.text || '').trim(), transcript = [committed.trim(), last].filter(Boolean).join(' ');
        finish({ completed: transcript.length > 0, empty: transcript.length === 0, transcript, grpcStatus: 0 });
      } else if (message.type === 'error' && message.code !== undefined) finish({});
    });
    ws.addEventListener('error', () => finish({}));
    ws.addEventListener('close', () => finish({}));
  });
}
module.exports = { ENDPOINT, transcribeChariot };
