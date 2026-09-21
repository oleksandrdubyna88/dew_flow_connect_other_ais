import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

import {
  affectedBy,
  currentFileIn,
  emptyMemory,
  FileAtRead,
  heldRevision,
  remember,
  rememberCurrent,
  revisionDocumentPath,
  RevisionMemory,
  stateOf,
  TOO_OLD_FOR_THE_REVISION,
} from '../openAtRevision';
import { revisionActions, RevisionRow, UNPROBED } from '../revisionActions';
import { readable } from './readableHtml';

/**
 * Reaching the code, honestly about which revision — the host's decisions, as values.
 *
 * <p>Two things the panel does that no page test can see: what it REMEMBERS per repository so that
 * one process answers for every row of a checkout that is gone, and how it GUARDS the one action
 * that touches the live filesystem. Both are pure here; `bugzReviewWiring.test.ts` pins that the
 * panel calls them, and `bugzReviewPage.test.ts` that the page renders what they decide.</p>
 */

const row = (findingId: number, repoPath = 'D:/repo', file = 'src/Totals.cs'): RevisionRow =>
  ({ findingId, repoPath, headSha: 'aaaa111bbbb2222cccc3333dddd4444eeee5555f', file });

const answered = (one: RevisionRow, reason: string, text = ''): FileAtRead =>
  ({ ok: true, file: { findingId: one.findingId, sha: one.headSha, path: one.file, reason, text } });

const text = (one: RevisionRow): FileAtRead => answered(one, '', 'public class Totals { }');

const said = (memory: RevisionMemory, one: RevisionRow): string => readable(revisionActions(one, stateOf(memory, one)));

// --------------------------------------------------------------------------------------------
// The memory: probe once per repository, and remember.
// --------------------------------------------------------------------------------------------

test('before any press, every row offers the action and says it will check first', () => {
  const state = stateOf(emptyMemory(), row(1));

  assert.equal(state.offered, true);
  assert.deepEqual(state, UNPROBED, 'nothing is known, so the row says so rather than promising');
  assert.match(said(emptyMemory(), row(1)), /Open at aaaa111/u);
  assert.match(said(emptyMemory(), row(1)), /not checked yet/u);
});

test('a checkout that is gone answers once for every row of that repository, and the other repository is untouched', () => {
  const gone = remember(emptyMemory(), row(1, 'D:/gone'), answered(row(1, 'D:/gone'), 'repo_path_missing'));

  for (const sibling of [row(1, 'D:/gone'), row(2, 'D:/gone'), row(3, 'D:/gone')]) {
    const state = stateOf(gone, sibling);
    assert.equal(state.offered, false, `row ${sibling.findingId} of a gone checkout must not offer a press that can only fail`);
    assert.match(state.note, /D:\/gone is not a git repository any more/u);
    assert.doesNotMatch(said(gone, sibling), /Open at/u, 'the button is gone, the sentence is there');
  }
  assert.deepEqual(stateOf(gone, row(9, 'D:/other')), UNPROBED, 'another repository knows nothing yet');
  assert.deepEqual(affectedBy(gone, row(1, 'D:/gone'), [row(1, 'D:/gone'), row(2, 'D:/gone'), row(9, 'D:/other')]), [1, 2],
    'the rows told are every row of that repository and no other');
});

test('a successful read turns every sibling row from "will check" into a plain offer, and is held for its own row', () => {
  const learned = remember(emptyMemory(), row(1), text(row(1)));

  assert.deepEqual(stateOf(learned, row(2)), { offered: true, note: '', currentNote: '' },
    'the repository answered, so a sibling no longer warns that it will check');
  assert.equal(stateOf(learned, row(1)).offered, true);
  assert.equal(heldRevision(learned, row(1))?.ok, true, 'a second press on the same row costs no process');
  assert.equal(heldRevision(learned, row(2)), undefined, 'but a sibling row still has to be read');
  assert.deepEqual(affectedBy(learned, row(1), [row(1), row(2), row(3, 'D:/other')]), [1, 2]);
});

test('a commit that is gone is a fact about that row alone', () => {
  const pruned = remember(emptyMemory(), row(1), answered(row(1), 'commit_unreachable'));

  const state = stateOf(pruned, row(1));
  assert.equal(state.offered, false);
  assert.match(state.note, /commit aaaa111 is not in the repository any more/u);
  assert.equal(stateOf(pruned, row(2)).offered, true, 'the sibling row may well read — 99.6 % of orphaned blobs do');
  assert.equal(heldRevision(pruned, row(1)), undefined, 'a reason is not text to reopen');
});

test('a file that was not at that path says which path, and points at the current file', () => {
  const moved = remember(emptyMemory(), row(1), answered(row(1), 'file_not_in_commit'));

  const state = stateOf(moved, row(1));
  assert.equal(state.offered, false);
  assert.match(state.note, /src\/Totals\.cs was not at this path at aaaa111/u);
  assert.match(state.note, /open the current file instead/u);
  assert.match(said(moved, row(1)), /Open CURRENT/u, 'the current file is still offered — it is the way forward');
});

test('a path the server refused, and a pair it no longer has, each say so', () => {
  const refused = remember(emptyMemory(), row(1, 'D:/repo', '../.ssh/config'), answered(row(1, 'D:/repo', '../.ssh/config'), 'path_refused'));
  assert.match(stateOf(refused, row(1, 'D:/repo', '../.ssh/config')).note, /not a repository-relative path/u);
  assert.equal(stateOf(refused, row(1, 'D:/repo', '../.ssh/config')).offered, false);

  const lost = remember(emptyMemory(), row(1), answered(row(1), 'pair_not_found'));
  assert.match(stateOf(lost, row(1)).note, /not in the database any more/u);
  assert.deepEqual(stateOf(lost, row(2)), UNPROBED, 'a pair that is gone says nothing about the repository');
});

test('git not answering keeps the action offered and asks for another press; so does a failed process', () => {
  const quiet = remember(emptyMemory(), row(1), answered(row(1), 'git_failed'));
  assert.equal(stateOf(quiet, row(1)).offered, true);
  assert.match(stateOf(quiet, row(1)).note, /git did not answer for D:\/repo; press again/u);
  assert.equal(heldRevision(quiet, row(1)), undefined, 'the next press must reach the server again');
  assert.deepEqual(affectedBy(quiet, row(1), [row(1), row(2)]), [1], 'nothing was learned about the repository');

  const failed = remember(emptyMemory(), row(1), { ok: false, tooOld: false, why: 'the server exited 74' });
  assert.equal(stateOf(failed, row(1)).offered, true);
  assert.match(stateOf(failed, row(1)).note, /could not be read: the server exited 74; press again/u);
  assert.equal(heldRevision(failed, row(1)), undefined);
});

test('a server too old for the mode is a fact about every row, spelled once', () => {
  const old = remember(emptyMemory(), row(1), { ok: false, tooOld: true, why: TOO_OLD_FOR_THE_REVISION });

  for (const any of [row(1), row(2, 'D:/other')]) {
    assert.equal(stateOf(old, any).offered, false, 'pressing again would spawn the same 64');
    assert.equal(stateOf(old, any).note, TOO_OLD_FOR_THE_REVISION);
  }
});

test('what was held for a row is a miss once the row names another commit or path', () => {
  const learned = remember(emptyMemory(), row(1), text(row(1)));

  assert.equal(heldRevision(learned, { ...row(1), headSha: 'ffff000ffff000ffff000ffff000ffff000ffff0' }), undefined,
    'a recollection under the page changes the commit, and last week\'s file must not open under it');
  assert.equal(heldRevision(learned, { ...row(1), file: 'src/Other.cs' }), undefined);
  assert.equal(stateOf(learned, { ...row(1), file: 'src/Other.cs' }).note, '', 'the repository is still known to answer');
});

test('why the current file could not be opened is remembered per row, and cleared by an empty why', () => {
  const refused = rememberCurrent(emptyMemory(), row(1), 'D:/repo is not a folder of this workspace');

  assert.equal(stateOf(refused, row(1)).currentNote, 'D:/repo is not a folder of this workspace');
  assert.match(said(refused, row(1)), /not a folder of this workspace/u);
  assert.equal(stateOf(refused, row(2)).currentNote, '');
  assert.equal(stateOf(rememberCurrent(refused, row(1), ''), row(1)).currentNote, '');
});

test('the memory is a value: remembering answers a new one and leaves the old untouched', () => {
  const before = emptyMemory();
  const after = remember(before, row(1), text(row(1)));

  assert.equal(before.rows.size, 0);
  assert.equal(after.rows.size, 1);
  assert.notEqual(before, after);
});

// --------------------------------------------------------------------------------------------
// The rendered actions, and the name a revision document is given.
// --------------------------------------------------------------------------------------------

test('a row with no file recorded offers no FILE action, but still offers the whole-repository checkout', () => {
  const noFile = revisionActions({ ...row(1), file: '' }, UNPROBED);

  assert.match(readable(noFile), /nothing to open/u);
  assert.doesNotMatch(noFile, /data-open-at|data-open-current/u,
    'neither file action has a path to open');
  // The checkout takes the repository and the commit, which this row HAS. Returning before it was
  // rendered suppressed a working action on every finding that recorded no path. (Code round, codex.)
  assert.match(noFile, /data-open-tree="1"/u,
    'the whole-repository checkout never needed a file');

  const noCheckout = revisionActions({ ...row(1), repoPath: '' }, UNPROBED);
  assert.match(noCheckout, /data-open-at="1"/u);
  assert.doesNotMatch(noCheckout, /data-open-current/u, 'a current file needs a checkout to be found in');
  assert.match(readable(noCheckout), /no checkout recorded/u);

  const noCommit = revisionActions({ ...row(1), headSha: '' }, UNPROBED);
  assert.doesNotMatch(noCommit, /data-open-at/u);
  assert.match(readable(noCommit), /an unrecorded commit/u);
});

test('a note or a path full of markup renders as text', () => {
  const hostile = { ...row(1), file: '<img src=x onerror=alert(1)>.cs', repoPath: '<b>D:/repo</b>' };
  const html = revisionActions(hostile, { offered: false, note: '</script><style>.pair{display:none}</style>', currentNote: '' });

  assert.ok(!html.includes('</script>'), 'a note could end the page\'s own script early');
  assert.ok(!/<style[\s>]/iu.test(html), 'a note could hide the rows around it');
  assert.ok(!/<img[\s>]/iu.test(html), 'a path could add an element');
  assert.ok(readable(html).includes('<style>.pair{display:none}</style>'), 'and the text is still all there');
});

test('a revision document is named by its file with the short sha, keeps its extension, and carries the full coordinates', () => {
  const at = revisionDocumentPath('aaaa111bbbb2222cccc3333dddd4444eeee5555f', 'src/Totals.cs');

  assert.equal(at.path, '/src/Totals@aaaa111.cs', 'the tab reads Totals@aaaa111.cs, and .cs still picks the language');
  assert.equal(at.query, 'sha=aaaa111bbbb2222cccc3333dddd4444eeee5555f&path=src%2FTotals.cs');
  assert.equal(revisionDocumentPath('aaaa111bbbb2222cccc3333dddd4444eeee5555f', 'Makefile').path, '/Makefile@aaaa111');
  assert.equal(revisionDocumentPath('aaaa111bbbb2222cccc3333dddd4444eeee5555f', 'a/b/c.test.ts').path, '/a/b/c.test@aaaa111.ts');
});

// --------------------------------------------------------------------------------------------
// The guard on the one action that touches the live filesystem — over real directories.
// --------------------------------------------------------------------------------------------

/** A workspace folder holding a checkout holding a file, and a checkout OUTSIDE the workspace beside it. */
function scene(): { readonly folder: string; readonly repo: string; readonly elsewhere: string; readonly done: () => void } {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-current-'));
  const folder = path.join(root, 'workspace');
  const repo = path.join(folder, 'repo');
  const elsewhere = path.join(root, 'elsewhere');
  fs.mkdirSync(path.join(repo, 'src'), { recursive: true });
  fs.mkdirSync(path.join(elsewhere, 'src'), { recursive: true });
  fs.writeFileSync(path.join(repo, 'src', 'Totals.cs'), 'class Totals { }', 'utf8');
  fs.writeFileSync(path.join(elsewhere, 'src', 'Totals.cs'), 'class Elsewhere { }', 'utf8');

  return { folder, repo, elsewhere, done: () => fs.rmSync(root, { recursive: true, force: true }) };
}

test('a file inside a checkout inside a workspace folder is opened, canonically', async () => {
  const { folder, repo, done } = scene();
  try {
    const found = await currentFileIn([folder], repo, 'src/Totals.cs');

    assert.equal(found.ok, true, found.ok ? '' : found.why);
    assert.equal(path.basename(found.ok ? found.path : ''), 'Totals.cs');
  } finally {
    done();
  }
});

test('a checkout that IS the workspace folder is the ordinary case, and is allowed', async () => {
  const { repo, done } = scene();
  try {
    const found = await currentFileIn([repo], repo, 'src/Totals.cs');

    assert.equal(found.ok, true, found.ok ? '' : found.why);
  } finally {
    done();
  }
});

test('a checkout outside every workspace folder is refused before anything is opened, naming why', async () => {
  const { folder, elsewhere, done } = scene();
  try {
    const found = await currentFileIn([folder], elsewhere, 'src/Totals.cs');

    assert.equal(found.ok, false, 'a pair naming /etc would otherwise open a system file');
    assert.match(found.ok ? '' : found.why, /is not a folder of this workspace/u);
  } finally {
    done();
  }
});

/**
 * A file that leads outside its checkout is refused, even from a checkout the workspace holds.
 *
 * <p>The escaping path points at a file that EXISTS — `elsewhere` sits beside the workspace, two
 * levels up from the checkout — so the only thing that can refuse it is containment. The first
 * version pointed one level up, at a path that did not exist, and passed through the "not in the
 * checkout any more" branch with the containment guard deleted: a fixture the code rejects for
 * another reason proves nothing, and the break-it check is what found it.</p>
 */
test('a file that leads outside its checkout is refused, even from a checkout the workspace holds', async () => {
  const { folder, repo, elsewhere, done } = scene();
  try {
    assert.ok(fs.existsSync(path.join(repo, '..', '..', 'elsewhere', 'src', 'Totals.cs')), 'the fixture must escape to a file that IS there');
    assert.equal(path.resolve(repo, '..', '..', 'elsewhere'), elsewhere);

    const found = await currentFileIn([folder], repo, '../../elsewhere/src/Totals.cs');

    assert.equal(found.ok, false);
    assert.match(found.ok ? '' : found.why, /leads outside/u, 'refused by containment, not by absence');
  } finally {
    done();
  }
});

test('a file that is not in the checkout any more says so, and sends the person to the revision', async () => {
  const { folder, repo, done } = scene();
  try {
    const found = await currentFileIn([folder], repo, 'src/Gone.cs');

    assert.equal(found.ok, false);
    assert.match(found.ok ? '' : found.why, /src\/Gone\.cs is not in .* any more; open it at its revision instead/u);
  } finally {
    done();
  }
});

test('no checkout and no file are each said, and no folder at all refuses everything', async () => {
  const { repo, done } = scene();
  try {
    assert.match((await currentFileIn([repo], '', 'src/Totals.cs') as { why: string }).why, /no checkout is recorded/u);
    assert.match((await currentFileIn([repo], repo, '') as { why: string }).why, /no file is recorded/u);
    assert.equal((await currentFileIn([], repo, 'src/Totals.cs')).ok, false, 'an empty workspace holds no checkout');
  } finally {
    done();
  }
});


test('a held revision belongs to its CHECKOUT as well as its row and commit', () => {
  // Code round, codex. The identity was findingId + headSha + file. Two checkouts of one repository
  // — which story 2.2 measured as ordinary here, 44 of 54 live paths were linked worktrees — give
  // the same three for different code, so the second row would have been handed the first's text.
  const inA = { findingId: 7, repoPath: 'd:/rsd/a', headSha: 'aaaa111', file: 'src/Totals.cs' };
  const inB = { ...inA, repoPath: 'd:/rsd/b' };
  const text = { ok: true as const, tooOld: false, file: { findingId: 7, sha: 'aaaa111', path: 'src/Totals.cs', reason: '', text: 'A' } };

  const memory = remember(emptyMemory(), inA, text);

  assert.equal(heldRevision(memory, inA)?.ok, true, 'the checkout it was read from still answers');
  assert.equal(heldRevision(memory, inB), undefined, 'another checkout must not be handed this text');
});

test('a server too old is told to every row on the page, not only the one that pressed', () => {
  // Code round, codex. `remember` sets `tooOld` globally but does not mark the repository probed, so
  // `affectedBy` answered with the pressed row alone: 199 of 200 rows kept offering the action and
  // each launched another doomed request.
  const rows = [
    { findingId: 1, repoPath: 'd:/rsd/a', headSha: 'aaa', file: 'x.cs' },
    { findingId: 2, repoPath: 'd:/rsd/a', headSha: 'bbb', file: 'y.cs' },
    { findingId: 3, repoPath: 'd:/rsd/b', headSha: 'ccc', file: 'z.cs' },
  ];
  const old = { ok: false as const, tooOld: true, why: 'this server does not have the mode' };

  const memory = remember(emptyMemory(), rows[0]!, old);

  assert.deepEqual([...affectedBy(memory, rows[0]!, rows)].sort(), [1, 2, 3],
    'an old binary is a fact about the SERVER, so every drawn row learns it at once');
});
