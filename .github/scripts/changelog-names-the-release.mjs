#!/usr/bin/env node
// changelog-names-the-release.mjs — a release that tells nobody what is in it does not ship.
//
//   node .github/scripts/changelog-names-the-release.mjs <tag> [--changelog <path>] [--baseline <path>]
//   node .github/scripts/changelog-names-the-release.mjs --lines
//
// Exit 0  the tag's line is not guarded, or its entry is there and no older entry has been lost.
// Exit 1  something is missing — the message names every version and the heading to write.
// Exit 2  the arguments or a file could not be read. A guard that cannot check must not pass.
//
// WHY THIS EXISTS, measured across all three heading shapes this changelog uses: 65 `mcp-v*` tags,
// 24 of them documented — 10 as `## Server X`, 3 inside a joint `## Extension A · Server X`, and 15
// in the parenthesised `## A — date (server X)` form the early extension releases used — and 41 with
// nothing at all. Eight consecutive releases carrying custom roles, `review_document`, the whole
// consultant and `coai-bugs` had not one word written about them. Every `mcp-v*` GitHub release body
// is the same fixed sentence, so the only record was the commit log.
//
// COUNTING ONLY `^## Server` SAYS 10, AND THAT MISREADING HAS ALREADY COST SOMETHING: the first pass
// at this backfill reconstructed 0.18.15, 0.18.16 and 0.18.17 — all three already documented by their
// joint headings — and wrote entries for 0.26.0 and 0.27.0, which were never tagged at all. That is
// why this matcher knows every shape, and why a test checks the baseline against `git tag`.
//
// WHERE IT RUNS: the `mcp-draft` job, immediately after the checkout — as early as a step can read
// this file, and before the draft release exists. A refusal there costs nothing: no draft, no
// uploaded asset, nothing published. **The repair is to write the entry and push the tag again**;
// this repository has burned a tag before (`mcp-v0.16.0`) and it is the supported move, because the
// alternative is a published release whose notes never arrive.
//
// WHY NODE, when its three siblings in this folder are bash: they drive `gh`, and this one is a text
// check over a file. A gate whose refusals cannot be exercised by a test is a gate nobody has seen
// close — `promote-release.mjs` in the conventions repository makes the same argument for the same
// reason, and it is why `changelogNamesTheRelease.test.ts` can spawn this and read its exit code.

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Every release line this repository tags, and whether this guard judges it.
 *
 * <p><b>ONE registry, with `guarded` as a field.</b> An earlier draft kept a second `UNGUARDED` array
 * that was consulted FIRST, while its own header told the next person that guarding another line was
 * "one row in LINES" — which was false: the bypass would have swallowed the new row and the policy
 * would have been silently dead. A line cannot be declared twice here, and a test reads this back
 * through `--lines` to keep it that way.</p>
 *
 * <p>`extension-v` and `server-v` are unguarded on purpose. Their gaps are real — 51 and 5 — but of a
 * different kind: a patch release beside a documented one, which is this changelog's long-standing
 * and legitimate convention. Guarding them is a policy decision about those lines, and it is made by
 * setting `guarded` here and recording their baseline.</p>
 *
 * <p><b>`word` and `guarded` are orthogonal, and keeping them apart is the point.</b> `word` says
 * which heading this line's entries carry in the changelog; `guarded` says whether a missing entry
 * blocks the release. An earlier draft gave a `word` only to the guarded line, which conflated the
 * two and left `changelog-section.mjs` — which needs the word to find a release's notes, for every
 * line — with nothing to read. Measured from the file: 52 `## Extension`, 18 `## Server`, 1
 * `## Team server`, and ZERO headings mentioning coai-bugs, which is why `bugs-v` has no word.</p>
 */
export const LINES = [
  { prefix: 'mcp-v', word: 'Server', guarded: true },
  { prefix: 'extension-v', word: 'Extension', guarded: false },
  { prefix: 'server-v', word: 'Team server', guarded: false },
  { prefix: 'bugs-v', guarded: false },
];

const here = path.dirname(fileURLToPath(import.meta.url));
// Resolved from the SCRIPT, never from the working directory: run from `src_vs_code/`, a
// CWD-relative default reads nothing and the guard exits 2 on a release that was perfectly fine.
const DEFAULT_CHANGELOG = path.resolve(here, '..', '..', 'src_vs_code', 'CHANGELOG.md');
const DEFAULT_BASELINE = path.resolve(here, '..', 'changelog-baseline.json');

/**
 * Whether the changelog documents this release, in any shape this file actually writes.
 *
 * <p>Three shapes, all of them real entries for the line:</p>
 * <ul>
 *   <li><code>## Server 0.28.0 — 2026-09-17</code> — the bare form;</li>
 *   <li><code>## Extension 0.32.3 · Server 0.18.17 — 2026-09-10</code> — one release, both halves;</li>
 *   <li><code>## 0.31.0 — 2026-09-06 (server 0.18.3)</code> — what the early extension entries used.</li>
 * </ul>
 *
 * <p><b>The version must END where the heading says it does.</b> The first draft used
 * <code>(?![\d.])</code>, which rejects <code>0.2.0</code> inside <code>0.28.0</code> and
 * <code>0.2.0.1</code> — but happily accepts <code>0.28.0rc1</code>, <code>0.28.0-beta.1</code> and
 * <code>0.28.0x</code>, because a letter is neither a digit nor a dot. <code>(?=\s|$)</code> is the
 * whole condition: whitespace or the end of the line, nothing else. (A <code>\b</code> would be worse
 * than either — the character after <code>0.2.0</code> in <code>0.2.0.1</code> is a dot, which
 * <code>\b</code> is perfectly happy with.)</p>
 *
 * <p>Case matters, and it is load-bearing: `Team server` is a different product from `Server`, and
 * the two can hold the same number.</p>
 */
export function namesTheRelease(changelog, word, version) {
  const v = version.replaceAll('.', String.raw`\.`);
  const bareOrJoint = new RegExp(String.raw`^## (?:[^\r\n]*? · )?${word} ${v}(?=\s|$)`, 'm');
  const parenthesised = new RegExp(String.raw`^## [^\r\n]*\(${word.toLowerCase()} ${v}\)`, 'm');

  return bareOrJoint.test(changelog) || parenthesised.test(changelog);
}

/** The line a tag belongs to, or nothing when no declared prefix claims it. */
export function lineOf(tag) {
  return [...LINES]
    .sort((a, b) => b.prefix.length - a.prefix.length)
    .find((line) => tag.startsWith(line.prefix));
}

/**
 * What the workflow should do about this tag.
 *
 * @param tag the ref being released, e.g. `mcp-v0.29.0`
 * @param read `{ changelog(), baseline() }` — each throws rather than returning a default, because a
 *   file this guard cannot read is a check it cannot perform, and that is exit 2 rather than a pass.
 */
export function verdict(tag, read) {
  if (typeof tag !== 'string' || tag.length === 0) {
    return {
      code: 2,
      said: 'usage: changelog-names-the-release.mjs <tag> [--changelog <path>] [--baseline <path>]\n'
        + 'the tag argument was missing or empty — in a workflow that usually means the step ran on '
        + 'something other than a tag push, so github.ref_name held a branch or nothing at all.',
    };
  }

  const line = lineOf(tag);
  if (line === undefined) {
    return {
      code: 2,
      said: `${tag}: not a release line this guard knows. Declared lines: `
        + `${LINES.map((l) => l.prefix).join(', ')}.`,
    };
  }
  if (!line.guarded) {
    return { code: 0, said: `${tag}: the ${line.prefix}* line is not guarded, nothing to check.` };
  }

  const version = tag.slice(line.prefix.length);
  if (!/^\d+(\.\d+)*$/.test(version)) {
    return { code: 2, said: `${tag}: "${version}" is not a version this guard can look for.` };
  }

  let changelog;
  let baseline;
  try {
    changelog = read.changelog();
    baseline = JSON.parse(read.baseline());
  } catch (error) {
    return { code: 2, said: `a file this guard needs could not be read: ${error.message}` };
  }

  // Valid JSON is not a valid baseline, and the difference is an exit code. `{"Server": {}}` parses,
  // and `recorded.includes` would then throw OUTSIDE the try above — so the process exits 1, which
  // in this guard's vocabulary means "a release has no entry": a specific and wrong accusation about
  // a file that is merely malformed. Raised by CodeRabbit on the pull request.
  const shaped = typeof baseline === 'object' && baseline !== null && !Array.isArray(baseline)
    && Object.values(baseline).every((versions) => Array.isArray(versions)
      && versions.every((v) => typeof v === 'string' && /^\d+(\.\d+)*$/.test(v)));
  if (!shaped) {
    // Describe the SHAPE, never echo the content. A guard's refusal goes into a CI log that more
    // people can read than can read the file, and "what it contained" is not what the reader needs
    // — they need to know what it should have been. Sonar's taint analysis is right that file
    // content reaching a log is worth a second look, even when this particular file is a committed
    // list of version numbers.
    return {
      code: 2,
      said: `the baseline at ${path.basename(DEFAULT_BASELINE)} is not a baseline: expected an `
        + 'object whose every property is an array of version strings like "0.28.0", got '
        + `${Array.isArray(baseline) ? 'an array' : typeof baseline}.`,
    };
  }

  const recorded = baseline[line.word] ?? [];
  const problems = [];

  if (!namesTheRelease(changelog, line.word, version)) {
    problems.push(`${tag} has no entry in the changelog.\n`
      + `  Write a "## ${line.word} ${version} — <date>" section saying what this release does for `
      + 'a person.');
  }
  if (!recorded.includes(version)) {
    problems.push(`${version} is not in the baseline, so nothing would notice its note being `
      + 'deleted later.\n'
      + `  Add "${version}" to the "${line.word}" list in ${path.basename(DEFAULT_BASELINE)}.`);
  }

  const lost = recorded.filter((v) => v !== version && !namesTheRelease(changelog, line.word, v));
  if (lost.length > 0) {
    problems.push('these releases had a note and no longer do: '
      + `${lost.join(', ')}.\n  Put them back, or say why they were removed.`);
  }

  if (problems.length === 0) {
    return { code: 0, said: `${tag}: documented, and no earlier note has been lost.` };
  }

  return {
    code: 1,
    said: `${problems.join('\n')}\n\n`
      + 'Then delete this tag and push it again. A release whose notes never arrive is the defect '
      + 'this check exists to prevent: eight releases of this line once shipped with none.',
  };
}

const self = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === self) {
  const argv = process.argv.slice(2);

  if (argv.includes('--lines')) {
    console.log(JSON.stringify(LINES, undefined, 2));
    process.exit(0);
  }

  // A flag with no value used to fall back to the DEFAULT path, so `--changelog` as the last
  // argument made the guard check a different file from the one it was asked about and pass. Raised
  // by CodeRabbit; the sibling extractor had already been given strict parsing by the gate and this
  // one had not.
  const valueOf = (flag, fallback) => {
    const at = argv.indexOf(flag);
    if (at === -1) {
      return fallback;
    }
    if (argv[at + 1] === undefined || argv[at + 1].startsWith('--')) {
      console.error(`"${flag}" needs a path after it.\n`
        + 'usage: changelog-names-the-release.mjs <tag> [--changelog <path>] [--baseline <path>]');
      process.exit(2);
    }

    return argv[at + 1];
  };
  const tag = argv.find((argument, at) => !argument.startsWith('--')
    && argv[at - 1] !== '--changelog' && argv[at - 1] !== '--baseline');

  const changelogPath = valueOf('--changelog', DEFAULT_CHANGELOG);
  const baselinePath = valueOf('--baseline', DEFAULT_BASELINE);

  const { code, said } = verdict(tag, {
    changelog: () => fs.readFileSync(changelogPath, 'utf8'),
    baseline: () => fs.readFileSync(baselinePath, 'utf8'),
  });
  (code === 0 ? console.log : console.error)(said);
  process.exit(code);
}
