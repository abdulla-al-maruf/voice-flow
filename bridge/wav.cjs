'use strict';
function readWav(b) {
  if (b.length < 12 || b.toString('ascii', 0, 4) !== 'RIFF' || b.toString('ascii', 8, 12) !== 'WAVE') throw Error('Expected RIFF WAVE');
  const end = b.readUInt32LE(4) + 8;
  if (end !== b.length) throw Error('Truncated or trailing RIFF data');
  let fmt, pcm;
  for (let p = 12; p < end;) {
    if (p + 8 > end) throw Error('Truncated WAV header');
    const id = b.toString('ascii', p, p + 4), size = b.readUInt32LE(p + 4); p += 8;
    if (p + size > end) throw Error('Truncated WAV chunk');
    if (id === 'fmt ') {
      if (fmt || size < 16) throw Error('Invalid WAV format chunk');
      fmt = b.subarray(p, p + size);
    }
    if (id === 'data') { if (pcm) throw Error('Multiple WAV data chunks'); pcm = b.subarray(p, p + size); }
    p += size + size % 2;
    if (p > end) throw Error('Missing WAV padding');
  }
  if (!fmt || !pcm?.length) throw Error('Missing WAV format or audio');
  if (fmt.readUInt16LE(0) !== 1 || fmt.readUInt16LE(2) !== 1 || fmt.readUInt32LE(4) !== 16000 || fmt.readUInt32LE(8) !== 32000 || fmt.readUInt16LE(12) !== 2 || fmt.readUInt16LE(14) !== 16 || pcm.length % 2) throw Error('WAV must be 16 kHz, mono, signed PCM16');
  if (pcm.length > 32000 * 120) throw Error('PoC audio limit is two minutes');
  return { pcm, durationMs: pcm.length / 32 };
}
function* chunks(pcm, size = 640) {
  if (!Number.isInteger(size) || size < 2 || size % 2) throw Error('Invalid PCM chunk size');
  for (let p = 0; p < pcm.length; p += size) yield pcm.subarray(p, p + size);
}
// 16 kHz mono PCM16 wrapped in a WAV header, for services that take a file.
function toWav(pcm) {
  const header = Buffer.alloc(44);
  header.write('RIFF', 0, 'ascii'); header.writeUInt32LE(36 + pcm.length, 4); header.write('WAVEfmt ', 8, 'ascii');
  header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22); header.writeUInt32LE(16000, 24);
  header.writeUInt32LE(32000, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34); header.write('data', 36, 'ascii'); header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}
module.exports = { readWav, chunks, toWav };
