import assert from 'node:assert/strict';
import test from 'node:test';

import { ReviewPair } from '../reviewPair';
import { askedOnce, GitMark } from '../projectIdentity';
import { ALL, reviewTabs } from '../reviewTabs';

/**
 * Which pairs are on screen — by project, then by language.
 *
 * <p><b>The blank-table defect is closed by construction here, not by a guard.</b> Two plan
 * reviewers reported the same thing independently: C# selected in project A, switch to project B
 * which is only TypeScript, and the filter matches nothing. The language tabs are rebuilt from the
 * pairs of the SELECTED project, so a language that is not in the new project is not a tab — and a
 * held choice that is not a tab falls back to everything. There is therefore no combination of a
 * held project and a held language that shows an empty table, and that is asserted rather than
 * assumed.</p>
 *
 * <p><b>Both filters default to everything</b>, because the page shows every pair today and an
 * opening screen that had already hidden rows would be worse than the one being improved.</p>
 *
 * <p><b>A strip is drawn only when it offers a choice.</b> With one project, `All projects · 9` and
 * `that_project · 9` are two tabs that do the same thing — furniture on a page whose entire story
 * is that it can be read. The live corpus is one project and two languages today (9 pairs,
 * measured 2026-09-18), so this is the ordinary case rather than an edge one.</p>
 */

function pair(over: Partial<ReviewPair>): ReviewPair {
  return {
    findingId: 1,
    symbolName: 'method_1',
    language: 'TypeScript',
    skeletonBefore: 'a',
    skeletonAfter: 'b',
    keep: -1,
    severity: 'Major',
    category: 'correctness',
    title: 'something',
    repoPath: 'D:/rsd/repo_a',
    headSha: 'aaa',
    fixSha: 'bbb',
    file: 'src/x.ts',
    line: 3,
    why: '',
    fix: '',
    comment: '',
    sentUtc: '',
    commentLost: '',
    ...over,
  };
}

/**
 * Two real repositories and a worktree of the first — the corpus's own shape.
 *
 * <p>Keyed by the path the reader is ASKED about: separators normalised, case KEPT. `identityOf`
 * folds case for the KEY only, because folding it before the filesystem lookup asks about a
 * directory that exists nowhere case matters — green on Windows, red on the Ubuntu runner.</p>
 */
const READ = (path: string): GitMark => {
  const known: Readonly<Record<string, GitMark>> = {
    'D:/rsd/repo_a': { kind: 'checkout' },
    'D:/rsd/repo_b': { kind: 'checkout' },
    'D:/rsd/_wt/wt-a': { kind: 'linked', gitdir: 'D:/rsd/repo_a/.git/worktrees/wt-a' },
  };

  return known[path] ?? { kind: 'gone' };
};

const HELD = { project: ALL, language: ALL };

test('a worktree of a project is not a project of its own', () => {
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a' }),
      pair({ findingId: 2, repoPath: 'D:\\rsd\\_wt\\wt-a' }),
      pair({ findingId: 3, repoPath: 'D:/rsd/repo_b' }),
    ],
    HELD,
    READ,
  );

  assert.deepEqual(found.projects.map((t) => t.label), ['All projects · 3', 'repo_a · 2', 'repo_b · 1'],
    'the worktree pair counts towards repo_a, which is the whole point of the identity rule');
});

test('choosing a project shows only its pairs, and only its languages', () => {
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a', language: 'C#' }),
      pair({ findingId: 2, repoPath: 'D:/rsd/repo_b', language: 'TypeScript' }),
      pair({ findingId: 3, repoPath: 'D:/rsd/repo_b', language: 'JavaScript' }),
    ],
    { project: 'd:/rsd/repo_b', language: ALL },
    READ,
  );

  assert.deepEqual(found.shown.map((p) => p.findingId), [2, 3]);
  assert.deepEqual(found.languages.map((t) => t.label), ['All languages · 2', 'TypeScript · 1', 'JavaScript · 1'],
    'C# is in the corpus but not in this project, so it is not offered here');
});

test('a language held from another project cannot blank the table', () => {
  // Findings 1 and 2, as a state the page can actually be put in: the person was in repo_a looking
  // at C#, and pressed the repo_b tab.
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a', language: 'C#' }),
      pair({ findingId: 2, repoPath: 'D:/rsd/repo_b', language: 'TypeScript' }),
    ],
    { project: 'd:/rsd/repo_b', language: 'C#' },
    READ,
  );

  assert.equal(found.language, ALL, 'a choice that selects nothing is dropped, not kept');
  assert.deepEqual(found.shown.map((p) => p.findingId), [2]);
});

test('a project that is no longer in the corpus falls back to all of them', () => {
  const found = reviewTabs(
    [pair({ findingId: 1, repoPath: 'D:/rsd/repo_a' }), pair({ findingId: 2, repoPath: 'D:/rsd/repo_b' })],
    { project: 'd:/rsd/deleted', language: ALL },
    READ,
  );

  assert.equal(found.project, ALL);
  assert.deepEqual(found.shown.map((p) => p.findingId), [1, 2]);
});

test('a held language that IS in the project survives the redraw', () => {
  // The companion: a rule that always fell back would pass the test above and throw the person's
  // choice away on every poll.
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a', language: 'C#' }),
      pair({ findingId: 2, repoPath: 'D:/rsd/repo_a', language: 'TypeScript' }),
    ],
    { project: 'd:/rsd/repo_a', language: 'C#' },
    READ,
  );

  assert.equal(found.language, 'C#');
  assert.deepEqual(found.shown.map((p) => p.findingId), [1]);
});

test('a path that is gone is a project of its own, and its tab says so', () => {
  // 41 % of the live corpus. It is not hidden and not merged with anything — and the tab says the
  // checkout is not there, because one that looked ordinary would promise a fix that cannot open.
  const found = reviewTabs(
    [pair({ findingId: 1, repoPath: 'D:/rsd/_wt/vanished' }), pair({ findingId: 2, repoPath: 'D:/rsd/repo_a' })],
    HELD,
    READ,
  );

  const vanished = found.projects.find((t) => t.label.startsWith('vanished'));
  assert.ok(vanished !== undefined, 'a project whose checkout is gone still has its pairs to review');
  assert.match(vanished.title ?? '', /not on disk/u);
  // The path AS RECORDED, not folded: the tooltip is the one field somebody copies into a
  // terminal, so only the grouping key is lower-cased.
  assert.equal(found.projects.find((t) => t.label.startsWith('repo_a'))?.title, 'D:/rsd/repo_a');
});

test('sessions that recorded no path are ONE unknown, not one per pair', () => {
  // 33 sessions of the live corpus carry `.`, which identifies nothing at all.
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: '.' }),
      pair({ findingId: 2, repoPath: '' }),
      pair({ findingId: 3, repoPath: 'D:/rsd/repo_a' }),
    ],
    HELD,
    READ,
  );

  assert.deepEqual(found.projects.map((t) => t.label), ['All projects · 3', 'Unknown project · 2', 'repo_a · 1']);
  assert.match(found.projects[1].title ?? '', /no path/u);
});

test('the tabs carry a slug, because a project key is a path', () => {
  const found = reviewTabs(
    [pair({ findingId: 1, repoPath: 'D:/rsd/repo_a' }), pair({ findingId: 2, repoPath: 'D:/rsd/repo_b' })],
    HELD,
    READ,
  );

  assert.deepEqual(found.projects.map((t) => t.slug), ['all', '0', '1']);
  assert.equal(found.projects[1].key, 'd:/rsd/repo_a', 'the real key is what the page posts back');
});

test('projects and languages are ordered by how many pairs they hold', () => {
  // Ten projects is a strip a person reads left to right; the biggest first puts what they are
  // working on where they look.
  const found = reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a', language: 'C#' }),
      pair({ findingId: 2, repoPath: 'D:/rsd/repo_b', language: 'TypeScript' }),
      pair({ findingId: 3, repoPath: 'D:/rsd/repo_b', language: 'TypeScript' }),
    ],
    HELD,
    READ,
  );

  assert.deepEqual(found.projects.map((t) => t.key), [ALL, 'd:/rsd/repo_b', 'd:/rsd/repo_a']);
  assert.deepEqual(found.languages.map((t) => t.key), [ALL, 'TypeScript', 'C#']);
});

test('one project is no strip, because there is nothing to choose between', () => {
  // The live corpus, today: nine pairs, one project, two languages. The project strip would be
  // `All projects · 9` beside `dew_flow_connect_other_ais · 9` — two tabs doing one thing.
  const found = reviewTabs(
    [pair({ findingId: 1, language: 'TypeScript' }), pair({ findingId: 2, language: 'JavaScript' })],
    HELD,
    READ,
  );

  assert.deepEqual(found.projects, []);
  assert.deepEqual(found.languages.map((t) => t.key), [ALL, 'TypeScript', 'JavaScript'],
    'the language strip DOES offer a choice here, and is drawn');
  assert.deepEqual(found.shown.map((p) => p.findingId), [1, 2], 'and every pair is still shown');
});

test('one language is no language strip either', () => {
  const found = reviewTabs([pair({ findingId: 1 }), pair({ findingId: 2 })], HELD, READ);

  assert.deepEqual(found.projects, []);
  assert.deepEqual(found.languages, []);
  assert.deepEqual(found.shown.map((p) => p.findingId), [1, 2]);
});

test('a pair whose language the server did not say is its own named bucket', () => {
  // `pairOf` fills an absent field with an empty string, so a server older than story 2.1 sends
  // pairs with no language at all. An empty key must not collide with the "everything" tab — which
  // is why ALL is a sentinel no path and no language can be, rather than the empty string.
  const found = reviewTabs(
    [pair({ findingId: 1, language: '' }), pair({ findingId: 2, language: 'TypeScript' })],
    HELD,
    READ,
  );

  assert.deepEqual(found.languages.map((t) => t.label), ['All languages · 2', 'Unknown language · 1', 'TypeScript · 1']);

  const chosen = reviewTabs([pair({ findingId: 1, language: '' }), pair({ findingId: 2, language: 'TypeScript' })],
    { project: ALL, language: '' }, READ);

  assert.deepEqual(chosen.shown.map((p) => p.findingId), [1], 'choosing the unknown bucket shows exactly it');
});

test('the filesystem is asked about each distinct path once, not once per pair', () => {
  let asked = 0;
  const once = askedOnce((path) => {
    asked += 1;
    return READ(path);
  });

  reviewTabs(
    [
      pair({ findingId: 1, repoPath: 'D:/rsd/repo_a' }),
      pair({ findingId: 2, repoPath: 'd:\\rsd\\repo_a' }),
      pair({ findingId: 3, repoPath: 'D:/rsd/repo_a/' }),
      pair({ findingId: 4, repoPath: 'D:/rsd/repo_b' }),
    ],
    HELD,
    once,
  );

  // THREE: `D:/rsd/repo_a` and `d:\\rsd\\repo_a` are one directory on Windows and two strings, and
  // the case fold is deliberately not applied before the filesystem lookup. One extra `existsSync`
  // per duplicated spelling per draw, against an identity rule that answered wrongly on Linux.
  assert.equal(asked, 3, 'four pairs, two spellings of one path, and one other');
});

test('a second call with the SAME reader touches no disk — which is what a filter press is', () => {
  // Two code reviewers caught this: memoising INSIDE the grouping meant every language press
  // re-probed all 91 paths of the live corpus, synchronously, on the extension host — and 41 % of
  // them do not exist, while a recorded UNC path on a sleeping server blocks until it answers. So
  // the reader is the caller's to hold, and this is the saving it exists for.
  let asked = 0;
  const once = askedOnce((path) => {
    asked += 1;
    return READ(path);
  });
  const pairs = [
    pair({ findingId: 1, repoPath: 'D:/rsd/repo_a', language: 'C#' }),
    pair({ findingId: 2, repoPath: 'D:/rsd/repo_b', language: 'TypeScript' }),
  ];

  reviewTabs(pairs, HELD, once);
  const afterTheDraw = asked;

  // The person presses a project tab, then a language tab. Both repaint.
  reviewTabs(pairs, { project: 'd:/rsd/repo_b', language: ALL }, once);
  reviewTabs(pairs, { project: 'd:/rsd/repo_b', language: 'TypeScript' }, once);

  assert.equal(afterTheDraw, 2, 'the first draw learns both paths');
  assert.equal(asked, 2, 'and two filter presses learn nothing new, because nothing changed on disk');
});
