import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { serverRunAt } from '../roundsDbRead';

/**
 * The one spawn that is allowed to ask about a directory this window is not pointed at.
 *
 * <p>`serverRun` carries the directory this window chose, as a default rather than a parameter,
 * precisely so that no call site can forget it — a child that inherits an environment with no
 * `COAI_DATA_DIR` reads the DEFAULT folder and reports, truthfully, that a history on a NAS does not
 * exist. `serverRunAt` is the deliberate exception, and an exception with one caller is a rule; an
 * exception with four is the bug the rule was written against.</p>
 *
 * <p>The same guard `writeWslconfig` carries, for the same reason and in the same shape: a second
 * caller must be somebody's decision rather than an autocomplete.</p>
 */

const SRC = join(__dirname, '..', '..', 'src');

/** Source with its comments removed, so a call graph is read from calls. */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//gu, '').replace(/^\s*\/\/.*$/gmu, '');
}

function sourcesIn(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const here = join(dir, entry.name);
    if (entry.isDirectory()) {
      return entry.name === 'test' ? [] : sourcesIn(here);
    }

    return entry.name.endsWith('.ts') ? [here] : [];
  });
}

test('only the move verification reads a directory other than this window\'s', () => {
  const callers = sourcesIn(SRC)
    .filter((file) => !file.endsWith(`${'roundsDbRead'}.ts`))
    // A CALL, in code rather than in prose — the same correction `wslNetwork.test.ts` needed the
    // day a docblock elsewhere named the function it guards.
    .filter((file) => /(?:^|[^\w.])serverRunAt\(/mu.test(withoutComments(readFileSync(file, 'utf8'))))
    .map((file) => file.slice(SRC.length + 1));

  assert.deepEqual(
    callers,
    ['extension.ts'],
    'serverRunAt has a new caller. It hands a spawned server a directory this window is NOT using, '
    + 'which is right for exactly one thing — reading a copy back before pointing anything at it. '
    + 'Anywhere else it is a read of the wrong history that looks like a read of the right one.',
  );
});

test('and it is the counter, so the verification is comparing two real directories', () => {
  // A structural test that matched nothing would pass for ever. This pins WHAT extension.ts uses it
  // for, not merely that the identifier appears somewhere in the file.
  const host = readFileSync(join(SRC, 'extension.ts'), 'utf8');

  assert.match(
    host,
    /readLog\(\s*server\.fsPath,\s*\{ limit: 1 \},\s*serverRunAt\(server\.fsPath, resolvedDirectory\)\)/u,
    'the fingerprint no longer reads the directory it was given — which would compare a directory '
    + 'with itself and call every move verified',
  );
});

test('nothing resolves the data directory by reading the variables itself', () => {
  // There were THREE answers to "where is the data" in this product, and the third was in
  // `extension.ts`: a raw `COAI_DATA_DIR` read that ignored `COAI_DATA_SIDE` and did not trim, so a
  // side-partitioned installation listed sessions from `<root>` while everything else wrote
  // `<root>/<side>`. The settings layer would have made it permanent rather than exposing it — a
  // setting is invisible to a `process.env` read, so that copy would have gone on reading
  // %LOCALAPPDATA% for everyone who chose a folder.
  const offenders = sourcesIn(SRC)
    .filter((file) => !file.endsWith('dataDir.ts'))
    .filter((file) => /process\.env\[["']COAI_DATA_(DIR|SIDE)["']\]/u.test(withoutComments(readFileSync(file, 'utf8'))))
    .map((file) => file.slice(SRC.length + 1));

  assert.deepEqual(
    offenders,
    [],
    'these read the storage variables directly instead of asking dataDir.ts. A second answer to '
    + '"where does the data live" is a second answer that drifts, and the drift is silent: it shows '
    + 'as an empty list rather than an error.',
  );
});

test('the directory it is given is handed to the child as the root, with no side', () => {
  // The unambiguity the doc claims: a path that already carries its side resolves to itself when no
  // side is named, so a side cannot be applied twice. Asserted by running a child that prints what
  // it was given.
  const run = serverRunAt(process.execPath, '/mnt/nas/coai/windows');

  return run(
    ['-e', 'process.stdout.write(`${process.env.COAI_DATA_DIR}|${process.env.COAI_DATA_SIDE ?? ""}`)'],
    10_000,
  ).then(({ code, output }) => {
    assert.equal(code, 0);
    assert.equal(output, '/mnt/nas/coai/windows|');
  });
});
