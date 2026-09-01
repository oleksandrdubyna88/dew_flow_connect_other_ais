import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { test } from 'node:test';
import {
  claudeSnippet,
  SNIPPET_BODY_SHA,
  SNIPPET_VERSION,
  snippetNote,
  snippetStatus,
  snippetVersionIn,
} from '../claudeSnippet';

/**
 * A pasted copy can be recognised as old.
 *
 * <p>Found in the wild, not imagined: the copy in `dew_flow_creds_for_devs/CLAUDE.md` was two
 * revisions behind the button's text and predated the SCOPE rule, so the AI obeying it would call
 * `review_code` with a commit subject and meet a refusal that nothing in its instructions
 * explained. Nobody was careless — that is what happens to text somebody pastes. The source moves,
 * the copy does not, and the copy is the one being obeyed.</p>
 */

test('the snippet carries a version a machine can read', () => {
  assert.equal(snippetVersionIn(claudeSnippet()), SNIPPET_VERSION);
});

test('a pasted file is recognised wherever the snippet sits inside it', () => {
  // It goes into a CLAUDE.md that is mostly other things, usually somewhere in the middle.
  const file = `# Project rules\n\nSomething else entirely.\n\n${claudeSnippet()}\n\n## After it\n`;

  assert.equal(snippetVersionIn(file), SNIPPET_VERSION);
});

test('an older paste is reported as older, with both numbers', () => {
  const old = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1');

  assert.deepEqual(snippetStatus(old), { kind: 'older', found: 1, current: SNIPPET_VERSION });
});

test('a copy pasted before versioning existed is not version zero', () => {
  // Everything pasted until today has no marker. "Predates versioning" is the true statement;
  // calling it 0 would invent a number nobody wrote.
  const before = '## Multi-model review gate (ConnectOtherAIs)\n\nThis repository is reviewed by…';

  assert.deepEqual(snippetStatus(before), { kind: 'unversioned', current: SNIPPET_VERSION });
});

test('no instruction file at all is absent, not stale', () => {
  // A repository that has deliberately not adopted the gate is not a problem to report.
  assert.deepEqual(snippetStatus(undefined), { kind: 'absent', current: SNIPPET_VERSION });
  assert.deepEqual(snippetStatus('# Just a readme\n'), { kind: 'absent', current: SNIPPET_VERSION });
});

test('the current version is current', () => {
  assert.deepEqual(snippetStatus(claudeSnippet()), { kind: 'current', current: SNIPPET_VERSION });
});

test('a copy from the FUTURE is not called old', () => {
  // An extension older than the pasted snippet — somebody updated the repo before this machine.
  const ahead = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, `coai-snippet v${SNIPPET_VERSION + 5}`);

  assert.deepEqual(snippetStatus(ahead), { kind: 'ahead', found: SNIPPET_VERSION + 5, current: SNIPPET_VERSION });
});

/**
 * The guard that makes the number worth having.
 *
 * <p>A version somebody must remember to bump is the same failure one level up: the snippet moves,
 * the number does not, and every pasted copy reports itself current forever. So the number is
 * pinned to the text. Editing the snippet fails this test until both are changed together, which is
 * the only moment either is cheap.</p>
 */
test('the snippet text and its version number move together', () => {
  const body = claudeSnippet().replace(/<!-- coai-snippet v\d+ -->\n?/, '');
  const sha = createHash('sha256').update(body).digest('hex').slice(0, 16);

  assert.equal(
    sha,
    SNIPPET_BODY_SHA,
    `The snippet text changed. Raise SNIPPET_VERSION to ${SNIPPET_VERSION + 1} and set `
      + `SNIPPET_BODY_SHA to '${sha}'. Both, together — a version that does not move with the text `
      + 'tells every pasted copy it is current forever, which is the defect this exists to catch.',
  );
});

test('the panel says nothing when the paste is current or absent', () => {
  // Two silences with different reasons, and both are correct: a workspace that never adopted the
  // gate is entitled not to, and one that is current has nothing to be told.
  assert.equal(snippetNote({ kind: 'current', current: SNIPPET_VERSION }), '');
  assert.equal(snippetNote({ kind: 'absent', current: SNIPPET_VERSION }), '');
});

test('a stale paste is told what to do, with both numbers', () => {
  const note = snippetNote({ kind: 'older', found: 1, current: 4 });

  assert.match(note, /v1/);
  assert.match(note, /v4/);
  assert.match(note, /Copy it again/);
});

test('an unversioned paste is told it is behind without inventing a number', () => {
  const note = snippetNote({ kind: 'unversioned', current: 4 });

  assert.match(note, /predates versioning/);
  assert.doesNotMatch(note, /v0/, 'a version nobody ever wrote');
});

test('a paste from the future says to update the extension, not to overwrite the repo', () => {
  const note = snippetNote({ kind: 'ahead', found: 9, current: 4 });

  assert.match(note, /update this one rather than pasting over it/);
});
