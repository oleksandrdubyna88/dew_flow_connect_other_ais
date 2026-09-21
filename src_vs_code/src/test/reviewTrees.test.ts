import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  canOpen, ListedTree, RemovalAnswer, RemovalRead, removalSentence, treeLine, TreesRead,
} from '../reviewTree';
import { readTrees, removeTree } from '../reviewTreeRead';
import { Choice, manageReviewTrees, TreesDeps } from '../reviewTreesCommand';

/**
 * Story 3.2b's decisions, as values: what the readers believe, what a person is told, and what the
 * picker does with each answer.
 *
 * <p>The flow takes its world as a parameter, so every path through it — including the two that must
 * never remove anything — runs here without an editor, a server or a repository.</p>
 */

const TREE: ListedTree = {
  name: 'coai-review-0badc0de-aaaa111bbbb2',
  repository: 'D:/repo/.git',
  repoPath: 'D:/repo',
  sha: 'aaaa111bbbb2222cccc3333dddd4444eeee5555f',
  path: 'C:/trees/coai-review-0badc0de-aaaa111bbbb2',
  created: '2026-09-21T10:00:00Z',
  state: 'ready',
};

const printing = (out: unknown, code = 0) => async (args: readonly string[]) => {
  asked.push([...args]);

  return { code, output: typeof out === 'string' ? out : JSON.stringify(out) };
};

let asked: string[][] = [];

const served = (over: Record<string, unknown> = {}): Record<string, unknown> => ({
  root: 'C:/trees',
  trees: [{ ...TREE }],
  reason: '',
  ...over,
});

// --------------------------------------------------------------------------------------------
// The readers.
// --------------------------------------------------------------------------------------------

test('the list is shaped item by item, and a row with no name is dropped rather than shown', async () => {
  asked = [];
  const read = await readTrees('coai-mcp', printing(served({
    trees: [{ ...TREE }, null, 7, { state: 'ready' }],
  })));

  assert.ok(read.ok);
  assert.equal(read.answer.trees.length, 1, 'a null, a number and a nameless row are not trees');
  assert.equal(read.answer.trees[0]?.name, TREE.name);
  assert.deepEqual(asked[0], ['--trees'], 'listing takes no arguments at all');
});

test('64 on either mode is the only thing that means the server is too old', async () => {
  const listed = await readTrees('coai-mcp', printing('', 64));
  assert.ok(!listed.ok && listed.tooOld);

  const removed = await removeTree('coai-mcp', TREE.name, false, printing('', 64));
  assert.ok(!removed.ok && removed.tooOld);

  // 65 is a request fault and must carry the server's own sentence instead.
  const fault = await removeTree('coai-mcp', TREE.name, false, printing('--tree-remove needs --tree <name>', 65));
  assert.ok(!fault.ok);
  assert.equal(fault.tooOld, false, 'a fault wearing the fallback hides behind a version nobody can check');
  assert.match(fault.why, /needs --tree/u);
});

test('a removal answer about another name, or with no reason, is refused', async () => {
  const other = await removeTree('coai-mcp', TREE.name, false, printing({ name: 'coai-review-somethingelse', reason: 'removed' }));
  assert.equal(other.ok, false, 'an answer about another tree would put its sentence on this row');

  const silent = await removeTree('coai-mcp', TREE.name, false, printing({ name: TREE.name, reason: '' }));
  assert.equal(silent.ok, false, 'something always happened, so an empty reason is not an answer');
});

test('--with-ignored is sent only when it is asked for', async () => {
  asked = [];
  await removeTree('coai-mcp', TREE.name, false, printing({ name: TREE.name, reason: 'removed' }));
  assert.deepEqual(asked[0], ['--tree-remove', '--tree', TREE.name]);

  asked = [];
  await removeTree('coai-mcp', TREE.name, true, printing({ name: TREE.name, reason: 'removed' }));
  assert.deepEqual(asked[0], ['--tree-remove', '--tree', TREE.name, '--with-ignored']);
});

// --------------------------------------------------------------------------------------------
// What a person is told.
// --------------------------------------------------------------------------------------------

const answer = (over: Partial<RemovalAnswer> = {}): RemovalAnswer =>
  ({ name: TREE.name, reason: 'removed', inTheWay: [], ignored: 0, ignoredSample: [], ...over });

test('a refusal NAMES what is in the way, including files inside a submodule', () => {
  const said = removalSentence(answer({ reason: 'dirty', inTheWay: ['a.txt', 'mods/sub/notes.md'] }));

  assert.match(said, /a\.txt/u);
  assert.match(said, /mods\/sub\/notes\.md/u, 'the mount alone is not something a person can act on');
  assert.match(said, /nothing was removed/u);
});

test('ignored files are counted, sampled and never silently swept', () => {
  const said = removalSentence(answer({ reason: 'has_ignored', ignored: 3, ignoredSample: ['.env', 'build/x'] }));

  assert.match(said, /3 ignored files/u);
  assert.match(said, /\.env/u, 'a count nobody can check is a count nobody can judge');

  assert.match(removalSentence(answer({ ignored: 1 })), /1 ignored file went with it/u);
  assert.equal(removalSentence(answer()), 'removed');
});

test('every removal word becomes a sentence, and none of them prints the word', () => {
  for (const reason of ['forgotten', 'not_ours', 'git_failed', 'unregistered', 'unreachable']) {
    const said = removalSentence(answer({ reason }));

    assert.ok(said.length > 0, `${reason} must say something`);
    assert.doesNotMatch(said, /_/u, `${reason} leaked our vocabulary`);
  }
});

test('a tree reads as what it is, and only a ready one offers to be opened', () => {
  assert.match(treeLine(TREE), /repo @ aaaa111 · 2026-09-21 · ready/u);
  assert.equal(canOpen(TREE), true);

  for (const state of ['incomplete', 'vanished', 'unregistered', 'unreachable']) {
    assert.equal(canOpen({ ...TREE, state }), false, `${state} cannot be opened`);
    assert.doesNotMatch(treeLine({ ...TREE, state }), /_/u);
  }
});

// --------------------------------------------------------------------------------------------
// The flow — including the two paths that must never remove anything.
// --------------------------------------------------------------------------------------------

interface Watched {
  readonly deps: TreesDeps;
  readonly removed: { name: string; withIgnored: boolean }[];
  readonly opened: string[];
  readonly said: string[];
  readonly titles: string[];
}

const watching = (
  list: TreesRead,
  answers: readonly RemovalRead[],
  picks: readonly (string | undefined)[],
): Watched => {
  const removed: { name: string; withIgnored: boolean }[] = [];
  const opened: string[] = [];
  const said: string[] = [];
  const titles: string[] = [];
  let pick = 0;
  let removal = 0;

  return {
    removed, opened, said, titles,
    deps: {
      list: async () => list,
      remove: async (name, withIgnored) => {
        removed.push({ name, withIgnored });

        return answers[removal++] ?? { ok: false, tooOld: false, why: 'no answer was prepared' };
      },
      open: async (path) => { opened.push(path); },
      pick: async (choices: readonly Choice[], title: string) => {
        titles.push(title);
        const wanted = picks[pick++];

        return wanted === undefined ? undefined : choices.find((one) => one.id === wanted);
      },
      say: (one) => said.push(one),
    },
  };
};

const listed = (trees: readonly ListedTree[], reason = ''): TreesRead =>
  ({ ok: true, answer: { root: 'C:/trees', trees, reason } });

test('an empty machine says so and names where they would be, and asks nothing', async () => {
  const w = watching(listed([]), [], []);

  await manageReviewTrees(w.deps);

  assert.match(w.said[0] ?? '', /no review checkouts/u);
  assert.match(w.said[0] ?? '', /C:\/trees/u);
  assert.equal(w.titles.length, 0, 'there is nothing to choose between');
});

test('choosing a tree and opening it uses the opener, and removes nothing', async () => {
  const w = watching(listed([TREE]), [], [TREE.name, 'open']);

  await manageReviewTrees(w.deps);

  assert.deepEqual(w.opened, [TREE.path]);
  assert.deepEqual(w.removed, [], 'opening is not removing');
});

test('a dirty tree is refused, said by name, and nothing is asked twice', async () => {
  const w = watching(
    listed([TREE]),
    [{ ok: true, answer: answer({ reason: 'dirty', inTheWay: ['mods/sub/notes.md'] }) }],
    [TREE.name, 'remove']);

  await manageReviewTrees(w.deps);

  assert.deepEqual(w.removed, [{ name: TREE.name, withIgnored: false }],
    'a refusal must not be followed by a second, harder attempt');
  assert.match(w.said.join(' '), /mods\/sub\/notes\.md/u);
});

test('ignored files are asked about once, and KEEPING them removes nothing', async () => {
  const w = watching(
    listed([TREE]),
    [{ ok: true, answer: answer({ reason: 'has_ignored', ignored: 2, ignoredSample: ['.env'] }) }],
    [TREE.name, 'remove', 'no']);

  await manageReviewTrees(w.deps);

  assert.deepEqual(w.removed, [{ name: TREE.name, withIgnored: false }],
    'saying no must not send the removal anyway');
  assert.match(w.titles.join(' '), /2 ignored files/u, 'the question says how many, not merely "are you sure"');
});

test('ignored files are removed only after the second ask, and the answer says how many went', async () => {
  const w = watching(
    listed([TREE]),
    [
      { ok: true, answer: answer({ reason: 'has_ignored', ignored: 2, ignoredSample: ['.env'] }) },
      { ok: true, answer: answer({ reason: 'removed', ignored: 2 }) },
    ],
    [TREE.name, 'remove', 'yes']);

  await manageReviewTrees(w.deps);

  assert.deepEqual(w.removed, [
    { name: TREE.name, withIgnored: false },
    { name: TREE.name, withIgnored: true },
  ], 'the flag is the person’s second ask and is never a default');
  assert.match(w.said.join(' '), /2 ignored files went with it/u);
});

test('walking away at any step removes nothing', async () => {
  for (const picks of [[undefined], [TREE.name, undefined], [TREE.name, 'remove', undefined]] as const) {
    const w = watching(
      listed([TREE]),
      [{ ok: true, answer: answer({ reason: 'has_ignored', ignored: 1, ignoredSample: ['.env'] }) }],
      picks);

    await manageReviewTrees(w.deps);

    assert.deepEqual(w.opened, []);
    assert.ok(w.removed.every((one) => !one.withIgnored), 'nothing irreversible may happen after a dismissal');
  }
});

test('a server that will not answer is said, and nothing is chosen', async () => {
  const w = watching({ ok: false, tooOld: true, why: 'this server is too old' }, [], []);

  await manageReviewTrees(w.deps);

  assert.match(w.said[0] ?? '', /too old/u);
  assert.equal(w.titles.length, 0);
});

test('a list that carries a reason is said rather than shown as empty', async () => {
  const w = watching(listed([], 'the root could not be read'), [], []);

  await manageReviewTrees(w.deps);

  assert.match(w.said[0] ?? '', /could not be listed/u);
  assert.match(w.said[0] ?? '', /root could not be read/u);
});

test('a document with no tree list is refused rather than read as an empty machine', async () => {
  // Saying "you hold none" about a document nobody understands invites a person to check another
  // commit out into a root that may already hold ten. (Code round, codex.)
  for (const out of [{}, { root: 'C:/trees' }, { trees: 'not an array' }]) {
    const read = await readTrees('coai-mcp', printing(out));

    assert.equal(read.ok, false, `${JSON.stringify(out)} must not read as an empty machine`);
  }

  // A REASON is the one shape allowed to carry no list.
  const said = await readTrees('coai-mcp', printing({ root: 'C:/trees', reason: 'the folder could not be read' }));
  assert.ok(said.ok);
  assert.match(said.answer.reason, /could not be read/u);
});
