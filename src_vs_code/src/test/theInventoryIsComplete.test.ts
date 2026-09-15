import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { DATA_TO_LEAVE, DATA_TO_MOVE } from '../dataDir';

/**
 * What is written under the data directory, and whether anything has been forgotten.
 *
 * <p><b>Why this exists.</b> The list the panel moved by named four entries. The server and the
 * extension between them write more than twenty. A person following the instructions on screen
 * therefore lost their edited prompts, their whole spending history, the entire chat half of the
 * product and the newest rounds — the ones still in the write-ahead log — and was told it had
 * worked, because a copy that misses a file does not fail.</p>
 *
 * <p><b>Why a scan and not a longer list.</b> A second hand-written list drifts exactly the way the
 * first one did, and the drift is silent for as long as nobody moves a directory. So the drift is
 * the thing under test: every path either half composes under its data directory must be named in
 * `shared/data-inventory.json`. The day somebody adds a persistent directory, this is red — in the
 * commit that adds it, rather than in an audit a year later.</p>
 *
 * <p>The scan reads SOURCE, which is a shape of test this repository is otherwise moving away from,
 * and the reason it is right here is the reverse of the usual one: what is being asserted IS a fact
 * about the source — "every place that composes such a path" — and no amount of running the program
 * can enumerate the paths it did not take.</p>
 */

const REPO = resolve(__dirname, '..', '..', '..');

interface Entry {
  readonly path: string;
  readonly kind: 'file' | 'directory';
  readonly move: boolean;
  readonly mention: boolean;
  readonly why: string;
}

const INVENTORY = (JSON.parse(
  readFileSync(join(REPO, 'shared', 'data-inventory.json'), 'utf8'),
) as { entries: Entry[] }).entries;

/** An entry as the scan sees it: the first path segment, with no trailing slash. */
function segment(path: string): string {
  return path.replace(/\/$/u, '');
}

const NAMED = new Set(INVENTORY.map((entry) => segment(entry.path)));

// ---------- the constants the product actually uses ----------

test('what the panel says to move is what the inventory says carries history', () => {
  assert.deepEqual(
    [...DATA_TO_MOVE].sort((a, b) => a.localeCompare(b)),
    INVENTORY.filter((entry) => entry.move).map((entry) => entry.path).sort((a, b) => a.localeCompare(b)),
  );
});

test('and what it says to leave is what the inventory says is a trap', () => {
  // Only the two that are SILENT when got wrong. The regenerated ones are left out of the sentence
  // on purpose — a panel that lists nine things nobody needs to think about is a panel nobody reads.
  assert.deepEqual(
    [...DATA_TO_LEAVE].sort((a, b) => a.localeCompare(b)),
    INVENTORY.filter((entry) => !entry.move && entry.mention).map((entry) => entry.path)
      .sort((a, b) => a.localeCompare(b)),
  );
});

test('every entry says why, because the why is what a person decides on', () => {
  for (const entry of INVENTORY) {
    assert.ok(entry.why.length > 15, `${entry.path}: "${entry.why}" is not a reason`);
  }
});

test('the database brings its write-ahead log with it', () => {
  // Named on its own because it is the one loss that is invisible from both ends: coai.db copies
  // cleanly, the rounds that were committed last are in the sidecar, and nothing reports anything.
  assert.ok(DATA_TO_MOVE.includes('coai.db-wal'), 'the newest rounds live here until a clean shutdown');
  assert.ok(DATA_TO_MOVE.includes('coai.db-shm'));
});

// ---------- and nothing either half writes is missing from it ----------

/** Every `.ts` and `.cs` file of both halves, with the generated and vendored trees skipped. */
function sourcesUnder(dir: string, extension: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const here = join(dir, entry.name);
    if (entry.isDirectory()) {
      return ['node_modules', 'out', 'bin', 'obj', 'test', 'tests', 'generated'].includes(entry.name)
        ? []
        : sourcesUnder(here, extension);
    }

    return entry.name.endsWith(extension) ? [here] : [];
  });
}

/**
 * Every literal first segment composed under a data directory, in either language.
 *
 * <p>The two spellings are the two languages' own: `Path.Combine(dataDir, "x")` in C# — with the
 * variable named `dataDir`, `_dataDir` or `settings.DataDir`, which is every spelling in the tree —
 * and `join(dataDir, 'x')` or `joinPath(dir, 'x')` in TypeScript. A composition that uses a CONSTANT
 * rather than a literal is caught by the constants' own test above; this is for the literals, which
 * is how every entry that went missing went missing.</p>
 */
function composedSegments(): Map<string, string> {
  const found = new Map<string, string>();
  const patterns: readonly RegExp[] = [
    /Path\.Combine\(\s*(?:settings\.)?_?[dD]ata[dD]ir\s*,\s*"([^"]+)"/gu,
    /(?:^|[^.\w])(?:join|joinPath)\(\s*(?:this\.)?(?:coaiDataDir\(\)|dataDir|_dataDir)\s*,\s*['"]([^'"]+)['"]/gu,
  ];

  const files = [
    ...sourcesUnder(join(REPO, 'src_mcp'), '.cs'),
    ...sourcesUnder(join(REPO, 'src_vs_code', 'src'), '.ts'),
  ];

  for (const file of files) {
    const text = readFileSync(file, 'utf8');
    for (const pattern of patterns) {
      for (const match of text.matchAll(pattern)) {
        found.set(match[1]!, file.slice(REPO.length + 1));
      }
    }
  }

  return found;
}

test('every path either half composes under the data directory is in the inventory', () => {
  const composed = composedSegments();

  // A scan that matched nothing would pass for ever, which is the failure mode of every structural
  // test. It found twenty-odd when it was written.
  assert.ok(composed.size >= 10, `the scan found only ${composed.size} paths — its patterns have rotted`);

  const missing = [...composed].filter(([path]) => !NAMED.has(path));

  assert.deepEqual(
    missing,
    [],
    'these are written under the data directory and the inventory has never heard of them. Add each '
    + 'one with its fate: does it carry history that a move must take, or is it written again by '
    + 'itself? Getting that wrong is silent in both directions.',
  );
});
