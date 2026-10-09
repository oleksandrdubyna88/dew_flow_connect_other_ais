import assert from 'node:assert/strict';
import { test } from 'node:test';
import { commandsSkewNote } from '../commandsBlocks';
import { commandEdit } from '../commandsMessages';

/**
 * What the commands' host reads a message as, and what it tells an older server — decided without a page.
 *
 * <p>These sat beside the Gate commands tab's own tests until E5.1 step 4 of research/PLAN_one_model_catalog.md deleted the
 * tab; the parser is the one Reviews › Commands on the Settings page posts through (`commandsEmbed.ts`), and what the
 * tab DREW and POSTED is asked of that place in `commandsOnTheNewPage.test.ts`.</p>
 */

test('the parser ignores what the page never sends', () => {
  for (const message of [null, 'add', {}, { type: 'nope' }, { type: 'switch', id: 'x', value: 'yes' }, { type: 'restage', id: 'x', value: 'weekly' }]) {
    assert.deepEqual(commandEdit(message), { kind: 'ignore' }, JSON.stringify(message));
  }
});

test('an older server is told it ignores all of this; a new one and an unknown one say nothing', () => {
  assert.match(commandsSkewNote('0.32.0'), /ignores these texts and commands.*0\.33\.0/);
  assert.equal(commandsSkewNote('0.33.0'), '');
  assert.equal(commandsSkewNote(''), '');
});
