'use strict';
const { toWav } = require('./wav.cjs');
const { splitPcm } = require('./split.cjs');
// Sarvam Saaras, the short-file REST endpoint (30 s per request): long recordings are cut at quiet points and joined.
const ENDPOINT = 'https://api.sarvam.ai/speech-to-text';
function validKey(key) { return typeof key === 'string' && /^[\x21-\x7e]{8,512}$/.test(key); }
async function transcribeSarvam({ apiKey, pcmSource, signal, onText = () => {}, fetchImpl = fetch }) {
  if (!validKey(apiKey)) throw Error('Invalid Sarvam key');
  const parts = []; let sentBytes = 0;
  for await (const chunk of pcmSource) { parts.push(chunk); sentBytes += chunk.length; }
  const cancelled = { completed: false, empty: false, transcript: '', sentBytes, grpcStatus: null };
  if (signal?.aborted) return cancelled;
  let transcript = '';
  for (const piece of splitPcm(Buffer.concat(parts))) {
    if (signal?.aborted) return { ...cancelled, transcript };
    const form = new FormData();
    form.set('file', new Blob([toWav(piece)], { type: 'audio/wav' }), 'speech.wav');
    form.set('model', 'saaras:v4'); form.set('language_code', 'bn-IN'); form.set('mode', 'codemix');
    const limit = AbortSignal.timeout(30000);
    const response = await fetchImpl(ENDPOINT, { method: 'POST', headers: { 'api-subscription-key': apiKey }, body: form, redirect: 'error', signal: signal ? AbortSignal.any([signal, limit]) : limit });
    if (!response.ok) return { completed: false, empty: false, transcript, sentBytes, grpcStatus: response.status };
    const text = String((await response.json())?.transcript || '').trim();
    if (text) { transcript = transcript ? transcript + ' ' + text : text; onText(transcript, false); }
  }
  return { completed: transcript.length > 0, empty: transcript.length === 0, transcript, sentBytes, grpcStatus: 0 };
}
module.exports = { ENDPOINT, validKey, transcribeSarvam };
