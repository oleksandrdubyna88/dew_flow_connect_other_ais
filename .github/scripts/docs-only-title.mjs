#!/usr/bin/env node
// docs-only-title.mjs — documentation alone (Markdown and pictures) never opens a release.
//
//   node .github/scripts/docs-only-title.mjs --pr <number>      (the facts come from `gh`)
//   node .github/scripts/docs-only-title.mjs --facts <file>     (the same facts, as JSON)
//
// Exit 0  nothing here would release a package on documentation alone.
// Exit 1  a releasing title or commit would — the message names the package, the files and the repair.
// Exit 2  the facts or the configuration could not be read. A check that cannot look must not pass.
//
// THE RULE, the operator's, 2026-09-26: a release pull request opens only for a change to CODE; a
// change to Markdown files alone never opens one (research/PLAN_the_release_guards_contradict.md).
// release-please cannot say that itself — its `exclude-paths` are directory prefixes, not globs — and
// what decides a release is the commit TYPE. So the rule is held where the type is chosen: a title (a
// squash merge's commit) or a commit (a rebase merge keeps each one) of a releasing type, whose files
// under some package are ALL documentation, would release that package with no code in it.
//
// DOCUMENTATION is Markdown and pictures — the operator's word, 2026-09-26: a screenshot or a diagram
// beside a README is still documentation. A picture beside CODE does not make it one: the code releases.
//
// PER PACKAGE, not per pull request: a `feat:` for the extension that also edits `src_mcp/README.md`
// puts that commit on the mcp line too, and would open an mcp release for a README.

import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolved } from './lib/resolved.mjs';

/** A conventional-commit header release-please releases on: feat, fix, perf, revert — or anything with `!`. */
export function releases(header) {
  const m = /^(\w+)(\([^)]*\))?(!)?:/.exec(header.trim());

  return m !== null && (m[3] === '!' || ['feat', 'fix', 'perf', 'revert'].includes(m[1]));
}

/** What counts as documentation: Markdown, and the pictures a document shows. */
export const DOCUMENTATION = ['.md', '.png', '.jpg', '.jpeg', '.gif', '.svg', '.webp'];

export function isDocumentation(file) {
  const lower = file.toLowerCase();

  return DOCUMENTATION.some((extension) => lower.endsWith(extension));
}

/** The packages a change would release on documentation alone: touched there, and every file there documentation. */
export function documentationOnlyPackages(files, packages) {
  return packages.filter((pkg) => {
    const inside = files.filter((file) => file.startsWith(`${pkg}/`));

    return inside.length > 0 && inside.every(isDocumentation);
  });
}

/** One change — a title over the whole pull request, or one commit over its own files. */
function problemsOf(what, header, files, packages) {
  if (!releases(header)) {
    return [];
  }

  return documentationOnlyPackages(files, packages).map((pkg) => {
    const md = files.filter((file) => file.startsWith(`${pkg}/`));
    return `${what} "${header}" would release ${pkg} on documentation alone (${md.join(', ')}).`;
  });
}

/** The decision, over facts already gathered. */
export function docsOnlyVerdict({ title, files, commits }, packages) {
  const problems = [
    ...problemsOf('The title', title, files, packages),
    ...commits.flatMap((c) => problemsOf(`Commit ${c.sha.slice(0, 8)}`, c.message.split('\n')[0], c.files, packages)),
  ];
  if (problems.length === 0) {
    return { code: 0, said: 'No package would be released on documentation alone.' };
  }

  return {
    code: 1,
    said: [...problems, '',
      'A change to documentation — Markdown and pictures — is not a release (the operator\'s rule). Say it',
      'as docs: — or, where the same change also carries code for ANOTHER package, move the documentation',
      'into its own docs: commit.',
    ].join('\n'),
  };
}

/** Room for a large pull request's file list, and a bound on one API question. */
const GH_OUTPUT_BYTES = 64 * 1024 * 1024;
const GH_TIMEOUT_MS = 120_000;

function gh(args) {
  return execFileSync(resolved('gh'), args,
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: GH_OUTPUT_BYTES, timeout: GH_TIMEOUT_MS, killSignal: 'SIGKILL' });
}

/** The facts for a pull request, from the API: its title, its files, and each commit with its own files. */
function factsOf(pr) {
  const repo = process.env.GITHUB_REPOSITORY;
  if (!repo) {
    throw new Error('GITHUB_REPOSITORY is not set, so there is no repository to ask');
  }
  const title = process.env.PR_TITLE ?? JSON.parse(gh(['api', `repos/${repo}/pulls/${pr}`])).title;
  const lines = (text) => text.split('\n').filter(Boolean);
  const files = lines(gh(['api', '--paginate', `repos/${repo}/pulls/${pr}/files`, '--jq', '.[].filename']));
  // The list carries each commit's message; only a commit of a RELEASING type can break the rule, so
  // only those are asked for their files — not one request per commit (the code round).
  const listed = JSON.parse(gh(['api', '--paginate', '--slurp', `repos/${repo}/pulls/${pr}/commits`])).flat();
  const commits = listed
    .filter((c) => releases(c.commit.message.split('\n')[0]))
    .map((c) => {
      const one = JSON.parse(gh(['api', `repos/${repo}/commits/${c.sha}`]));
      return { sha: c.sha, message: c.commit.message, files: (one.files ?? []).map((f) => f.filename) };
    });

  return { title, files, commits };
}

function packagesOf(root) {
  const config = JSON.parse(fs.readFileSync(path.join(root, 'release-please-config.json'), 'utf8'));
  const packages = Object.keys(config.packages ?? {});
  if (packages.length === 0) {
    throw new Error('release-please-config.json declares no packages');
  }

  return packages;
}

const isFacts = (value) => value !== null && typeof value === 'object'
  && typeof value.title === 'string' && Array.isArray(value.files) && Array.isArray(value.commits);

function main(argv) {
  const valueAfter = (flag) => {
    const at = argv.indexOf(flag);
    return at >= 0 && argv[at + 1] !== undefined && !argv[at + 1].startsWith('--') ? argv[at + 1] : undefined;
  };
  const factsFile = valueAfter('--facts');
  const pr = valueAfter('--pr');
  if ((factsFile === undefined) === (pr === undefined)) {
    console.error('usage: docs-only-title.mjs --pr <number> | --facts <file> — exactly one of them.');
    return 2;
  }
  let facts;
  let packages;
  try {
    const here = path.dirname(fileURLToPath(import.meta.url));
    packages = packagesOf(process.env.RELEASE_CONFIG_ROOT ?? path.resolve(here, '..', '..'));
    facts = factsFile === undefined ? factsOf(pr) : JSON.parse(fs.readFileSync(factsFile, 'utf8'));
  } catch (error) {
    console.error(`docs-only-title: could not read what to check: ${error.message}`);
    return 2;
  }
  if (!isFacts(facts)) {
    console.error('docs-only-title: the facts need a title, a list of files and a list of commits.');
    return 2;
  }
  const { code, said } = docsOnlyVerdict(facts, packages);
  (code === 0 ? console.log : console.error)(said);

  return code;
}

const self = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === self) {
  process.exit(main(process.argv.slice(2)));
}
