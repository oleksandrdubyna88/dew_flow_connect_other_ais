import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ReviewPair } from '../reviewPair';
import { mountsNote, ReviewTreeAnswer, TreeRead, treeSentence } from '../reviewTree';
import { readTreeAt } from '../reviewTreeRead';
import { RevisionItem, RevisionPanel, RevisionReach } from '../revisionPanel';

/**
 * The review tree's DECISIONS, as values — the half neither a source-reading test nor a page test
 * can reach.
 *
 * <p>Story 3.1 put everything decidable in `openAtRevision.ts` and tested it here, as values; this
 * story shipped its own decisions — what an answer must match before it is believed, what a person
 * is told instead of a reason word, what a press does with each answer — and tested the SEAMS around
 * them instead. SonarCloud said so in the only way that is hard to argue with: 85 of `reviewTreeRead`'s
 * 142 new lines were never executed by anything. That is this file.</p>
 *
 * <p>Nothing here spawns anything. `readTreeAt` takes its runner as a parameter, which is what makes
 * the shaping testable without a binary; the live contract suite runs the real one.</p>
 */

const SHA = 'aaaa111bbbb2222cccc3333dddd4444eeee5555f';
const REPO = 'D:/repo';
const TREE = 'C:/Users/x/AppData/Local/coai-mcp/review-worktrees/coai-review-0badc0de-aaaa111bbbb2';

/** What the server would print, as an object; the tests serialise it. */
const served = (over: Record<string, unknown> = {}): Record<string, unknown> => ({
  findingId: 7,
  sha: SHA,
  repoPath: REPO,
  path: TREE,
  repository: 'D:/repo/.git',
  reused: false,
  reason: '',
  emptyMounts: [],
  trees: [],
  ...over,
});

/** A runner that answers one canned stdout at exit 0, and records what it was asked. */
const printing = (out: unknown, code = 0): { run: (a: readonly string[]) => Promise<{ code: number; output: string }>; asked: string[][] } => {
  const asked: string[][] = [];

  return {
    asked,
    run: async (args) => {
      asked.push([...args]);

      return { code, output: typeof out === 'string' ? out : JSON.stringify(out) };
    },
  };
};

const asked = { findingId: 7, headSha: SHA, repoPath: REPO } as const;

// --------------------------------------------------------------------------------------------
// What the reader believes, and what it refuses.
// --------------------------------------------------------------------------------------------

test('a well-formed answer is accepted whole, with every collection present', async () => {
  const { run, asked: sent } = printing(served({ reused: true, emptyMounts: ['mods/rules'] }));

  const read = await readTreeAt('coai-mcp', asked, run);

  assert.equal(read.ok, true);
  assert.ok(read.ok);
  assert.equal(read.tree.path, TREE);
  assert.equal(read.tree.reused, true);
  assert.deepEqual([...read.tree.emptyMounts], ['mods/rules']);
  assert.deepEqual(sent[0], ['--tree-at', '--id', '7'], 'the id is the only coordinate on argv');
});

test('an answer about another row, another commit or another repository is refused', async () => {
  const cases: readonly (readonly [string, Record<string, unknown>])[] = [
    ['another row', { findingId: 8 }],
    ['another commit', { sha: 'ffff111bbbb2222cccc3333dddd4444eeee5555f' }],
    // The id and the commit alone are not an identity: a pair recollected while the request was in
    // flight can answer the same finding at the same commit for a DIFFERENT repository, and this one
    // would open a window on it. (Code round, codex.)
    ['another repository', { repoPath: 'D:/somewhere-else' }],
  ];

  for (const [what, over] of cases) {
    const { run } = printing(served(over));
    const read = await readTreeAt('coai-mcp', asked, run);

    assert.equal(read.ok, false, `${what} must not be believed`);
    assert.ok(!read.ok && !read.tooOld, 'and it is not an old server either');
  }
});

test('a REASON-only answer is believed although it echoes no coordinates', async () => {
  // `pair_not_found` has no tree and no commit to echo. Refusing it would turn a true answer into
  // silence, which is the same trap story 3.1 documented for its own reader.
  const { run } = printing({ findingId: 7, reason: 'pair_not_found' });

  const read = await readTreeAt('coai-mcp', asked, run);

  assert.ok(read.ok);
  assert.equal(read.tree.reason, 'pair_not_found');
  assert.equal(read.tree.path, '');
  assert.deepEqual([...read.tree.trees], [], 'a missing collection reads as empty, never undefined');
});

test('a tree at a path that is not absolute is refused before any window opens', async () => {
  for (const path of ['review-worktrees/coai-review-1', '', './somewhere']) {
    const { run } = printing(served({ path }));
    const read = await readTreeAt('coai-mcp', asked, run);

    assert.equal(read.ok, false, `'${path}' must not become a folder this editor opens`);
  }
});

test('an absolute path in either shape is accepted, because the server may be either OS', async () => {
  for (const path of ['/home/me/.local/share/coai-mcp/review-worktrees/x', 'C:\\Users\\x\\y', 'D:/repo/x']) {
    const { run } = printing(served({ path }));
    const read = await readTreeAt('coai-mcp', asked, run);

    assert.equal(read.ok, true, `'${path}' is absolute and must be accepted`);
  }
});

test('64 is the only code that means the server is too old; every other one is its own sentence', async () => {
  const tooOld = await readTreeAt('coai-mcp', asked, printing('', 64).run);
  assert.ok(!tooOld.ok && tooOld.tooOld, '64 and only 64 sends the page down the fallback');

  const fault = await readTreeAt('coai-mcp', asked, printing('--tree-at needs --id <findingId>', 65).run);
  assert.ok(!fault.ok);
  assert.equal(fault.tooOld, false, 'a request fault wearing 64 would hide behind "your server is too old"');
  assert.match(fault.why, /needs --id/u, "the server's own sentence is what the row says");

  const quiet = await readTreeAt('coai-mcp', asked, printing('', 74).run);
  assert.ok(!quiet.ok && !quiet.tooOld);
  assert.match(quiet.why, /74/u, 'a silent failure still names its code');
});

test('output that is not JSON, or is not an object, is refused rather than guessed at', async () => {
  for (const out of ['', 'not json at all', '[]', 'null', '"a string"']) {
    const read = await readTreeAt('coai-mcp', asked, printing(out).run);

    assert.equal(read.ok, false, `'${out}' must not be read as an answer`);
  }
});

test('a tree list is shaped item by item, and rubbish inside it is dropped rather than trusted', async () => {
  const { run } = printing(served({
    reason: 'budget',
    path: '',
    trees: [{ repository: 'D:/a/.git', sha: SHA, path: 'D:/t1', created: '2026-09-01T00:00:00Z' }, null, 7, {}],
  }));

  const read = await readTreeAt('coai-mcp', asked, run);

  assert.ok(read.ok);
  assert.equal(read.tree.trees.length, 2, 'the two objects survive; the null and the number do not');
  assert.equal(read.tree.trees[0]?.path, 'D:/t1');
  assert.equal(read.tree.trees[1]?.path, '', 'an object with no fields reads as empty strings, never undefined');
});

// --------------------------------------------------------------------------------------------
// What a person is told — and it is never the reason word.
// --------------------------------------------------------------------------------------------

const answer = (over: Partial<ReviewTreeAnswer> = {}): ReviewTreeAnswer => ({
  findingId: 7, sha: SHA, repoPath: REPO, path: TREE, repository: 'D:/repo/.git',
  reused: false, reason: '', emptyMounts: [], trees: [], ...over,
});

test('the cap refusal NAMES the checkouts using it up, not merely how many there are', () => {
  const said = treeSentence(answer({
    reason: 'budget',
    path: '',
    trees: [
      { repository: 'D:/a/.git', sha: 'bbbb222ccc', path: 'C:/trees/one', created: '2026-09-01T10:00:00Z' },
      { repository: 'D:/b/.git', sha: 'cccc333ddd', path: 'C:/trees/two', created: '2026-09-02T10:00:00Z' },
    ],
  }));

  assert.match(said, /2 review checkouts/u);
  // A refusal that says "you have ten" without saying which ten leaves a person no move.
  assert.match(said, /C:\/trees\/one/u);
  assert.match(said, /C:\/trees\/two/u);
  assert.match(said, /2026-09-01/u, 'the day is how a person tells an old one from today’s');
});

test('every reason word becomes a sentence, and none of them prints the word itself', () => {
  const words = ['pair_not_found', 'repo_path_missing', 'git_failed', 'commit_unreachable', 'in_progress'];

  for (const reason of words) {
    const said = treeSentence(answer({ reason, path: '' }));

    assert.ok(said.length > 0, `${reason} must say something`);
    assert.doesNotMatch(said, /_/u, `${reason} leaked our vocabulary into the page`);
  }
});

test('a half-made checkout somebody edited is named by its PATH, because they are asked to look at it', () => {
  const said = treeSentence(answer({ reason: 'incomplete_and_dirty', path: 'C:/trees/half' }));

  assert.match(said, /C:\/trees\/half/u);
  assert.match(said, /by hand/u, 'the next move is theirs, and the sentence says so');
});

test('an in-progress checkout says it survives a reload, because it runs in the server', () => {
  const said = treeSentence(answer({ reason: 'in_progress', path: '' }));

  assert.match(said, /reloaded/u, 'the local flag dies with the window; the run does not');
});

test('a reason nobody spelled still says something rather than nothing', () => {
  assert.match(treeSentence(answer({ reason: 'something_new_the_server_learned' })), /could not be made/u);
});

test('empty submodule mounts are said on the row, and silence means there were none', () => {
  assert.equal(mountsNote(answer()), '');
  assert.match(mountsNote(answer({ emptyMounts: ['mods/rules', 'mods/deps'] })), /mods\/rules, mods\/deps/u);
});

// --------------------------------------------------------------------------------------------
// The press: what it says while it works, and what it does with each answer.
// --------------------------------------------------------------------------------------------

const pair = (findingId = 7): ReviewPair => ({
  findingId, symbolName: 'Totals', language: 'csharp', skeletonBefore: '', skeletonAfter: '',
  keep: -1, severity: 'major', category: 'bug', title: 't', repoPath: REPO, headSha: SHA,
  fixSha: 'ffff111bbbb2222cccc3333dddd4444eeee5555f', file: 'src/Totals.cs', line: 12, why: '', fix: '', comment: '', sentUtc: '', commentLost: '',
});

/** A panel whose every hook is recorded, and whose posts are collected in order. */
const panelWith = (read: TreeRead, opening: () => Promise<void> = async () => { /* opens */ }): {
  panel: RevisionPanel; posts: RevisionItem[][]; opened: string[];
} => {
  const posts: RevisionItem[][] = [];
  const opened: string[] = [];
  const hooks: RevisionReach = {
    readFileAt: async () => ({ ok: false, tooOld: false, why: 'not this test' }),
    showRevision: async () => { /* not this test */ },
    showCurrent: async () => { /* not this test */ },
    folders: () => [REPO],
    readTreeAt: async () => read,
    openFolder: async (path) => { opened.push(path); await opening(); },
  };

  return { panel: new RevisionPanel(hooks, (items) => posts.push([...items])), posts, opened };
};

const saidOn = (posts: RevisionItem[][], at: number): string =>
  (posts[at] ?? []).map((item) => item.html).join(' ');

test('a press says it is working BEFORE the server is asked, and replaces that when it ends', async () => {
  const { panel, posts, opened } = panelWith({ ok: true, tree: answer() });

  await panel.openTree(pair(), [pair()]);

  assert.equal(posts.length, 2, 'one post for the press, one for its ending');
  // CLAUDE.md §8: a checkout runs for a minute, which is exactly long enough for a person to
  // conclude nothing happened.
  assert.match(saidOn(posts, 0), /checking the commit out/u);
  assert.doesNotMatch(saidOn(posts, 0), /data-open-tree/u, 'and the button is gone while it runs');
  assert.match(saidOn(posts, 1), /data-open-tree/u, 'which comes back when it ends');
  assert.deepEqual(opened, [TREE], 'an answer that carries a tree opens it');
});

test('a refusal opens nothing and says why, and the row keeps saying it on a redraw', async () => {
  const { panel, posts, opened } = panelWith({
    ok: true,
    tree: answer({ reason: 'budget', path: '', trees: [{ repository: 'D:/a/.git', sha: 'bbbb222', path: 'C:/t/one', created: '2026-09-01' }] }),
  });

  await panel.openTree(pair(), [pair()]);

  assert.deepEqual(opened, [], 'nothing was made, so nothing may be opened');
  assert.match(saidOn(posts, 1), /C:\/t\/one/u, 'the refusal names what is using up the cap');

  // A person deciding another row must be able to read it again afterwards.
  const again = panel.stateFor([pair()]);
  assert.ok(again.size === 1);
});

test('a failed read is said on the row rather than swallowed', async () => {
  const { panel, posts } = panelWith({ ok: false, tooOld: true, why: 'this server is too old' });

  await panel.openTree(pair(), [pair()]);

  assert.match(saidOn(posts, 1), /too old/u);
});

test('an editor that refuses the folder is caught, and the row says so instead of the press vanishing', async () => {
  const { panel, posts } = panelWith(
    { ok: true, tree: answer() },
    async () => { throw new Error('the window would not open'); },
  );

  await panel.openTree(pair(), [pair()]);

  assert.match(saidOn(posts, 1), /would not open/u);
  assert.match(saidOn(posts, 1), /data-open-tree/u, 'and it can be pressed again');
});

test('a second press while one is out starts nothing', async () => {
  let opens = 0;
  const { panel } = panelWith({ ok: true, tree: answer() }, async () => { opens += 1; });

  const first = panel.openTree(pair(), [pair()]);
  await panel.openTree(pair(), [pair()]);
  await first;

  assert.equal(opens, 1, 'two presses on one row must not check the commit out twice');
});

test('a refusal is remembered for the row, so a redraw says it again', async () => {
  const { panel, posts } = panelWith({ ok: true, tree: answer({ reason: 'git_failed', path: '' }) });

  await panel.openTree(pair(), [pair()]);
  assert.match(saidOn(posts, 1), /git did not answer/u);

  // A second press, answered the same way, must still say it — the note is not consumed by being
  // shown. (That a CLOSED window drops it is `forget()`, pinned in `bugzReviewWiring.test.ts`:
  // there is no behavioural surface for it here, because a forgotten note is the ABSENCE of text on
  // a draw that only the panel's own webview performs.)
  await panel.openTree(pair(), [pair()]);
  assert.match(saidOn(posts, 3), /git did not answer/u);
});
