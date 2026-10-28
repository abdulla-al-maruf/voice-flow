'use strict';
class AudioQueue {
  items = []; bytes = 0; totalBytes = 0; ended = false; wake;
  constructor(limit = 32000 * 125) { this.limit = limit; }
  push(chunk) {
    if (this.ended) throw Error('Audio queue is closed');
    if (!Buffer.isBuffer(chunk) || !chunk.length || chunk.length % 2) throw Error('Invalid PCM chunk');
    if (this.totalBytes + chunk.length > this.limit) throw Error('Audio duration limit reached');
    this.items.push(chunk); this.bytes += chunk.length; this.totalBytes += chunk.length;
    this.wake?.(); this.wake = undefined;
  }
  end() { this.ended = true; this.wake?.(); this.wake = undefined; }
  discard() { this.items.length = 0; this.bytes = 0; this.end(); }
  async *[Symbol.asyncIterator]() {
    while (true) {
      while (this.items.length) { const chunk = this.items.shift(); this.bytes -= chunk.length; yield chunk; }
      if (this.ended) return;
      await new Promise(resolve => { this.wake = resolve; });
    }
  }
}
module.exports = { AudioQueue };
