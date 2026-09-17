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
 * A word is data, and it is about to become part of a regular expression.
 *
 * <p>Today's words are `Server`, `Extension` and `Team server` and none of them contains a
 * metacharacter — but the registry is a list somebody will add to, and a line called `Tool (CLI)` or
 * `C++` would silently build either an invalid pattern or one that matches the wrong thing. Escaping
 * at the boundary costs a line; noticing it later costs a release with the wrong notes.</p>
 */
const quoted = (text) => text.replaceAll(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`);

/** The words this changelog heads its releases with, longest first so `Team server` wins. */
const WORDS = [...new Set(LINES.map((l) => l.word).filter(Boolean))]
  .sort((a, b) => b.length - a.length)
  .map(quoted);

/**
 * Where one release's section stops.
 *
 * <p>At the next RELEASE heading, never at the next `## `. A note may legitimately carry its own
 * `## Breaking changes` subheading, and cutting there would truncate the body at the first
 * subheading anybody writes.</p>
 *
 * <p><b>A release heading carries a VERSION, and that is the whole difference.</b> An earlier draft
 * matched `## ` followed by a known word or a digit, which four reviewers said would end the section
 * at `## Server compatibility` or `## 1. Migration notes` and silently drop the rest of the note
 * from the published release. The number is required now.</p>
 */
const NEXT_RELEASE = new RegExp(
  String.raw`^## (?:(?:${WORDS.join('|')}) )?\d+(?:\.\d+)*(?=\s|$)`, 'm');

/**
 * The lines of this release's section, or nothing when the changelog does not document it.
 *
 * <p>Finds the heading in any of the three shapes this file writes — bare, joint
 * (`## Extension A · Server X`) and parenthesised (`## A — date (server X)`) — and returns from that
 * heading to just before the next release heading. The version must END where the heading says it
 * does, for the reason spelt out in the guard: a trailing `rc1` or `-beta.1` is a different
 * release.</p>
 */
export function sectionFor(changelog, line, version) {
  const word = quoted(line.word);
  const v = quoted(version);
  const shapes = [
    // `## Server 0.28.0 — …`, and the joint `## Extension 0.32.3 · Server 0.18.17 — …`.
    String.raw`(?:[^\r\n]*? · )?${word} ${v}(?=\s|$)`,
    // `## 0.31.0 — 2026-09-06 (server 0.18.3)`.
    String.raw`[^\r\n]*\(${quoted(line.word.toLowerCase())} ${v}\)`,
  ];
  if (line.bare === true) {
    // Only the line that OWNS the bare form, or `## 0.31.0` would hand an mcp release the
    // extension's notes. One number belongs to two lines otherwise.
    shapes.push(String.raw`${v}(?=\s|$)`);
  }
  const heading = new RegExp(String.raw`^## (?:${shapes.join('|')})`, 'm');

  const body = changelog.replaceAll('\r\n', '\n');
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

  return { code: 0, body: sectionFor(changelog, line, version) ?? fallback };
}

/**
 * The command line, parsed strictly.
 *
 * <p>Every deviation is an exit 2 rather than a default: a mistyped `--fallbcak` that is ignored
 * generates the wrong notes and says nothing, and a `--out` with no value writes them where nobody
 * looks. A release is not a place to be forgiving about arguments.</p>
 */
export function parseArgv(argv) {
  const FLAGS = new Set(['--changelog', '--fallback', '--out']);
  const values = {};
  let tag;

  for (let at = 0; at < argv.length; at += 1) {
    const argument = argv[at];

    if (!argument.startsWith('--')) {
      if (tag !== undefined) {
        return { error: `two tags were given, "${tag}" and "${argument}" — this takes exactly one.` };
      }
      tag = argument;
      continue;
    }
    if (!FLAGS.has(argument)) {
      return { error: `unknown option "${argument}". Known: ${[...FLAGS].join(', ')}.` };
    }
    if (argument in values) {
      return { error: `"${argument}" was given twice.` };
    }
    if (argv[at + 1] === undefined || argv[at + 1].startsWith('--')) {
      return { error: `"${argument}" needs a value.` };
    }
    values[argument] = argv[at + 1];
    at += 1;
  }

  return { tag, values };
}

const self = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === self) {
  // Everything below is wrapped: a throw would exit 1, which in this script's vocabulary means
  // "there is no entry" — the one answer it must never give by accident.
  try {
    const { tag, values, error } = parseArgv(process.argv.slice(2));
    if (error !== undefined) {
      console.error(`${error}\nusage: changelog-section.mjs <tag> `
        + '[--changelog <path>] [--fallback <text>] [--out <path>]');
      process.exit(2);
    }

    const fallback = values['--fallback'] ?? '';
    if (fallback.trim() === '') {
      // A release whose body is empty is not a release with short notes; it is a release that looks
      // like a mistake. The caller says what the sentence is, and it must say something.
      console.error('--fallback is required and must not be empty: every release gets a body, and '
        + 'a release with no changelog entry gets this sentence.');
      process.exit(2);
    }

    const changelogPath = values['--changelog'] ?? DEFAULT_CHANGELOG;
    const out = values['--out'];
    const { code, body, said } = verdict(tag,
      () => fs.readFileSync(changelogPath, 'utf8'), fallback);

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
  } catch (error) {
    console.error(`the notes could not be worked out: ${error.stack ?? error.message}`);
    process.exit(2);
  }
}
