'use strict';
const { modelReady, transcribeLocal } = require('./local-engine.cjs');
const { validKey, transcribeSarvam } = require('./sarvam-engine.cjs');
const { transcribeChariot } = require('./chariot-engine.cjs');
// Public edition: only the offline model and the user's own paid keys.
const engines = {
  local: { needsKey: false, transcribe: transcribeLocal, ready: () => modelReady() },
  sarvam: { needsKey: true, transcribe: transcribeSarvam, ready: validKey },
  chariot: { needsKey: true, transcribe: transcribeChariot, ready: validKey }
};
function engineFor(name = 'local') {
  if (typeof name !== 'string' || !Object.hasOwn(engines, name)) throw Error('Unknown voice engine');
  return engines[name];
}
module.exports = { engines, engineFor };
