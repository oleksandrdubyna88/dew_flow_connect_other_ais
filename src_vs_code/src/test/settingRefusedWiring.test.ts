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

const SRC = path.join(__dirname, '..', '..', 'src');

const source = (file: string): string => fs.readFileSync(path.join(SRC, file), 'utf8');

/**
 * Every production source file, RECURSIVELY.
 *
 * <p>A flat `readdirSync` was the first version, and a reviewer pointed out what it cannot see: a
 * panel added at `src/panels/newPanel.ts` calls `saveSetting`, never reports its rejection, and the
 * guard below stays green. `src/` already has subdirectories, so this is a matter of when rather
 * than whether. `test` and `generated` are skipped — the first is this file's own neighbours, the
 * second is not written by hand.</p>
 */
function sourceFiles(dir = ''): string[] {
  return fs.readdirSync(path.join(SRC, dir), { withFileTypes: true }).flatMap((entry) => {
    const here = dir === '' ? entry.name : `${dir}/${entry.name}`;
    if (entry.isDirectory()) {
      return ['test', 'generated'].includes(entry.name) ? [] : sourceFiles(here);
    }

    return entry.name.endsWith('.ts') ? [here] : [];
  });
}

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
  // The enumeration `saveSetting`'s doc makes, asserted rather than promised: a caller that appears
  // without a report turns this red and names it.
  //
  // `extension.ts` is the fourth, and it takes the panel's shape — catch locally, report, and return
  // without going on. That last part is the whole reason it is not the other shape: the block it
  // would otherwise copy next is built from the choice, so a refusal it walked past would put a
  // directory in somebody's client entry that this window is not using.
  const callers = ['phrasesPanel.ts', 'rolesPanel.ts', 'panelProvider.ts', 'extension.ts'];
  for (const file of callers) {
    assert.match(
      source(file),
      /reportRefusal\(/u,
      `${file} calls saveSetting but never reports a refusal — a rejection with nowhere to go is the `
      + 'silence this whole change was about',
    );
  }

  const everywhere = sourceFiles()
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

test('the reload is offered at most once per WINDOW, and the gate is in the shared reporter', () => {
  // It began as a flag inside the phrases tab, which left "at most once" true for exactly one of the
  // three callers: two failed role edits in a stale window raised two notifications.
  const door = source('sideConfig.ts');

  assert.match(door, /let reloadOffered = false/u, 'the once-per-window gate is not in the reporter');
  assert.match(
    door,
    /refusalNotice\(refusalFor\(context, key, error\), sentences, reloadOffered\)/u,
    'the gate is declared but never consulted',
  );
  assert.match(door, /reloadOffered = true;/u, 'nothing ever closes the gate, so the action repeats forever');
  // And it gates the ACTION, never the message. An early `if (reloadOffered) return` stood here for
  // one round and made a second refusal say nothing at all — which, for the two callers with no
  // banner, is silence about a save that did not happen.
  assert.doesNotMatch(
    door,
    /if \(reloadOffered\)/u,
    'the whole notification is gated again, not just the button it carries',
  );
  assert.doesNotMatch(
    source('phrasesPanel.ts'),
    /reloadOffered/u,
    'the phrases tab still keeps its own copy of the gate, which is the per-caller version of it',
  );
});

// The promise a reload cannot keep is asserted where it belongs — against the TEXT the module
// produces, in settingRefused.test.ts ('the cure is offered without pretending it is free'). It was
// briefly asserted here as well, against the source, and that copy failed on the doc comment
// explaining why the phrase had been removed: a source-level check cannot tell a sentence the code
// SAYS from one it talks ABOUT. The behavioural assertion has no such blind spot.

test('the repaint is in a then BEFORE the catch, never a finally', () => {
  // The specific shape a reviewer named: a queue that repaints in `finally` still replaces the typed
  // text however faithfully the refusal is reported.
  const queue = source('settledWrites.ts');

  assert.match(queue, /\.then\(\(again\) => \(again \? options\.render\(\) : undefined\)\)/u);
  assert.doesNotMatch(queue, /\.finally\(/u, 'a finally would repaint on the failure path too');
});
