'use strict';
const path = require('node:path');
const fs = require('node:fs');
// Offline Bangla model (SayedShaun/bangla-zipformer-onnx, int8, CC BY-SA 4.0), downloaded into Voice Flow's own data folder.
const MODEL_DIR = path.join(process.env.LOCALAPPDATA || '', 'VoiceFlow', 'Models', 'bangla-zipformer-int8');
const FILES = ['encoder.onnx', 'decoder.onnx', 'joiner.onnx', 'tokens.txt'];
const RATE = 16000, MAX_PIECE = RATE * 15, MIN_PIECE = RATE * 10, WINDOW = RATE / 50;
let cached;
function modelReady(dir = MODEL_DIR) { return FILES.every(file => fs.existsSync(path.join(dir, file))); }
function recognizer(dir) {
  if (cached?.dir === dir) return cached.asr;
  const { OfflineRecognizer } = require('sherpa-onnx-node');
  const asr = new OfflineRecognizer({ featConfig: { sampleRate: RATE, featureDim: 80 },
    modelConfig: { transducer: { encoder: path.join(dir, 'encoder.onnx'), decoder: path.join(dir, 'decoder.onnx'), joiner: path.join(dir, 'joiner.onnx') },
      tokens: path.join(dir, 'tokens.txt'), numThreads: 4, provider: 'cpu', debug: 0 },
    decodingMethod: 'modified_beam_search', maxActivePaths: 4 });
  cached = { dir, asr }; return asr;
}
function samples(pcm) { const out = new Float32Array(pcm.length / 2); for (let i = 0; i < out.length; i++) out[i] = pcm.readInt16LE(i * 2) / 32768; return out; }
// The model only takes ~20 s at a time: cut at the quietest 20 ms between 10 s and 15 s so no word is split.
function pieces(all) {
  const result = [];
  let start = 0;
  while (all.length - start > MAX_PIECE) {
    let best = start + MAX_PIECE, quietest = Infinity;
    for (let at = start + MIN_PIECE; at + WINDOW <= start + MAX_PIECE; at += WINDOW) {
      let energy = 0; for (let i = at; i < at + WINDOW; i++) energy += all[i] * all[i];
      if (energy < quietest) { quietest = energy; best = at + WINDOW / 2; }
    }
    result.push(all.subarray(start, best)); start = best;
  }
  if (all.length > start) result.push(all.subarray(start));
  return result;
}
async function transcribeLocal({ pcmSource, signal, onText = () => {}, dir = MODEL_DIR, asr }) {
  const parts = []; let sentBytes = 0;
  for await (const chunk of pcmSource) { parts.push(chunk); sentBytes += chunk.length; }
  if (signal?.aborted) return { completed: false, empty: false, transcript: '', sentBytes, grpcStatus: null };
  const all = samples(Buffer.concat(parts)), engine = asr || recognizer(dir);
  let transcript = '';
  for (const piece of pieces(all)) {
    if (signal?.aborted) return { completed: false, empty: false, transcript, sentBytes, grpcStatus: null };
    const stream = engine.createStream(); stream.acceptWaveform({ samples: piece, sampleRate: RATE });
    const text = String((await engine.decodeAsync(stream))?.text || '').trim();
    if (text) { transcript = transcript ? transcript + ' ' + text : text; onText(transcript, false); }
  }
  return { completed: transcript.length > 0, empty: transcript.length === 0, transcript, sentBytes, grpcStatus: 0 };
}
module.exports = { MODEL_DIR, modelReady, pieces, transcribeLocal };
