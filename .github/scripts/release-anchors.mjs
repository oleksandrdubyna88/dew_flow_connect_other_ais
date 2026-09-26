#!/usr/bin/env node
// release-anchors.mjs — every release line's tag sits on a commit release-please can find.
//
//   node .github/scripts/release-anchors.mjs [--repo <dir>]
//
// Exit 0  every line's tag touches its own package, or has not been cut yet.
// Exit 1  a tag sits on a commit that touches nothing of its package — the message names the tag,
//         the commit, what it touches, and the command that puts the tag back.
// Exit 2  the configuration or git could not be read. A check that cannot look must not pass.
//
// WHY THIS EXISTS (research/PLAN_the_release_guards_contradict.md, 2026-09-26): release-please counts a
// line's next release from its tag, and it finds that commit INSIDE the list of commits touching the
// line's package (`commitsAfterSha(splitCommits[path], releaseSha)` in its src/manifest.ts). A tag on a
// commit that touches nothing there is not in that list, so the search answers -1 and EVERY commit of
// the package in the window counts as new. `mcp-v0.38.0` had been moved to a baseline-only commit, and
// release-please opened an empty `mcp 0.39.0` three times, listing features released weeks before.
// A rebase merge of a release pull request with a commit on top lands the tag the same way.
//
// WHERE IT RUNS: the first step of `release-please.yml`, so a broken anchor stops the run that would
// have opened the empty release, and says why. It only reads; it never moves a tag — that decision is
// a person's, and the message prints the command for it.

import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

/** What a line's tag looks like here: `<component>-v<version>` (include-component-in-tag, separator `-`). */
export function tagOf(component, version) {
  return `${component}-v${version}`;
}

/** Whether any of a commit's files lies under a package directory. */
export function touches(files, packagePath) {
  return files.some((file) => file.startsWith(`${packagePath}/`));
}

/**
 * The decision, over facts already gathered: one row per line — its package, its tag, the tag's
 * commit (empty when the tag has not been cut), that commit's files, and the newest commit at or
 * before it that does touch the package.
 */
export function anchorVerdict(lines, repository) {
  const broken = lines.filter((line) => line.sha.length > 0 && !touches(line.files, line.path));
  const said = lines.map((line) => describe(line));
  if (broken.length === 0) {
    return { code: 0, said: said.join('\n') };
  }

  return { code: 1, said: [...said, '', ...broken.map((line) => repair(line, repository))].join('\n') };
}

function describe(line) {
  if (line.sha.length === 0) {
    return `${line.tag}: not cut yet — a merged release pull request waiting for its tag, which is normal.`;
  }

  return touches(line.files, line.path)
    ? `${line.tag}: ${line.sha.slice(0, 8)} touches ${line.path}/ — release-please can find it.`
    : `${line.tag}: ${line.sha.slice(0, 8)} touches only ${line.files.join(', ') || 'nothing'} — `
      + `nothing under ${line.path}/, so release-please cannot find it and will count every ${line.path} `
      + 'commit in its window as new.';
}

function repair(line, repository) {
  if (line.candidate === undefined) {
    return `${line.tag}: no commit at or before it touches ${line.path}/, so there is nowhere to put it back.`;
  }
  const same = line.sameTree
    ? `its ${line.path}/ tree is identical to the tag's, so the release is unchanged`
    : `its ${line.path}/ tree DIFFERS from the tag's — check which one the release was built from first`;

  return `${line.tag}: put it back on ${line.candidate.sha.slice(0, 8)} "${line.candidate.subject}" (${same}):\n`
    + `  gh api -X PATCH "repos/${repository}/git/refs/tags/${line.tag}" -f sha=${line.candidate.sha} -F force=true\n`
    + '  A PATCH of the ref, not a delete: deleting the tag of a published release turns it into a draft.';
}

/** How long one git question may take: a local read, so a minute is already a hang. */
const GIT_TIMEOUT_MS = 60_000;

/** One git answer, or a refusal the caller turns into exit 2. */
function git(repo, args) {
  return execFileSync('git', args,
    { cwd: repo, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: GIT_TIMEOUT_MS, killSignal: 'SIGKILL' }).trim();
}

/**
 * The tag's commit, or empty when the tag has not been cut. ABSENT is asked separately from READ: a
 * git failure swallowed into an empty answer would read as "not cut yet", which passes — a broken
 * checkout waving the run through without looking at one anchor (the code round). So only a tag git
 * LISTS no such name for is uncut; any failure throws, and the caller exits 2.
 */
function tagCommit(repo, tag) {
  const listed = git(repo, ['tag', '--list', tag]);
  if (listed.length === 0) {
    return '';
  }

  return git(repo, ['rev-list', '-n', '1', `refs/tags/${tag}`]);
}

/** The git facts for one line; the decision above never runs git. */
function factsFor(repo, packagePath, component, version) {
  const tag = tagOf(component, version);
  const sha = tagCommit(repo, tag);
  if (sha.length === 0) {
    return { path: packagePath, tag, sha, files: [] };
  }
  const files = git(repo, ['diff-tree', '--root', '--no-commit-id', '--name-only', '-r', sha])
    .split('\n').filter(Boolean);
  const last = git(repo, ['log', '-1', '--format=%H%x00%s', sha, '--', packagePath]);
  if (last.length === 0) {
    return { path: packagePath, tag, sha, files };
  }
  const [candidateSha, subject] = last.split('\0');
  const sameTree = git(repo, ['rev-parse', `${candidateSha}:${packagePath}`])
    === git(repo, ['rev-parse', `${sha}:${packagePath}`]);

  return { path: packagePath, tag, sha, files, candidate: { sha: candidateSha, subject }, sameTree };
}

function repositoryOf(repo) {
  if (process.env.GITHUB_REPOSITORY) {
    return process.env.GITHUB_REPOSITORY;
  }
  try {
    const url = git(repo, ['remote', 'get-url', 'origin']);
    const m = /github\.com[:/](.+?)(?:\.git)?$/.exec(url);
    return m === null ? '<owner>/<repo>' : m[1];
  } catch {
    return '<owner>/<repo>';
  }
}

function main(argv) {
  const at = argv.indexOf('--repo');
  if (at >= 0 && (argv[at + 1] === undefined || argv[at + 1].startsWith('--'))) {
    console.error('usage: release-anchors.mjs [--repo <dir>] — --repo needs a directory.');
    return 2;
  }
  const repo = path.resolve(at >= 0 ? argv[at + 1] : process.cwd());
  let lines;
  try {
    const config = JSON.parse(fs.readFileSync(path.join(repo, 'release-please-config.json'), 'utf8'));
    const manifest = JSON.parse(fs.readFileSync(path.join(repo, '.release-please-manifest.json'), 'utf8'));
    lines = Object.entries(config.packages ?? {}).map(([packagePath, pkg]) => {
      const version = manifest[packagePath];
      if (typeof version !== 'string' || typeof pkg.component !== 'string') {
        throw new Error(`${packagePath} has no component in the config or no version in the manifest`);
      }
      return factsFor(repo, packagePath, pkg.component, version);
    });
  } catch (error) {
    console.error(`release-anchors: could not read what to check — the configuration or git: ${error.message}`);
    return 2;
  }
  if (lines.length === 0) {
    console.error('release-anchors: the config declares no packages, so there is nothing this could check.');
    return 2;
  }
  const { code, said } = anchorVerdict(lines, repositoryOf(repo));
  (code === 0 ? console.log : console.error)(said);

  return code;
}

const self = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === self) {
  process.exit(main(process.argv.slice(2)));
}
