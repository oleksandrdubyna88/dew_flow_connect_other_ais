import assert from 'node:assert/strict';
import { test } from 'node:test';
import { followReport } from '../followReport';

/**
 * A rename that went wrong says so — and one that went right says nothing.
 *
 * <p><b>Two stories of the tail plan point here, and measuring collapsed them into one.</b> Story 6
 * is an unguarded `index.refresh()`: if it throws after records have been refiled, the picker keeps
 * offering names that have moved, and the outer catch only logs. Story 7 said a permission failure
 * was retried five times and reported exactly as a lock is.</p>
 *
 * <p><b>Story 7's premise was wrong, and the code says so.</b> `chatStoreLock` returns `false` only
 * on `EEXIST` and throws for anything else, so a permission error arrives as `failed` and `follow`
 * gives up at once — the classification that story wanted to add already exists, one layer down.
 * What neither outcome had was anybody being TOLD. So the two are one change.</p>
 *
 * <p>The decision is a value because the alternative is a notification per conversation, and a folder
 * refactor moves many at once.</p>
 */

test('a rename that worked interrupts nobody', () => {
  // The case that keeps this from becoming noise. It is also the overwhelmingly common one: a rename
  // that followed is a rename nobody needs to be told about.
  assert.equal(followReport({ moved: 12, couldNot: 0, refreshFailed: false }), undefined,
    'a rename that worked perfectly told the person about it anyway');
});

test('nothing at all to report is still nothing', () => {
  assert.equal(followReport({ moved: 0, couldNot: 0, refreshFailed: false }), undefined);
});

test('a stale list is the loudest outcome, because the data is right and the screen is wrong', () => {
  const said = followReport({ moved: 7, couldNot: 0, refreshFailed: true });

  assert.ok(said !== undefined, 'the picker is showing names that have moved and nothing says so');
  assert.equal(said.severity, 'warning');
  assert.match(said.title, /7 conversations/u, 'the person is not told how much moved under them');
  assert.match(said.title, /out of date/u, 'the sentence does not say the list itself is the problem');
  assert.match(said.title, /Reopen/u, 'the person is told something is wrong and not what to do about it');
});

test('a failed refresh outranks conversations that could not follow', () => {
  // Both at once is possible, and only one sentence may be said. The stale LIST wins: a conversation
  // that did not follow is still findable under its old name, while a list nobody can trust makes
  // every row on it suspect.
  const said = followReport({ moved: 3, couldNot: 2, refreshFailed: true });

  assert.ok(said !== undefined);
  assert.match(said.title, /out of date/u, 'the stale list was not the thing reported');
});

test('a conversation that could not follow is not reported as lost, because it is not', () => {
  const said = followReport({ moved: 4, couldNot: 1, refreshFailed: false });

  assert.ok(said !== undefined, 'a conversation that could not follow its file was never mentioned');
  assert.match(said.title, /1 conversation\b/u, 'the singular reads as a plural');
  assert.match(said.title, /still in the conversation list/u,
    'the sentence implies something was destroyed; nothing was — it keeps the path it had');
});

test('the count reads as English on both sides of one', () => {
  // Small, and it is the kind of thing that ships: "1 conversations could not be moved".
  const one = followReport({ moved: 0, couldNot: 1, refreshFailed: false });
  const many = followReport({ moved: 0, couldNot: 3, refreshFailed: false });

  assert.match(one?.title ?? '', /^1 conversation /u);
  assert.match(many?.title ?? '', /^3 conversations /u);
});
