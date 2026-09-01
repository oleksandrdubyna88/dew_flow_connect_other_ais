import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { HELP } from '../help';

/**
 * Every tooltip is attached to a control that exists.
 *
 * <p>Written after three of them were not. Removing the translator took its two settings out of the
 * panel and out of the manifest, and left `language`, `translator` and `translatorModel` sitting in
 * the tooltip catalog describing controls nobody could see — text that reads as current
 * documentation and is not reachable from the product at all.</p>
 *
 * <p>The coverage test next door works the other way round: it fails when a control has no help.
 * This one fails when help has no control. A catalog needs both directions, because only one of
 * them is caught by using the product.</p>
 */

/** The panel is the only thing that attaches tooltips; a key reaches it by name or by literal. */
const panel = ['panelView.ts']
  .map((f) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', f), 'utf8'))
  .join('\n');

test('every tooltip key is attached to a control in the panel', () => {
  const orphans = Object.keys(HELP).filter(
    (key) => !panel.includes(`'${key}'`) && !panel.includes(`HELP.${key}`),
  );
  assert.deepEqual(
    orphans,
    [],
    `these tooltips describe controls that are not in the panel: ${orphans.join(', ')}. ` +
      'Attach them, or delete the text — help for a control nobody can reach is worse than none.',
  );
});
