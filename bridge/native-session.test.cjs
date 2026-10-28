'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { setTimeout: sleep } = require('node:timers/promises');
const { NativeSession } = require('./native-session.cjs');
function socket() {
  const s = new EventEmitter(); s.setNoDelay = () => s; s.setTimeout = () => s;
  s.destroy = () => { s.destroyed = true; }; return s;
}
function harness(transcribe) {
  const s = socket(), events = [];
  const session = new NativeSession({ id: 1, address: '127.0.0.1:12345', token: 'fake', model: 'fake', send: e => events.push(e), connect: () => s, transcribe });
  s.emit('connect'); return { s, events, session };
}
test('native capture preserves fragmented PCM and waits for both audio EOF and stop command', async () => {
  const received = [];
  const h = harness(async ({ pcmSource, onText }) => {
    for await (const b of pcmSource) { received.push(b); onText('বাংলা English', false); }
    return { completed: true, transcript: 'বাংলা English', sentBytes: Buffer.concat(received).length, grpcStatus: 0 };
  });
  h.s.emit('data', Buffer.from([1])); h.s.emit('data', Buffer.from([2,3,4,5])); h.s.emit('data', Buffer.from([6]));
  h.s.emit('end'); await sleep(5); assert.equal(h.events.some(e => e.type === 'result'), false);
  h.session.stop(); await h.session.result;
  assert.deepEqual(Buffer.concat(received), Buffer.from([1,2,3,4,5,6]));
  assert.equal(h.events.find(e => e.type === 'result').ok, true);
});
test('stop arriving before TCP EOF still drains every queued byte', async () => {
  let count = 0;
  const h = harness(async ({ pcmSource }) => { for await (const b of pcmSource) count += b.length; return { completed: true, transcript: 'done', sentBytes: count }; });
  h.session.stop(); h.s.emit('data', Buffer.from([7,8])); h.s.emit('end'); await h.session.result;
  assert.equal(count,2); assert.equal(h.events.at(-1).ok,true);
});
test('early cloud completion and mismatched byte counts cannot authorize paste', async () => {
  const h = harness(async () => ({ completed: true, transcript: 'partial', sentBytes: 0 }));
  h.s.emit('data', Buffer.from([1,2])); h.session.stop(); h.s.emit('end'); await h.session.result;
  assert.equal(h.events.find(e=>e.type==='result').ok,false);
});
test('cancellation cannot emit a later success and frees the audio socket', async () => {
  const h = harness(async ({ pcmSource }) => { for await (const b of pcmSource) { void b; } return { completed: true, transcript: 'stale', sentBytes: 0 }; });
  h.session.close(); await h.session.result;
  assert.equal(h.s.destroyed,true); assert.equal(h.events.some(e=>e.type==='result'),false);
});
test('odd final audio and remote audio addresses fail visibly', async () => {
  const h = harness(async ({ pcmSource }) => { for await (const b of pcmSource) { void b; } return { completed:false }; });
  h.s.emit('data', Buffer.from([1])); h.s.emit('end'); await h.session.result;
  assert.equal(h.events.some(e=>e.type==='session-error'),true);
  assert.throws(()=>new NativeSession({ id:1,address:'example.com:443' }));
});
