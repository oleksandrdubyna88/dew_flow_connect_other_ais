import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { ARTEFACT_VERSION, copiedMessage } from '../claudeSnippet';

/**
 * A version only the source code knows is a version nobody has.
 *
 * <p>Reported by the operator, looking at the ⋯ menu: the snippet had been at v5 for a release and
 * the menu said "Copy the CLAUDE.md snippet" — so a person with v4 pasted in their repository had
 * no way to learn there was anything newer, and no reason to click. Two places say it now: the menu
 * item, read BEFORE the click, and the message after it, which is the only one that can compare
 * what you took with what you already had.</p>
 *
 * <p><b>Against `ARTEFACT_VERSION`, not `SNIPPET_VERSION` — the same defect came back through this
 * door.</b> The label was pinned to the gate rule's own marker, which is frozen at 5 by the
 * conventions migration baseline and therefore cannot move. So the menu went on saying `(v5)` across
 * three changes to what the clipboard carries, and the test was green the whole time: it was
 * comparing the label with a number that had no way of changing. The label names the version of the
 * ARTEFACT the click will give you, and this is what makes that checkable.</p>
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
    item!.title.includes(`(v${ARTEFACT_VERSION})`),
    `the menu says "${item!.title}" while the artefact it copies is v${ARTEFACT_VERSION} — a version `
      + 'in the code and not in the menu is one nobody can act on. A command title is static JSON the '
      + `editor reads before any of our code runs, so write "(v${ARTEFACT_VERSION})" into `
      + 'package.json by hand; this test is what stops the drift shipping.',
  );
});

test('the message after the click compares what you took with what you have', () => {
  const older = copiedMessage({ kind: 'older', behind: ['the consultant'], current: 5 });

  assert.ok(older.includes('the consultant'), older);
  assert.ok(older.includes('replace'), older);
  assert.ok(copiedMessage({ kind: 'current', current: 5 }).includes('already on it'));
  assert.ok(copiedMessage({ kind: 'unversioned', current: 5 }).includes('before the version marker'));
  assert.ok(copiedMessage({ kind: 'absent', current: 5 }).includes('paste it into'));
});

test('a repository that is AHEAD is told to keep what it has', () => {
  // The one case where copying is the wrong move: somebody updated the repository from a newer
  // build than this one. Pasting over it would be a downgrade nobody asked for.
  const message = copiedMessage({ kind: 'ahead', newer: ['the review gate'], current: 5 });

  assert.ok(message.includes('NEWER'));
  assert.ok(message.includes('Keep what you have'));
});

/**
 * And it names the ARTEFACT's version, in every state.
 *
 * <p>Asked for on the plan round: a regression that left one branch of `copiedMessage` reading
 * `SNIPPET_VERSION` would put "(v5) is on your clipboard" in the notification while the menu that
 * was just clicked said v6, and nothing else in the suite looks at that sentence.</p>
 */
test('every message names the version that just went on the clipboard', () => {
  const all = [
    copiedMessage({ kind: 'older', behind: ['the consultant'], current: ARTEFACT_VERSION }),
    copiedMessage({ kind: 'current', current: ARTEFACT_VERSION }),
    copiedMessage({ kind: 'ahead', newer: ['the consultant'], current: ARTEFACT_VERSION }),
    copiedMessage({ kind: 'unversioned', current: ARTEFACT_VERSION }),
    copiedMessage({ kind: 'absent', current: ARTEFACT_VERSION }),
  ];

  for (const message of all) {
    assert.ok(message.includes(`v${ARTEFACT_VERSION}`), message);
  }
});
