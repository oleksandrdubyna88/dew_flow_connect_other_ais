#!/usr/bin/env node
// changelog-section.mjs — the notes a release carries are the notes somebody wrote.
//
//   node .github/scripts/changelog-section.mjs <tag> [--changelog <path>] [--fallback <text>] [--out <path>]
//
// Exit 0  the section, or the fallback when this release has none. Either way there is a body.
// Exit 2  the tag belongs to no declared release line, or a file could not be read.
//
// WHY THIS EXISTS: every release body in this repository was a literal in the workflow.
// `release.yml` handed `draft-release.sh` a fixed sentence and that went to `gh release create`;
// nothing read CHANGELOG.md. So 65 `mcp-v*` releases carry byte-identical bodies, and the guard in
// `changelog-names-the-release.mjs` — which now refuses a release with no changelog entry — was
// making somebody write prose the reader of the release never sees. This is the other half.
//
// IT DECLARES NO RELEASE LINES OF ITS OWN. `lineOf` and `LINES` come from the guard. A second
// implementation of "which heading word belongs to this tag" is a defect from the moment it
// compiles, because the two drift and nothing notices.
//
// THE FALLBACK IS LOAD-BEARING, not politeness: `extension-v*` and `server-v*` have 51 and 5
// releases with no entry, and `bugs-v*` has no heading shape in this file at all. None of those
// releases may fail over a gap this work is not closing.
//
// WHY `--out` RATHER THAN STDOUT FOR THE WORKFLOW: a release body is multiline markdown that can
// begin a line with `-`. Travelling as a shell argument it is split by word-splitting, truncated at
// the first newline, or read as a flag by whatever parses it next. The workflow writes it to a file
// and passes the PATH; `draft-release.sh` gives that to `gh release create --notes-file`.

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

import { LINES, lineOf } from './changelog-names-the-release.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_CHANGELOG = path.resolve(here, '..', '..', 'src_vs_code', 'CHANGELOG.md');

/**
 * Where one release's section stops.
 *
 * <p>At the next RELEASE heading, never at the next `## `. A note may legitimately carry its own
 * `## Breaking changes` subheading, and cutting there would truncate the body at the first
 * subheading anybody writes. The alternation is built from the registry's own words plus a digit,
 * which is the bare `## 0.31.0 — …` form the early extension releases used.</p>
 */
const NEXT_RELEASE = new RegExp(String.raw`^## (?:${[...new Set(LINES.map((l) => l.word)
  .filter(Boolean))].join('|')}|\d)`, 'm');

/**
 * The lines of this release's section, or nothing when the changelog does not document it.
 *
 * <p>Finds the heading in any of the three shapes this file writes — bare, joint
 * (`## Extension A · Server X`) and parenthesised (`## A — date (server X)`) — and returns from that
 * heading to just before the next release heading. The version must END where the heading says it
 * does, for the reason spelt out in the guard: a trailing `rc1` or `-beta.1` is a different
 * release.</p>
 */
export function sectionFor(changelog, word, version) {
  const v = version.replace(/\./g, String.raw`\.`);
  const heading = new RegExp(
    String.raw`^## (?:(?:[^\r\n]*? · )?${word} ${v}(?=\s|$)|[^\r\n]*\(${word.toLowerCase()} ${v}\))`,
    'm');

  const body = changelog.replace(/\r\n/g, '\n');
  const at = body.search(heading);
  if (at === -1) {
    return undefined;
  }

  const rest = body.slice(at);
  const nextAt = rest.slice(1).search(NEXT_RELEASE);

  return (nextAt === -1 ? rest : rest.slice(0, nextAt + 1)).trimEnd();
}

export function verdict(tag, read, fallback) {
  if (typeof tag !== 'string' || tag.length === 0) {
    return {
      code: 2,
      said: 'usage: changelog-section.mjs <tag> [--changelog <path>] [--fallback <text>] [--out <path>]',
    };
  }

  const line = lineOf(tag);
  if (line === undefined) {
    return {
      code: 2,
      said: `${tag}: not a release line this repository declares. Declared: `
        + `${LINES.map((l) => l.prefix).join(', ')}.`,
    };
  }

  // A line with no heading word is documented nowhere in this file — `bugs-v*` today. That is not an
  // error, it is a release that has only the sentence.
  if (line.word === undefined) {
    return { code: 0, body: fallback };
  }

  const version = tag.slice(line.prefix.length);
  if (!/^\d+(\.\d+)*$/.test(version)) {
    return { code: 2, said: `${tag}: "${version}" is not a version this can look for.` };
  }

  let changelog;
  try {
    changelog = read();
  } catch (error) {
    return { code: 2, said: `the changelog could not be read: ${error.message}` };
  }

  return { code: 0, body: sectionFor(changelog, line.word, version) ?? fallback };
}

const self = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === self) {
  const argv = process.argv.slice(2);
  const FLAGS = ['--changelog', '--fallback', '--out'];
  const valueOf = (flag, fallback) => {
    const at = argv.indexOf(flag);

    return at === -1 || argv[at + 1] === undefined ? fallback : argv[at + 1];
  };
  const tag = argv.find((argument, at) => !argument.startsWith('--')
    && !FLAGS.includes(argv[at - 1]));

  const changelogPath = valueOf('--changelog', DEFAULT_CHANGELOG);
  const out = valueOf('--out', undefined);
  const { code, body, said } = verdict(tag,
    () => fs.readFileSync(changelogPath, 'utf8'), valueOf('--fallback', ''));

  if (code !== 0) {
    console.error(said);
    process.exit(code);
  }
  if (out === undefined) {
    process.stdout.write(`${body}\n`);
  } else {
    fs.writeFileSync(out, `${body}\n`, 'utf8');
    console.log(`wrote ${body.length} characters of notes to ${out}`);
  }
  process.exit(0);
}
