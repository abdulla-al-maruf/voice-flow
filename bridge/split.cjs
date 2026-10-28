'use strict';
// Cuts 16 kHz mono PCM16 into pieces of at most maxSeconds, always at the quietest 20 ms between minSeconds and maxSeconds, so no word is split.
function splitPcm(pcm, maxSeconds = 28, minSeconds = 20) {
  const RATE = 16000, max = RATE * 2 * maxSeconds, min = RATE * 2 * minSeconds, window = RATE / 50 * 2;
  const result = [];
  let start = 0;
  while (pcm.length - start > max) {
    let best = start + max, quietest = Infinity;
    for (let at = start + min; at + window <= start + max; at += window) {
      let energy = 0;
      for (let i = at; i < at + window; i += 2) { const s = pcm.readInt16LE(i); energy += s * s; }
      if (energy < quietest) { quietest = energy; best = at + window / 2 - (window / 2) % 2; }
    }
    result.push(pcm.subarray(start, best)); start = best;
  }
  if (pcm.length > start) result.push(pcm.subarray(start));
  return result;
}
module.exports = { splitPcm };
