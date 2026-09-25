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

/**
 * Every module that attaches tooltips: the panel, and any section that imports `help` from
 * `panelControls` — read off the imports, so a section moved out of `panelView.ts` is not a tooltip
 * this test stops seeing. A key reaches one by name or by literal.
 */
const source = path.join(__dirname, '..', '..', 'src');
const attaching = fs.readdirSync(source)
  .filter((f) => f.endsWith('.ts'))
  .filter((f) => f === 'panelView.ts'
    || /import \{[^}]*\bhelp\b[^}]*\} from '\.\/panelControls'/.test(fs.readFileSync(path.join(source, f), 'utf8')));
const panel = attaching.map((f) => fs.readFileSync(path.join(source, f), 'utf8')).join('\n');

test('the modules that attach tooltips are found — a search that finds only one has stopped working', () => {
  assert.ok(attaching.includes('panelView.ts'), attaching.join(', '));
  assert.ok(attaching.includes('cadenceSettings.ts'), attaching.join(', '));
});

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
