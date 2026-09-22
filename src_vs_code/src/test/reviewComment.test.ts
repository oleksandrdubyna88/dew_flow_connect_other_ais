import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';

import { COMMENT_MOST_CHARS, commentBlock, commentWrite, decisionsFor, settled } from '../reviewComment';
import { ReviewPair } from '../reviewPair';

/**
 * What the panel holds and writes for a person's words, and the one number the page shares with the
 * server (story 4.2 of `PLAN_a_comment_crosses_the_machine_boundary.md`).
 *
 * <p>These are the PURE halves; the page's behaviour — typing, pausing, leaving, a read-only box —
 * is proven by running the page in `bugzReviewPage.test.ts`.</p>
 */

const pair = (findingId: number, keep: number, comment: string): ReviewPair => ({
  findingId, symbolName: `method_${findingId}`, language: 'CSharp',
  skeletonBefore: 'method_1() { }', skeletonAfter: 'method_1() { lock { } }', keep,
  severity: 'Major', category: 'Reliability', title: 'a race', repoPath: 'D:/repo',
  headSha: 'aaaa111', fixSha: 'bbbb222', file: 'src/Totals.cs', line: 5, why: '', fix: '',
  comment, sentUtc: '', commentLost: '',
});

/**
 * The box's limit and the server's are ONE number, held against a file neither program owns.
 *
 * <p>The charset rule lives in `CommentRule` and is never re-implemented here; the LENGTH is the one
 * thing the page must know, because `maxlength` is what stops the thousand-and-first character from
 * being typed at all. Two copies of a number drift without a word, and the person caught between
 * them is told their comment is too long by a box that let them write it.</p>
 *
 * <p>So both halves assert their OWN number against `shared/comment-limit.json` — the server's suite
 * `CommentRule.MostChars`, this one the export AND the attribute the page actually renders. The first
 * draft read the number out of `CommentRule.cs` itself, and `NothingReadsAnotherProgramsSourceTests`
 * refused it: a test that parses another program's source goes quiet on a reformat instead of red.</p>
 */
test('the box stops where the server does: one number, held against the shared limit', () => {
  const shared = JSON.parse(
    readFileSync(join(__dirname, '..', '..', '..', 'shared', 'comment-limit.json'), 'utf8')) as { mostChars?: unknown };
  assert.equal(typeof shared.mostChars, 'number', 'shared/comment-limit.json no longer says what the limit is');

  assert.equal(COMMENT_MOST_CHARS, shared.mostChars);
  const rendered = /maxlength="(\d+)"/u.exec(commentBlock(pair(1, -1, ''), undefined));
  assert.equal(Number(rendered?.[1]), shared.mostChars, 'and the page renders the same number it exports');
});

test('a decision press writes each pair\'s draft when there is one, and its stored words when not', () => {
  const pairs = [pair(1, -1, 'stored one'), pair(2, -1, 'stored two')];

  assert.deepEqual(decisionsFor([1, 2, 99], 1, pairs, new Map([[2, 'typed two']])), [
    { findingId: 1, keep: 1, comment: 'stored one' },
    { findingId: 2, keep: 1, comment: 'typed two' },
    { findingId: 99, keep: 1, comment: '' },
  ], 'a keep press must never write an empty comment over a stored one');
});

test('a comment is written with the pair\'s CURRENT keep, and nothing for a pair the panel lacks', () => {
  const pairs = [pair(1, 0, '')];

  assert.deepEqual(commentWrite(1, 'dropped, but worth a word', pairs),
    [{ findingId: 1, keep: 0, comment: 'dropped, but worth a word' }]);
  assert.deepEqual(commentWrite(2, 'about nobody', pairs), [], 'a keep invented for it would be a decision nobody made');
});

test('a draft is let go only when exactly its words landed', () => {
  const drafts = new Map([[1, 'what was written'], [2, 'typed on since'], [3, 'not in this write']]);

  const left = settled(drafts, [
    { findingId: 1, keep: 1, comment: 'what was written' },
    { findingId: 2, keep: 1, comment: 'what was in the air' },
  ]);

  assert.deepEqual([...left], [[2, 'typed on since'], [3, 'not in this write']],
    'newer words typed during the write are still unsaved, and still the person\'s');
});
