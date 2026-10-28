'use strict';
const net = require('node:net');
const { AudioQueue } = require('./audio-queue.cjs');
class NativeSession {
  constructor({ id, address, token, model, send, transcribe, apiKey, connect = opts => net.createConnection(opts) }) {
    if (!Number.isSafeInteger(id) || id < 1 || !/^127\.0\.0\.1:[0-9]{1,5}$/.test(address)) throw Error('Invalid capture session');
    const port = Number(address.split(':')[1]); if (port < 1 || port > 65535) throw Error('Invalid capture port');
    this.id = id; this.send = send; this.queue = new AudioQueue(32000 * 310); this.abort = new AbortController();
    this.stopping = false; this.audioEnded = false; this.closed = false; this.carry = Buffer.alloc(0);
    this.socket = connect({ host: '127.0.0.1', port }); this.socket.setNoDelay(true);
    this.socket.setTimeout(15000);
    this.socket.on('connect', () => { this.socket.setTimeout(0); send({ type: 'connected', id }); });
    this.socket.on('timeout', () => this.fail('Audio connection timed out'));
    this.socket.on('error', () => this.fail('Audio connection failed'));
    this.socket.on('close', () => { if (!this.audioEnded && !this.closed) this.fail('Audio connection closed unexpectedly'); });
    this.socket.on('data', data => {
      if (this.closed) return;
      try {
        const combined = this.carry.length ? Buffer.concat([this.carry, data]) : data;
        const length = combined.length - combined.length % 2;
        if (length) this.queue.push(combined.subarray(0, length));
        this.carry = combined.subarray(length);
      } catch { this.fail('Audio buffer limit reached'); }
    });
    this.socket.on('end', () => {
      this.audioEnded = true;
      if (this.carry.length) { this.fail('Partial final audio sample'); return; }
      if (this.stopping) this.queue.end();
      else this.eofTimer = setTimeout(() => this.fail('Capture ended before stop confirmation'), 5000);
    });
    this.result = transcribe({ token, model, apiKey, pcmSource: this.queue, signal: this.abort.signal, timeoutMs: 340000,
      onText: (text, finished) => { if (!this.closed) send({ type: 'text', id, text, finished }); }
    }).then(result => {
      if (this.closed) return;
      const drained = this.stopping && this.audioEnded && result.sentBytes === this.queue.totalBytes;
      const ok = result.completed && drained;
      const empty = result.empty === true && drained && result.grpcStatus === 0 && !result.transcript;
      send({ type: 'result', id, ok, empty, text: result.transcript || '', sentBytes: result.sentBytes, audioBytes: this.queue.totalBytes, grpcStatus: result.grpcStatus });
      this.close();
    }).catch(() => this.fail('Voice connection failed'));
  }
  stop() {
    if (this.closed) return;
    this.stopping = true; clearTimeout(this.eofTimer);
    if (this.audioEnded) this.queue.end();
  }
  fail(reason) { if (this.closed) return; this.send({ type: 'session-error', id: this.id, reason }); this.close(); }
  close() {
    if (this.closed) return; this.closed = true; clearTimeout(this.eofTimer);
    this.abort.abort(); this.queue.discard(); this.socket.destroy();
  }
}
module.exports = { NativeSession };
