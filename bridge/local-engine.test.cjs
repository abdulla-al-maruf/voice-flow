'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pieces, transcribeLocal } = require('./local-engine.cjs');
const RATE = 16000;
function fakeAsr(texts) {
  const seen = [];
  return { seen, createStream: () => ({ acceptWaveform: ({ samples }) => seen.push(samples.length) }), decodeAsync: async () => ({ text: texts[seen.length - 1] ?? '' }) };
}
async function* source(...chunks) { for (const c of chunks) yield c; }
test('long audio is cut into pieces of at most 15 s at the quietest point, losing no sample', () => {
  const all = new Float32Array(RATE * 40).fill(0.5);
  all.fill(0, RATE * 12, RATE * 12 + 800);
  const parts = pieces(all);
  assert.ok(parts.every(p => p.length <= RATE * 15));
  assert.equal(parts.reduce((n, p) => n + p.length, 0), all.length);
  assert.ok(Math.abs(parts[0].length - (RATE * 12 + 160)) <= 320, 'first cut should land in the silent gap');
});
test('pieces are joined into one transcript and every byte is counted', async () => {
  const asr = fakeAsr(['আমি', 'ভালো আছি']), pcm = Buffer.alloc(RATE * 2 * 20, 1);
  const shown = [];
  const result = await transcribeLocal({ pcmSource: source(pcm.subarray(0, 1000), pcm.subarray(1000)), asr, onText: t => shown.push(t) });
  assert.equal(result.transcript, 'আমি ভালো আছি');
  assert.ok(result.completed && !result.empty && result.grpcStatus === 0);
  assert.equal(result.sentBytes, pcm.length);
  assert.equal(asr.seen.length, 2);
  assert.equal(shown.at(-1), 'আমি ভালো আছি');
});
test('silence gives an empty result, not a failure', async () => {
  const result = await transcribeLocal({ pcmSource: source(Buffer.alloc(3200)), asr: fakeAsr(['']) });
  assert.ok(result.empty && !result.completed && result.grpcStatus === 0);
});
test('a cancelled session returns no result', async () => {
  const abort = new AbortController(); abort.abort();
  const result = await transcribeLocal({ pcmSource: source(Buffer.alloc(3200)), signal: abort.signal, asr: fakeAsr(['কিছু']) });
  assert.ok(!result.completed && !result.empty);
});
