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

const [currentPath, basePath] = process.argv.slice(2);
if (currentPath === undefined || basePath === undefined) {
  console.error('usage: baseline-only-grows.mjs <current.json> <base.json>');
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

if (Object.keys(base).length === 0) {
  console.log('the base commit records no baseline, so nothing can have been lost — '
    + 'this is the first one.');
  process.exit(0);
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
