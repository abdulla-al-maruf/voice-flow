'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { splitPcm } = require('./split.cjs');
const { readWav, toWav } = require('./wav.cjs');
const { transcribeSarvam, ENDPOINT: SARVAM } = require('./sarvam-engine.cjs');
const { transcribeChariot } = require('./chariot-engine.cjs');
const { engineFor, engines } = require('./engines.cjs');
const KEY = 'test-key-1234567890';
async function* source(...chunks) { for (const c of chunks) yield c; }
const second = n => Buffer.alloc(32000 * n, 1);

test('splitPcm cuts long audio at the quiet gap and loses nothing', () => {
  const pcm = Buffer.alloc(32000 * 60, 2); pcm.fill(0, 32000 * 24, 32000 * 24 + 640);
  const parts = splitPcm(pcm);
  assert.ok(parts.every(p => p.length <= 32000 * 28 && p.length % 2 === 0));
  assert.equal(parts.reduce((n, p) => n + p.length, 0), pcm.length);
  assert.ok(Math.abs(parts[0].length - (32000 * 24 + 320)) <= 640, 'first cut should land in the silent gap');
});
test('toWav output is a valid 16 kHz mono WAV', () => {
  const pcm = second(1);
  assert.deepEqual(readWav(toWav(pcm)).pcm, pcm);
});
test('Sarvam sends each piece with the key, model and Bangla language, and joins the text', async () => {
  const calls = [], texts = ['প্রথম অংশ', 'দ্বিতীয় অংশ'];
  const fetchImpl = async (url, options) => { calls.push({ url, options }); return { ok: true, json: async () => ({ transcript: texts[calls.length - 1] }) }; };
  const shown = [];
  const result = await transcribeSarvam({ apiKey: KEY, pcmSource: source(second(20), second(15)), fetchImpl, onText: t => shown.push(t) });
  assert.equal(calls.length, 2);
  assert.equal(calls[0].url, SARVAM);
  assert.equal(calls[0].options.headers['api-subscription-key'], KEY);
  const form = calls[0].options.body;
  assert.equal(form.get('model'), 'saaras:v4'); assert.equal(form.get('language_code'), 'bn-IN'); assert.equal(form.get('mode'), 'codemix');
  assert.equal(result.transcript, 'প্রথম অংশ দ্বিতীয় অংশ');
  assert.ok(result.completed && result.grpcStatus === 0 && result.sentBytes === 32000 * 35);
  assert.equal(shown.at(-1), 'প্রথম অংশ দ্বিতীয় অংশ');
});
test('Sarvam reports a refused key as a failed result, not a transcript', async () => {
  const result = await transcribeSarvam({ apiKey: KEY, pcmSource: source(second(2)), fetchImpl: async () => ({ ok: false, status: 403 }) });
  assert.ok(!result.completed && !result.empty && result.grpcStatus === 403);
});
test('Sarvam silence is empty and a bad key is refused before any request', async () => {
  const empty = await transcribeSarvam({ apiKey: KEY, pcmSource: source(second(1)), fetchImpl: async () => ({ ok: true, json: async () => ({ transcript: '' }) }) });
  assert.ok(empty.empty && !empty.completed);
  await assert.rejects(transcribeSarvam({ apiKey: 'short', pcmSource: source(second(1)), fetchImpl: async () => { throw Error('must not be called'); } }), /Invalid Sarvam key/);
});

class FakeSocket extends EventTarget {
  static last; sent = []; bufferedAmount = 0; closed = false;
  constructor(url) { super(); this.url = url; FakeSocket.last = this; }
  send(data) { this.sent.push(data); }
  close() { this.closed = true; }
  say(object) { this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(object) })); }
}
test('Chariot connects with Bangla settings, waits for session.started, streams audio, then finishes', async () => {
  const shown = [];
  const run = transcribeChariot({ apiKey: KEY, pcmSource: source(second(1)), WebSocketImpl: FakeSocket, onText: t => shown.push(t) });
  const socket = FakeSocket.last, url = new URL(socket.url);
  assert.equal(url.host, 'api.chariot.in');
  assert.equal(url.searchParams.get('language'), 'bn'); assert.equal(url.searchParams.get('audio_format'), 'pcm_16000'); assert.equal(url.searchParams.get('api_key'), KEY);
  await new Promise(r => setTimeout(r, 10));
  assert.equal(socket.sent.length, 0, 'no audio before session.started');
  socket.say({ type: 'session.started' }); await new Promise(r => setTimeout(r, 30));
  const audio = socket.sent.filter(x => typeof x !== 'string');
  assert.equal(audio.reduce((n, a) => n + a.length, 0), 32000);
  assert.equal(JSON.parse(socket.sent.at(-1)).type, 'input.done');
  socket.say({ type: 'transcript.delta', text: ' আমি' }); socket.say({ type: 'transcript.delta', text: ' ভালো' });
  assert.equal(shown.at(-1), 'আমি ভালো');
  socket.say({ type: 'transcript.done', text: 'আমি ভালো আছি।', audio_seconds: 1 });
  const result = await run;
  assert.equal(result.transcript, 'আমি ভালো আছি।');
  assert.ok(result.completed && result.grpcStatus === 0 && result.sentBytes === 32000 && socket.closed);
});
test('Chariot joins committed segments and the closing segment', async () => {
  const run = transcribeChariot({ apiKey: KEY, pcmSource: source(second(1)), WebSocketImpl: FakeSocket });
  const socket = FakeSocket.last; socket.say({ type: 'session.started' }); await new Promise(r => setTimeout(r, 20));
  socket.say({ type: 'transcript.committed', segment_id: 0, text: 'এক' }); socket.say({ type: 'transcript.done', text: 'দুই' });
  assert.equal((await run).transcript, 'এক দুই');
});
test('Chariot error, early close and cancel never produce a transcript', async () => {
  let run = transcribeChariot({ apiKey: KEY, pcmSource: source(second(1)), WebSocketImpl: FakeSocket });
  FakeSocket.last.say({ type: 'error', code: 4402, message: 'Insufficient credits' });
  let result = await run; assert.ok(!result.completed && !result.empty);
  run = transcribeChariot({ apiKey: KEY, pcmSource: source(second(1)), WebSocketImpl: FakeSocket });
  FakeSocket.last.dispatchEvent(new Event('close'));
  result = await run; assert.ok(!result.completed && !result.empty);
  const abort = new AbortController();
  run = transcribeChariot({ apiKey: KEY, pcmSource: source(second(1)), WebSocketImpl: FakeSocket, signal: abort.signal });
  abort.abort(); result = await run; assert.ok(!result.completed);
});
test('engines need their key: paid engines refuse a missing or short key, local and Google do not', () => {
  assert.ok(!engineFor('sarvam').ready(undefined) && !engineFor('chariot').ready('x'));
  assert.ok(engineFor('sarvam').ready(KEY) && engineFor('chariot').ready(KEY));
  if (engines.google) assert.ok(engineFor('google').ready());
  assert.equal(engineFor('sarvam').needsKey, true); assert.equal(engineFor('local').needsKey, false);
});
