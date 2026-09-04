import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { copiedMessage, SNIPPET_VERSION } from '../claudeSnippet';

/**
 * A version only the source code knows is a version nobody has.
 *
 * <p>Reported by the operator, looking at the ⋯ menu: the snippet had been at v5 for a release and
 * the menu said "Copy the CLAUDE.md snippet" — so a person with v4 pasted in their repository had
 * no way to learn there was anything newer, and no reason to click. Two places say it now: the menu
 * item, read BEFORE the click, and the message after it, which is the only one that can compare
 * what you took with what you already had.</p>
 */

// From the working directory, which `npm test` sets to the extension root — the compiled tests run
// as CommonJS, so `import.meta` is not available here.
const manifest = JSON.parse(readFileSync(join(process.cwd(), 'package.json'), 'utf8')) as {
  contributes: { commands: { command: string; title: string }[] };
};

test('the menu item names the version it will give you', () => {
  const item = manifest.contributes.commands.find((c) => c.command === 'coai.copyClaudeSnippet');

  assert.ok(item !== undefined, 'the command is still registered');
  assert.ok(
    item!.title.includes(`(v${SNIPPET_VERSION})`),
    `the menu says "${item!.title}" while the snippet is v${SNIPPET_VERSION} — a version in the code `
      + 'and not in the menu is one nobody can act on',
  );
});

test('the message after the click compares what you took with what you have', () => {
  assert.ok(copiedMessage({ kind: 'older', found: 4, current: 5 }).includes('v4'));
  assert.ok(copiedMessage({ kind: 'older', found: 4, current: 5 }).includes('replace'));
  assert.ok(copiedMessage({ kind: 'current', current: 5 }).includes('already on it'));
  assert.ok(copiedMessage({ kind: 'unversioned', current: 5 }).includes('before the version marker'));
  assert.ok(copiedMessage({ kind: 'absent', current: 5 }).includes('paste it into'));
});

test('a repository that is AHEAD is told to keep what it has', () => {
  // The one case where copying is the wrong move: somebody updated the repository from a newer
  // build than this one. Pasting over it would be a downgrade nobody asked for.
  const message = copiedMessage({ kind: 'ahead', found: 6, current: 5 });

  assert.ok(message.includes('NEWER'));
  assert.ok(message.includes('Keep what you have'));
});

test('every message names the version that just went on the clipboard', () => {
  const all = [
    copiedMessage({ kind: 'older', found: 4, current: SNIPPET_VERSION }),
    copiedMessage({ kind: 'current', current: SNIPPET_VERSION }),
    copiedMessage({ kind: 'ahead', found: 9, current: SNIPPET_VERSION }),
    copiedMessage({ kind: 'unversioned', current: SNIPPET_VERSION }),
    copiedMessage({ kind: 'absent', current: SNIPPET_VERSION }),
  ];

  for (const message of all) {
    assert.ok(message.includes(`v${SNIPPET_VERSION}`), message);
  }
});
