import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The WIRING, which imports `vscode` and so can only be read.
 *
 * <p>Two gate reviewers asked for this in the same round, from opposite directions: one that the
 * plan never verified the phrases tab actually reaches `saveSetting`, and one that a pure classifier
 * can be perfectly tested while the shipped tab still calls its own `config.update` and still says
 * the wrong thing. Both are right — every other test in this change exercises a module that no
 * webview host is obliged to call.</p>
 *
 * <p><b>Each assertion pins BOTH halves of its condition</b>, because a structural test matching only
 * the presence of the new call survives its own defect: leaving the old write in place beside the new
 * one would pass it. So the absence of the bare update is asserted as well as the presence of the
 * shared door.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the phrases tab writes through the ONE door, and no longer has a bare config.update', () => {
  const host = source('phrasesPanel.ts');

  assert.match(
    host,
    /await saveSetting\(side\(\), config\(\), KEY, outcome\.rows\)/u,
    'the phrases tab does not write through sideConfig.saveSetting',
  );
  assert.doesNotMatch(
    host,
    /config\(\)\.update\(/u,
    'the phrases tab still has its own config.update — which is how it came to have its own idea of '
    + 'what a failure means, and a second copy of "which layer does this belong in"',
  );
});

test('every host that saves a setting reports the refusal, because saveSetting no longer swallows it', () => {
  // The enumeration `saveSetting`'s doc makes, asserted rather than promised: if a fourth caller
  // appears without a report, this goes red and names it.
  const callers = ['phrasesPanel.ts', 'rolesPanel.ts', 'panelProvider.ts'];
  for (const file of callers) {
    assert.match(
      source(file),
      /reportRefusal\(/u,
      `${file} calls saveSetting but never reports a refusal — a rejection with nowhere to go is the `
      + 'silence this whole change was about',
    );
  }

  const everywhere = fs
    .readdirSync(path.join(__dirname, '..', '..', 'src'))
    .filter((f) => f.endsWith('.ts'))
    .filter((f) => /\bsaveSetting\(/u.test(source(f)) && f !== 'sideConfig.ts');

  assert.deepEqual(
    [...everywhere].sort((a, b) => a.localeCompare(b)),
    [...callers].sort((a, b) => a.localeCompare(b)),
    'the set of saveSetting callers has changed — the new one must pick one of the two reporting '
    + 'shapes documented on saveSetting, and be listed here',
  );
});

test('saveSetting itself no longer catches, so the refusal can reach those callers at all', () => {
  const door = source('sideConfig.ts');
  const body = door.slice(door.indexOf('export async function saveSetting'), door.indexOf('const RELOAD'));

  assert.match(body, /await config\.update\(key, value, vscode\.ConfigurationTarget\.Global\)/u);
  assert.doesNotMatch(
    body,
    /catch/u,
    'saveSetting catches again — a caller then reads a resolved promise as a save that landed, and '
    + 'settledWrites repaints over the words that were never stored',
  );
});

test('the repaint is in a then BEFORE the catch, never a finally', () => {
  // The specific shape a reviewer named: a queue that repaints in `finally` still replaces the typed
  // text however faithfully the refusal is reported.
  const queue = source('settledWrites.ts');

  assert.match(queue, /\.then\(\(again\) => \(again \? options\.render\(\) : undefined\)\)/u);
  assert.doesNotMatch(queue, /\.finally\(/u, 'a finally would repaint on the failure path too');
});
