import assert from 'node:assert/strict';
import test from 'node:test';

import { GitMark, identitiesOf, identityOf, normalisePath, UNKNOWN_PROJECT } from '../projectIdentity';

/**
 * Which sessions belong to the SAME project, decided without spawning anything.
 *
 * <p><b>Every case here came out of the live table</b> (`%LOCALAPPDATA%/coai-mcp/coai.db`,
 * `sessions.repo_path`, 106 distinct values on 2026-09-18) rather than out of imagination, because
 * the plan's own rule was wrong and the measurement is what showed it. The numbers are in
 * `story22-identity-measured.md`; the ones that decided this module:</p>
 *
 * <ul>
 *   <li>106 raw values, <b>91</b> after case and separator normalisation — 15 were one repository
 *       spelled two ways, and one repository was split THREE ways (`D:\rsd\…`, `d:\rsd\…`,
 *       `D:/rsd/…`, 80 sessions between them).</li>
 *   <li>Stripping a worktree-looking last segment — what the plan said to do — merges
 *       <b>22 directories</b> under `d:/rsd/_wt` (`coai-*`, `creds-*` and `conv-gate`: three
 *       different products) and <b>20</b> under `d:/rsd`, which is every project on the machine in
 *       one tab. So no suffix is ever stripped by pattern, and that is asserted below.</li>
 *   <li>A linked worktree's `.git` FILE names its parent, so 54 live paths resolve to <b>10</b>
 *       identities for the price of one file read each.</li>
 *   <li><b>41 %</b> of the paths are gone. An identity that can only be learned from disk is
 *       therefore unavailable for two in five, which is why absence is a first-class answer here
 *       and not an error.</li>
 * </ul>
 */

/** A filesystem that answers from a table, so a test states what git wrote rather than mocking fs. */
function marksFrom(table: Readonly<Record<string, GitMark>>): (path: string) => GitMark {
  return (path) => table[path] ?? { kind: 'gone' };
}

test('one repository spelled three ways is one project', () => {
  const read = marksFrom({
    'd:/rsd/dew_flow_connect_other_ais': { kind: 'checkout' },
  });

  const keys = [
    'D:\\rsd\\dew_flow_connect_other_ais',
    'd:\\rsd\\dew_flow_connect_other_ais',
    'D:/rsd/dew_flow_connect_other_ais',
    'D:/rsd/dew_flow_connect_other_ais/',
  ].map((raw) => identityOf(raw, read).key);

  assert.equal(new Set(keys).size, 1, 'the live table had 42 + 20 + 18 sessions of ONE repository in three buckets');
  assert.equal(keys[0], 'd:/rsd/dew_flow_connect_other_ais');
});

test('a linked worktree belongs to the repository its .git names', () => {
  // Verbatim from this worktree's own `.git` file.
  const read = marksFrom({
    'c:/users/strug/appdata/local/temp/claude/d--rsd-clauderag/23347fea/scratchpad/wt-rp': {
      kind: 'linked',
      gitdir: 'D:/rsd/dew_flow_connect_other_ais/.git/worktrees/wt-rp',
    },
    'd:/rsd/dew_flow_connect_other_ais': { kind: 'checkout' },
  });

  const one = identityOf('C:/Users/strug/AppData/Local/Temp/claude/d--rsd-ClaudeRag/23347fea/scratchpad/wt-rp', read);

  assert.equal(one.key, 'd:/rsd/dew_flow_connect_other_ais');
  assert.equal(one.label, 'dew_flow_connect_other_ais', 'the tab is named after the repository, not the scratch directory');
  assert.ok(one.reachable);
});

test('two worktrees in ONE directory belonging to two products stay apart', () => {
  // Finding 0, and it is not hypothetical: these two are real siblings in the live table.
  const read = marksFrom({
    'd:/rsd/_wt/coai-audit': { kind: 'linked', gitdir: 'D:/rsd/dew_flow_connect_other_ais/.git/worktrees/coai-audit' },
    'd:/rsd/_wt/creds-form-chrome': { kind: 'linked', gitdir: 'D:/rsd/dew_flow_creds_for_devs/.git/worktrees/creds-form-chrome' },
  });

  const a = identityOf('D:\\rsd\\_wt\\coai-audit', read);
  const b = identityOf('D:\\rsd\\_wt\\creds-form-chrome', read);

  assert.notEqual(a.key, b.key, 'stripping the last segment put 22 directories of three products in one tab');
  assert.equal(a.key, 'd:/rsd/dew_flow_connect_other_ais');
  assert.equal(b.key, 'd:/rsd/dew_flow_creds_for_devs');
});

test('a submodule INSIDE a worktree is its own project, not the worktree parent', () => {
  // The trap the measurement uncovered and no reviewer named. Verbatim from `.agents/conventions/.git`
  // in this checkout — cutting at `/.git/worktrees/` would file dew_flow_conventions under
  // connect_other_ais, and conventions has its own sessions in the live table.
  const read = marksFrom({
    'd:/rsd/dew_flow_conventions': {
      kind: 'linked',
      gitdir: 'D:/rsd/dew_flow_connect_other_ais/.git/worktrees/wt-rp/modules/.claude/rules/shared',
    },
  });

  const one = identityOf('D:/rsd/dew_flow_conventions', read);

  assert.equal(one.key, 'd:/rsd/dew_flow_conventions',
    'a gitdir under a worktree names the PARENT only when exactly one segment follows /worktrees/');
  assert.notEqual(one.key, 'd:/rsd/dew_flow_connect_other_ais');
});

test('a submodule in a main checkout, whose gitdir git writes relative, is its own project', () => {
  const read = marksFrom({
    'd:/rsd/some_repo/vendor/thing': { kind: 'linked', gitdir: '../../.git/modules/vendor/thing' },
  });

  assert.equal(identityOf('d:/rsd/some_repo/vendor/thing', read).key, 'd:/rsd/some_repo/vendor/thing');
});

test('a path that is gone is its own project, marked unreachable, and merged with nothing', () => {
  // 41 % of the corpus. The identity cannot be learned, so nothing is invented for it.
  const read = marksFrom({});

  const a = identityOf('D:\\rsd\\_wt\\coai-gone', read);
  const b = identityOf('D:\\rsd\\_wt\\creds-gone', read);

  assert.equal(a.reachable, false);
  assert.equal(a.key, 'd:/rsd/_wt/coai-gone', 'no suffix is stripped — that is what merged 22 directories');
  assert.notEqual(a.key, 'd:/rsd/_wt');
  assert.notEqual(a.key, b.key, 'two unreachable paths are two unknowns, not one project');
});

test('a relative or empty repo_path is one explicit unknown', () => {
  const read = marksFrom({});

  // 33 sessions in the live table carry `.` — the second largest bucket, and it says nothing at all.
  assert.equal(identityOf('.', read).key, UNKNOWN_PROJECT);
  assert.equal(identityOf('', read).key, UNKNOWN_PROJECT);
  assert.equal(identityOf('   ', read).key, UNKNOWN_PROJECT);
  assert.equal(identityOf('.', read).reachable, false);
  assert.equal(identityOf('.', read).label, 'Unknown project');
});

test('each distinct path is asked about ONCE however many sessions share it', () => {
  // The draw already spends 468 ms highlighting; the identity rule must not add a read per row.
  let asked = 0;
  const read = (path: string): GitMark => {
    asked += 1;
    return path === 'd:/rsd/repo' ? { kind: 'checkout' } : { kind: 'gone' };
  };

  const found = identitiesOf(
    ['D:\\rsd\\repo', 'd:/rsd/repo', 'D:/rsd/repo/', 'D:\\rsd\\other', '.', '.'],
    read,
  );

  assert.equal(asked, 2, 'six paths, two distinct normalised ones that exist on disk');
  assert.equal(found.size, 3, 'repo, other, and the unknown');
  assert.equal(found.get('d:/rsd/repo')?.label, 'repo');
});

test('normalising is separators, a trailing slash and case — and nothing else', () => {
  assert.equal(normalisePath('D:\\rsd\\A\\B\\'), 'd:/rsd/a/b');
  assert.equal(normalisePath('D:/rsd//A'), 'd:/rsd/a', 'a doubled separator is one');
  assert.equal(normalisePath('/'), '/', 'a root is not normalised away to nothing');
  assert.equal(normalisePath('d:/rsd/repo'), 'd:/rsd/repo', 'an already-normal path is untouched');
});
