import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  INVENTORY,
  SOURCE_EXTENSIONS,
  asText,
  count,
  everySourceFile,
} from '../../scripts/count-notifications.mjs';

/**
 * The published count of places this extension speaks to a person cannot drift.
 *
 * <p><b>Why this test exists, precisely.</b> The notifications plan promises that every message is
 * written down, and quotes a number in three documents. Its first draft published 109 call sites, a
 * class table summing to 113, and "95 events" — which is neither 109 − 16 nor 113 − 16 but the sum
 * of the table's first four rows. Every test was green, because no test knew what the number was.
 * The plan round found it by hand; this makes finding it by hand unnecessary.</p>
 *
 * <p>It runs BEFORE the compile, beside `prepareGate` and `claudeAdapter`, because it needs no
 * build: the script and this test are both ESM and read the source tree directly.</p>
 */

/**
 * The same text, however this checkout spells the end of a line.
 *
 * <p>The guard is about NUMBERS. It went red twice on a file nobody had touched, both times after a
 * rebase: the generator writes LF and git hands the file back with CRLF on Windows, and it then
 * considers the file clean because it normalises on comparison — so `git diff` is empty while a byte
 * comparison fails. A `.gitattributes` pin is in place and is still not enough, because a rebase
 * materialises the file at a step where that pin is not yet in the tree and git never rewrites an
 * already-checked-out file whose normalised content matches.</p>
 *
 * <p>That red is the worst kind. The test whose whole job is to say "a call site was added, moved or
 * removed" said it about content that had not changed, and the lesson a person takes from it is to
 * regenerate the artefact and move on — which is the reflex that makes the guard useless on the day
 * it is right.</p>
 */
function sameLines(text) {
  return text.split(String.fromCharCode(13)).join('');
}

test('the checked-in inventory is what the counter counts today', () => {
  const counted = count();
  const onDisk = readFileSync(INVENTORY, 'utf8');

  assert.equal(
    sameLines(asText(counted)),
    sameLines(onDisk),
    'run `node scripts/count-notifications.mjs --write` — a call site was added, moved or removed',
  );
});

test('the count is internally consistent, which the hand-written one was not', () => {
  const counted = count();
  const byApi = Object.values(counted.byApi).reduce((total, n) => total + n, 0);

  assert.equal(byApi, counted.direct, 'the per-API split must sum to the calls still made directly');
  assert.ok(counted.sites > 0, 'a counter that finds nothing would pass every other assertion here');
});

test('the totals are what a person counting by hand would get', () => {
  // The three assertions this replaces restated the definitions: `sites` IS `direct + routed` and
  // `events` IS `sites - modal`, so asserting those equalities could not fail whatever the counter
  // did to the inputs. A shared aggregation error would have sailed through all three. (CodeRabbit.)
  //
  // So the numbers here are written by hand from a fixture small enough to count by eye: two direct
  // calls in one file, three routed calls in another, one of them a modal question.
  const here = mkdtempSync(join(tmpdir(), 'coai-sites-'));
  try {
    writeFileSync(join(here, 'old.ts'), [
      'void vscode.window.showWarningMessage("one");',
      'void vscode.window.showErrorMessage("two");',
      '',
    ].join('\n'));
    writeFileSync(join(here, 'new.ts'), [
      "void notify({ as: 'warning', class: 'failure', source: 'x', code: 'a-thing-failed' });",
      "void notifyOnce({ as: 'information', class: 'outcome', source: 'x', code: 'a-thing-landed' });",
      "void notifyAndAsk({ as: 'warning', class: 'confirmation', source: 'x', code: 'really', modal: true });",
      '',
    ].join('\n'));

    const counted = count(here);

    assert.equal(counted.direct, 2);
    assert.equal(counted.routed, 3);
    assert.equal(counted.sites, 5);
    assert.equal(counted.modal, 1);
    assert.equal(counted.events, 4);
    assert.equal(counted.byApi.showWarningMessage, 1);
    assert.equal(counted.byApi.showErrorMessage, 1);
    assert.equal(counted.byApi.showInformationMessage, 0);
    assert.deepEqual([...counted.codes].sort(), ['a-thing-failed', 'a-thing-landed', 'really']);
  } finally {
    rmSync(here, { recursive: true, force: true });
  }
});

test('a `code` belongs to a notice, and an object that merely has a `code` property does not', () => {
  // Two maps in this tree have a `code` key and nothing to do with notifications: TAB_NAMES in
  // `rolesPage.ts` holds 'Code review', and LANGUAGES in `settingsShape.ts` holds de/en/es/ru/uk.
  // The first pattern collected the one, the shape-constrained second collected the other five -
  // into a list the artefact calls the suppression key space. Anchoring on the `source` that always
  // precedes a notice's `code` tells them apart. (CodeRabbit found the second.)
  const here = mkdtempSync(join(tmpdir(), 'coai-sites-'));
  try {
    writeFileSync(join(here, 'maps.ts'), [
      "const TAB_NAMES = { plan: 'Plan review', code: 'Code review' };",
      "const LANGUAGES = [{ code: 'en', label: 'English' }, { code: 'uk', label: 'Ukrainian' }];",
      "void notify({ as: 'warning', class: 'failure', source: 'real', code: 'the-only-one' });",
      '',
    ].join('\n'));

    assert.deepEqual([...count(here).codes], ['the-only-one']);
  } finally {
    rmSync(here, { recursive: true, force: true });
  }
});

/**
 * How many places this extension speaks to a person.
 *
 * <p>It started at 109 and is 111.</p>
 *
 * <ul>
 *   <li><b>110</b> — S3 SURFACES something never shown before: the server's own list of settings it
 *       could not understand, which crossed the wire on every probe and was dropped by the parser.
 *       A new message rather than a routed one.</li>
 *   <li><b>111</b> — the rebase onto main, 2026-09-17. Another lane shipped the copy
 *       acknowledgement's catch while this branch was routing, so main arrived carrying a message
 *       that had never been through the funnel. The ratchet went red on the rebase and named the
 *       file, which is the entire purpose of counting: a branch cannot quietly re-open the hole it
 *       is closing, and neither can the branch merging into it.</li>
 * </ul>
 *
 * <p>A new message is the only sanctioned way this number moves up, and each rise is written here
 * with its reason.</p>
 */
const PLACES_THIS_SPEAKS = 111;

test('the POPULATION changes only on purpose', () => {
  // Routing must not move it in either direction: the completeness promise is made over this
  // number, so it has to mean the same thing on the day the funnel lands as on the day the last
  // site is routed. An earlier version of the script made `sites` mean "still direct", and the
  // first routed modal quietly moved `events` from 93 to 89 — the population appearing to shrink
  // because the work was going well. Raising this constant is a deliberate act with a reason
  // beside it, which is what made the S3 addition visible instead of silent.
  assert.equal(count().sites, PLACES_THIS_SPEAKS, 'the number of places this extension speaks to a person');
});

/**
 * The ratchet. 109 call sites cannot be routed through the funnel in one commit, and a whitelist
 * with a hundred entries in it is a lie dressed as enforcement. So the number is allowed to FALL
 * and never to rise: a new direct call makes the drift test above red, and lowering this constant
 * is the only sanctioned way to change it.
 */
const MOST_DIRECT_CALLS_ALLOWED = 1;

test('no call site is added outside the funnel — the count only ever falls', () => {
  const counted = count();

  assert.ok(
    counted.direct <= MOST_DIRECT_CALLS_ALLOWED,
    `${counted.direct} direct calls, and the ratchet stands at ${MOST_DIRECT_CALLS_ALLOWED}. `
    + 'A new message goes through notify() — see notify.ts.',
  );
});

test('and the scan still finds a call it is SUPPOSED to find', () => {
  // The companion `testing.md` asks for beside every prohibition: a scan that matches nothing
  // passes for ever. The funnel calls the API on purpose, so if this reaches zero the scan has
  // stopped working rather than the product having stopped showing messages.
  assert.ok(count().inTheFunnel > 0, 'the scan no longer matches the funnel itself');
});

test('the per-file breakdown accounts for every site', () => {
  const counted = count();
  const perFile = Object.values(counted.perFile).reduce((total, n) => total + n, 0);

  assert.equal(perFile, counted.direct, 'the breakdown lists where the REMAINING direct calls are');
  assert.ok(
    Object.keys(counted.perFile).every((path) => !path.includes('/test/')),
    'the tests are not product call sites, and two of them assert on these very names',
  );
});

test('the scan sees every extension the compiler ships, not only .ts', () => {
  // A hole the code round found, and it was not hypothetical: a `.tsx` file calling
  // `window.showWarningMessage` would have left BOTH numbers unchanged — the population and the
  // ratchet — so the completeness promise would have gone on being made while being false. A scan
  // that decides what counts by extension has to know every extension `tsconfig.json` accepts.
  assert.deepEqual([...SOURCE_EXTENSIONS], ['.ts', '.tsx', '.mts', '.cts']);
});

test('a file in one of those extensions is actually picked up, not merely listed in a constant', () => {
  // The companion the previous test needs: a list of extensions nothing reads would pass it.
  const here = mkdtempSync(join(tmpdir(), 'coai-sites-'));
  try {
    writeFileSync(join(here, 'panel.tsx'), 'void vscode.window.showWarningMessage("hi");\n');
    writeFileSync(join(here, 'types.d.ts'), 'declare const x: number;\n');
    writeFileSync(join(here, 'notes.md'), 'vscode.window.showWarningMessage\n');

    const found = everySourceFile(here).map((path) => path.slice(here.length + 1));

    assert.deepEqual(found, ['panel.tsx'], 'the .tsx is source; a declaration file and a doc are not');
  } finally {
    rmSync(here, { recursive: true, force: true });
  }
});

test('the scan counts code and not prose, in both directions', () => {
  // Found by making a comment that says `vscode.window.showErrorMessage` while fixing something
  // else: the population went up by one. The scan had always counted the words in docstrings, and
  // this file's subject matter is those very words. It is worse than a nuisance — a comment
  // mentioning `notify(` inflates `routed` and therefore the POPULATION, which is the number the
  // completeness promise is made over.
  const here = mkdtempSync(join(tmpdir(), 'coai-sites-'));
  try {
    writeFileSync(
      join(here, 'talker.ts'),
      [
        '// A comment naming vscode.window.showErrorMessage and notify( on purpose.',
        '/* And a block one: .showWarningMessage, modal: true */',
        'const url = "https://example.com/a//b"; // a URL, whose // must not eat this line',
        'void vscode.window.showWarningMessage("the one real call");',
        '',
      ].join('\n'),
    );

    const [file] = everySourceFile(here);
    assert.ok(file !== undefined);
    const text = readFileSync(file, 'utf8');

    assert.equal((text.match(/\.showErrorMessage\b/gu) ?? []).length, 1, 'the raw text does hold the prose');
    assert.equal(count(here).byApi.showErrorMessage, 0, 'and the scan does not count it');
    assert.equal(count(here).byApi.showWarningMessage, 1, 'the real call, once');
    assert.equal(count(here).modal, 0, 'a modal named in a comment is not a question anybody was asked');
  } finally {
    rmSync(here, { recursive: true, force: true });
  }
});
