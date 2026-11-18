import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FEATURES } from '../binaryFeatures';
import { userLayerReader } from '../modelKeys';
import { envBlock, seedOverlay, settingsFrom } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * A window VS Code updated in place keeps the OLD settings registry until a reload, and that registry's default for
 * `coai.onExhausted` is `human` (parent § 4.6, U3). Nothing was chosen there, so nothing may be written and nothing frozen.
 * The stale registry is modelled as `merged` answering `human` and `inspect` answering only a `defaultValue`.
 */
const STALE = userLayerReader(() => 'human', () => ({ defaultValue: 'human' }));

test('NothingHeldWritesNoPolicy', () => {
  const settings = settingsFrom(STALE);

  const upToDate = envBlock(settings, DEFAULT_VENDORS, '', undefined, [FEATURES.onExhaustedAuto], true);
  const older = envBlock(settings, DEFAULT_VENDORS, '', undefined, [], true);

  assert.equal('COAI_ON_EXHAUSTED' in upToDate, false, 'a binary that reads auto is told nothing');
  assert.equal(older['COAI_ON_EXHAUSTED'], 'good_enough', 'a binary that cannot read auto gets the old default, never human');
});

test('ASeededOverlayFreezesNoDefault', () => {
  assert.equal(Object.prototype.hasOwnProperty.call(seedOverlay(STALE), 'onExhausted'), false);

  const chosen = userLayerReader(() => 'human', () => ({ defaultValue: 'human', globalValue: 'continue' }));

  assert.equal(seedOverlay(chosen)['onExhausted'], 'continue');
});
