import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { promptChosen } from '../promptsPerRound';

/**
 * The sidebar reads and writes THIS side's settings — the prompt-per-round picker included.
 *
 * <p>The defect: with *separate settings for each side* on, `PanelProvider` drew the page from
 * `settingsFrom((section) => config.get(section))` — the SHARED layer — and the prompt-per-round picker wrote with
 * `config.update(…, Global)`. So on a side with its own settings the picker changed every other side, and every
 * overlaid setting on the page (rounds, thresholds, the consultant, prompts per round…) showed the shared value
 * rather than the one this side runs with. The vendors alone went through the side-aware reader.</p>
 *
 * <p>The host half imports `vscode` and cannot be run here, so its WIRING is read — the precedent is
 * `settingRefusedWiring.test.ts` — and each assertion pins both halves of its condition: the door present AND
 * the bypass absent. The decision itself is a value, tested below as one.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');
const panel = (): string => fs.readFileSync(path.join(SRC, 'panelProvider.ts'), 'utf8');

/** One method's text, from its signature to the next member's doc comment. */
function body(source: string, signature: string): string {
  const start = source.indexOf(signature);
  assert.ok(start >= 0, `${signature} is gone, so this test asserts nothing about it`);
  const end = source.indexOf('\n  /**', start);

  return source.slice(start, end < 0 ? undefined : end);
}

test('the panel never reads its settings from the shared layer alone', () => {
  assert.doesNotMatch(
    panel(),
    /settingsFrom\(\(section\) => config\.get\(section\)\)/u,
    'a settings read that skips this side\'s overlay — the page then shows values this side does not run with',
  );
  assert.match(panel(), /settingsFrom\(this\.read\(config\)\)/u, 'and the side-aware reader is what the page reads through');
});

test('the prompt-per-round picker reads and writes through this side, and reports a refusal', () => {
  const choose = body(panel(), 'private async choosePrompt(');

  assert.doesNotMatch(choose, /config\.update\(/u, 'it writes around saveSetting, so a side\'s choice lands on every side');
  assert.match(choose, /this\.save\(config, 'promptsPerRound'/u, 'it does not write through the one save');
  assert.match(choose, /settingsFrom\(this\.read\(config\)\)/u, 'it does not read the side it writes to');
});

test('choosing a round\'s prompt pads the earlier rounds with "not chosen" and keeps every other role', () => {
  const current = { plan: ['a'], code: ['x', 'y'] };

  assert.deepEqual(promptChosen(current, 'plan', 3, 'c'), { plan: ['a', '', 'c'], code: ['x', 'y'] });
  assert.deepEqual(promptChosen(current, 'code', 1, 'z'), { plan: ['a'], code: ['z', 'y'] });
  assert.deepEqual(promptChosen({}, 'feature', 1, 'f'), { feature: ['f'] });
  assert.deepEqual(current, { plan: ['a'], code: ['x', 'y'] }, 'the stored value is never changed in place');
});
