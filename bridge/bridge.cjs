'use strict';
const readline = require('node:readline');
const { NativeSession } = require('./native-session.cjs');
const { engineFor } = require('./engines.cjs');
const send = data => process.stdout.write(JSON.stringify(data) + '\n');
let active;
// Public edition: no account sign-in. The app only needs a ready helper; each engine checks its own files or key.
function command(data) {
  if (!data || typeof data.type !== 'string') throw Error('Invalid command');
  if (data.type === 'signin' || data.type === 'resume' || data.type === 'signout') { send({ type: 'auth', ready: false }); return; }
  if (data.type === 'start') {
    if (!Number.isSafeInteger(data.id) || data.id < 1) throw Error('Invalid session');
    if (active && !active.closed) throw Error('Previous session is active');
    try {
      const engine = engineFor(data.engine);
      if (!engine.ready(data.apiKey)) throw Error('Voice engine files or key are missing');
      const session = new NativeSession({ id: data.id, address: data.address, send, transcribe: engine.transcribe, apiKey: engine.needsKey ? data.apiKey : undefined });
      active = session;
    } catch { send({ type: 'session-error', id: data.id, reason: 'Voice engine failed to start' }); }
    return;
  }
  if (data.type === 'stop' && active?.id === data.id) { active.stop(); return; }
  if (data.type === 'cancel' && active?.id === data.id) { active.close(); active = undefined; return; }
  if (data.type === 'quit') { active?.close(); process.exit(0); }
}
const lines = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
lines.on('line', line => {
  if (line.length > 32768) { send({ type: 'error', reason: 'Invalid command length' }); return; }
  try { command(JSON.parse(line)); } catch { send({ type: 'error', reason: 'Invalid command' }); }
});
lines.on('close', () => { active?.close(); process.exit(0); });
process.on('uncaughtException', () => { send({ type: 'error', reason: 'Voice helper stopped' }); active?.close(); process.exit(1); });
process.on('unhandledRejection', () => { send({ type: 'error', reason: 'Voice helper failed' }); active?.close(); process.exit(1); });
send({ type: 'boot' });
