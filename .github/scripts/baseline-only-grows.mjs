#!/usr/bin/env node
// baseline-only-grows.mjs — the ratchet closes in both directions.
//
//   node .github/scripts/baseline-only-grows.mjs <current.json> <base.json>
//
// Exit 0  nothing recorded before has stopped being recorded.
// Exit 1  a version, or a whole line, would stop being protected. The message names them.
// Exit 2  a file could not be read or parsed. A check that cannot check must not pass.
//
// WHY: `changelog-baseline.json` is what lets the release guard say a note has gone MISSING — it
// runs every recorded version against the changelog and refuses a release that lost one. But the
// baseline lives in the repository it guards, so one commit deleting a note AND its baseline row
// passes everything: both sides of the comparison move together. That is the residual hole in any
// in-repo ratchet, and this is what closes it.
//
// THE BASE REVISION IS THE HARD PART, and two gate reviewers called it Blocking independently:
//   * a shallow CI checkout has no `origin/main` at all — `git show origin/main:...` fails outright;
//   * and once a deletion has landed on main, main compares equal to itself and the check is moot.
// So CI pins `github.event.pull_request.base.sha` — the commit this change is actually proposed
// against — and runs this only on a pull request.
//
// A BASE THAT CARRIES NO BASELINE IS A PASS. The commit that introduces the file has nothing to
// compare against, and so does any branch taken from before it existed. Refusing there would make
// the check impossible to merge in the first place.

import * as fs from 'node:fs';

/**
 * Whether this is a baseline at all: an object whose every property is an array of strings.
 *
 * <p>Valid JSON is not a valid baseline, and the difference matters because the two answers are
 * different exit codes. `[]`, `"text"`, `42`, `{"Server": "0.28.0"}` and `{"Server": [1]}` all parse;
 * some of them used to throw out of `filter` or `Set` and exit 1, which in this script means "a
 * release lost its note" — a specific and wrong accusation. `null` is the sharpest case:
 * `JSON.parse("null")` succeeds and `Object.entries(null)` throws.</p>
 */
export function isBaseline(value) {
  return typeof value === 'object'
    && value !== null
    && !Array.isArray(value)
    && Object.values(value).every((versions) => Array.isArray(versions)
      && versions.every((v) => typeof v === 'string'));
}

/** Every version the base protected that the current file no longer does, by line. */
export function lost(base, current) {
  const gone = {};

  for (const [word, versions] of Object.entries(base)) {
    const kept = new Set(current[word] ?? []);
    const missing = versions.filter((v) => !kept.has(v));
    if (missing.length > 0) {
      gone[word] = missing;
    }
  }

  return gone;
}

const argv = process.argv.slice(2);
// The caller VOUCHES for the absence; the script never infers it from emptiness. Raised on the
// second code round and it is the sharper half of the empty-base question: if a truncated `{}` ever
// lands on the base branch, every later pull request would see an empty base, take the "nothing can
// have been lost" path and leave the ratchet silently dead. Whether the file exists is a fact CI
// establishes with `git cat-file -e`, so CI is what says so.
const baseAbsent = argv.includes('--base-absent');
const [currentPath, basePath] = argv.filter((a) => !a.startsWith('--'));
if (currentPath === undefined || basePath === undefined) {
  console.error('usage: baseline-only-grows.mjs <current.json> <base.json> [--base-absent]');
  process.exit(2);
}

let current;
let base;
try {
  current = JSON.parse(fs.readFileSync(currentPath, 'utf8'));
  base = JSON.parse(fs.readFileSync(basePath, 'utf8'));
} catch (error) {
  console.error(`a baseline could not be read: ${error.message}`);
  process.exit(2);
}

for (const [what, value] of [['current', current], ['base', base]]) {
  if (!isBaseline(value)) {
    console.error(`the ${what} baseline is not a baseline: expected an object whose every property `
      + `is an array of version strings, got ${JSON.stringify(value)?.slice(0, 120) ?? 'undefined'}.`);
    process.exit(2);
  }
}

if (Object.keys(base).length === 0) {
  if (baseAbsent) {
    console.log('the base commit carries no baseline file at all, so nothing can have been lost — '
      + 'this is the commit that introduces one.');
    process.exit(0);
  }

  console.error(`the base baseline at ${basePath} exists and is EMPTY, which is a truncated file `
    + 'rather than a first commit — and treating it as a pass would disarm this check for every '
    + 'pull request after it.\n'
    + 'If the base genuinely has no baseline file, the caller says so with --base-absent; that is a '
    + 'fact about git history, not something this script may infer from an empty object.');
  process.exit(2);
}

const gone = lost(base, current);
if (Object.keys(gone).length === 0) {
  console.log('the baseline only grew.');
  process.exit(0);
}

console.error('these releases would stop being protected:\n'
  + Object.entries(gone).map(([word, versions]) => `  ${word}: ${versions.join(', ')}`).join('\n')
  + '\n\nA row here is the only thing that notices its changelog entry being deleted. If the entry '
  + 'is genuinely gone on purpose, say so in the pull request and remove them in a commit of their '
  + 'own, so it is a decision somebody made rather than a line that vanished in a larger diff.');
process.exit(1);
