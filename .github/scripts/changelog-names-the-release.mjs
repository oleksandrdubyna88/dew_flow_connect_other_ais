#!/usr/bin/env node
// changelog-names-the-release.mjs — a release that tells nobody what is in it does not ship.
//
//   node .github/scripts/changelog-names-the-release.mjs <tag> [changelog]
//
// Exit 0  the tag's line is not guarded, or its entry is there.
// Exit 1  the entry is missing — the message names the version and the heading to write.
// Exit 2  the arguments or the file could not be read. A guard that cannot check must not pass.
//
// WHY THIS EXISTS, measured: 67 `mcp-v*` tags against 10 `## Server` entries, and thirteen
// consecutive releases carrying custom roles, `review_document`, the whole consultant and
// `coai-bugs` with not one word written about any of them. Every `mcp-v*` GitHub release body is the
// same fixed sentence, so the only record of nine days' work was the commit log.
//
// WHERE IT RUNS: the `mcp-draft` job, immediately after the checkout — which is as early as a step
// can read this file, and before the draft release exists. A refusal there costs nothing: no draft,
// no uploaded asset, nothing published. **The repair is to delete the tag and push it again** once
// the entry is written; this repository has burned a tag before (`mcp-v0.16.0`) and it is the
// supported move, because the alternative is a published release whose notes never arrive.
//
// WHY NODE, when its three siblings in this folder are bash: they drive `gh`, and this one is a text
// check over a file. A gate whose refusals cannot be exercised by a test is a gate nobody has seen
// close — `promote-release.mjs` in the conventions repository makes the same argument for the same
// reason, and it is why `changelogNamesTheRelease.test.ts` can spawn this and read its exit code.
//
// ONLY THE MCP LINE IS GUARDED TODAY. `extension-v*` and `server-v*` have gaps of their own — 51 and
// 5 — but they are gaps of a different kind (a patch release beside a documented one), and putting a
// guard on them is a policy decision about those lines rather than a fix to this one. Adding them is
// one row in LINES below.

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Tag prefix -> the word its entries are headed with in the changelog. */
const LINES = new Map([
  ['mcp-v', 'Server'],
]);

/** Prefixes this repository tags but this guard deliberately does not check. */
const UNGUARDED = ['extension-v', 'server-v', 'bugs-v'];

const DEFAULT_CHANGELOG = path.join('src_vs_code', 'CHANGELOG.md');

/**
 * Whether the changelog carries the heading this version requires.
 *
 * <p>Matched as a WHOLE version, never as a prefix: `0.2.0` must not be satisfied by
 * `## Server 0.28.0` (a substring) nor by `## Server 0.2.0.1` (a longer number). The trailing
 * lookahead is what does that — a `\b` would not, because the character after `0.2.0` in `0.2.0.1`
 * is a dot, which `\b` is perfectly happy with.</p>
 *
 * <p>A joint heading counts: this file writes `## Extension 0.32.3 · Server 0.18.17` when one release
 * shipped both halves, and that IS an entry for the Server line.</p>
 *
 * <p>Case matters, and it is load-bearing: `Team server` is a different product from `Server`, and
 * the two can hold the same number.</p>
 */
export function namesTheRelease(changelog, word, version) {
  const escaped = version.replace(/\./g, String.raw`\.`);
  const heading = new RegExp(String.raw`^## (?:[^\r\n]*? · )?${word} ${escaped}(?![\d.])`, 'm');

  return heading.test(changelog);
}

/** The line a tag belongs to, or nothing when this guard does not judge it. */
export function lineOf(tag) {
  for (const [prefix, word] of LINES) {
    if (tag.startsWith(prefix)) {
      return { word, version: tag.slice(prefix.length) };
    }
  }

  return undefined;
}

export function verdict(tag, readChangelog) {
  if (typeof tag !== 'string' || tag.length === 0) {
    return { code: 2, said: 'usage: changelog-names-the-release.mjs <tag> [changelog]' };
  }
  if (UNGUARDED.some((prefix) => tag.startsWith(prefix))) {
    return { code: 0, said: `${tag}: not a guarded release line, nothing to check` };
  }

  const line = lineOf(tag);
  if (line === undefined) {
    return {
      code: 2,
      said: `${tag}: not a tag this guard knows. Guarded lines: ${[...LINES.keys()].join(', ')}; `
        + `deliberately unguarded: ${UNGUARDED.join(', ')}.`,
    };
  }
  if (!/^\d+(\.\d+)*$/.test(line.version)) {
    return { code: 2, said: `${tag}: "${line.version}" is not a version this guard can look for.` };
  }

  let changelog;
  try {
    changelog = readChangelog();
  } catch (error) {
    return { code: 2, said: `the changelog could not be read: ${error.message}` };
  }

  if (namesTheRelease(changelog, line.word, line.version)) {
    return { code: 0, said: `${tag}: "## ${line.word} ${line.version}" is there.` };
  }

  return {
    code: 1,
    said: `${tag} has no entry in the changelog.\n`
      + `Write a "## ${line.word} ${line.version} — <date>" section saying what this release does for `
      + 'a person, then delete this tag and push it again.\n'
      + 'A release whose notes never arrive is the defect this check exists to prevent: thirteen '
      + 'releases of this line once shipped with none.',
  };
}

const here = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === here) {
  const [tag, file] = process.argv.slice(2);
  const target = file ?? DEFAULT_CHANGELOG;
  const { code, said } = verdict(tag, () => fs.readFileSync(target, 'utf8'));
  (code === 0 ? console.log : console.error)(said);
  process.exit(code);
}
