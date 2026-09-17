import assert from 'node:assert/strict';
import { test } from 'node:test';

import { Answer, KeyRow, KeysPage, Revocation } from '../bugsAdminApi';
import {
  START,
  Trail,
  afterFailedIssue,
  afterRevoke,
  back,
  canGoBack,
  confirmRevoke,
  faceOf,
  forward,
  here,
} from '../bugsKeysFlow';

/**
 * The decisions the plan round argued about, asserted where they live.
 *
 * <p>Every one of these was a finding: that a 401 gets no diagnosis, that an unreachable server must
 * not read as a credential problem, that Back is a remembered cursor rather than a computed one,
 * that `changed: false` is not an error, and that a failed issuance is never retried.</p>
 */

const key = (over: Partial<KeyRow> = {}): KeyRow => ({
  id: 'aaaa1111',
  note: 'the tuesday workshop',
  createdUtc: '2026-09-17T10:00:00.0000000Z',
  lastSeenMonth: '2026-09',
  sent: 3,
  waiting: 1,
  ...over,
});


/** A key nobody has used: `lastSeenMonth` is ABSENT, exactly as the server sends it. */
const neverUsed = (): KeyRow => ({
  id: 'aaaa1111',
  note: 'the tuesday workshop',
  createdUtc: '2026-09-17T10:00:00.0000000Z',
  sent: 0,
  waiting: 0,
});

const page = (over: Partial<KeysPage> = {}): Answer<KeysPage> => ({
  kind: 'ok',
  value: { items: [key()], limit: 50, total: 1, ...over },
});

test('the newest page has nowhere to go back to', () => {
  assert.equal(here(START), '', 'the newest page is fetched with no cursor at all');
  assert.equal(canGoBack(START), false);
  assert.equal(back(START), START, 'back at the root must not fall off it');
});

/**
 * Back is the cursor we USED, not one we computed.
 *
 * <p>The server's token is opaque: there is no arithmetic that turns "the page after X" into "the
 * page before X", and composing one is a 400 by design. So the only honest Back is a memory.</p>
 */
test('back returns to the cursor that fetched the previous page', () => {
  const second = forward(START, 'cursor-one');
  const third = forward(second, 'cursor-two');

  assert.equal(here(third), 'cursor-two');
  assert.equal(here(back(third)), 'cursor-one');
  assert.equal(here(back(back(third))), '', 'and all the way to the newest page');
  assert.equal(canGoBack(back(back(third))), false);
});

test('next is offered only while the server says another page may exist', () => {
  const withMore = faceOf(page({ nextBefore: 'cursor-one' }), START, '');
  const atTheEnd = faceOf(page(), START, '');

  assert.equal(withMore.kind === 'keys' && withMore.hasNext, true);
  assert.equal(atTheEnd.kind === 'keys' && atTheEnd.hasNext, false, 'an absent cursor IS the end');
});

test('back is offered only once a page has been left behind', () => {
  const first = faceOf(page(), START, '');
  const second = faceOf(page(), forward(START, 'cursor-one'), '');

  assert.equal(first.kind === 'keys' && first.hasBack, false);
  assert.equal(second.kind === 'keys' && second.hasBack, true);
});

/** The finding two reviewers made independently: a 401 names no cause. */
test('a 401 becomes the one rejected face, carrying the server words and nothing else', () => {
  const face = faceOf({ kind: 'rejected', why: "an administrator's key is required" }, START, '');

  assert.equal(face.kind, 'rejected');
  assert.equal(face.kind === 'rejected' && face.why, "an administrator's key is required");
});

/** And the finding that an unreachable server must not be mistaken for a bad key. */
test('an unreachable server is its own face, not the rejected one', () => {
  const face = faceOf({ kind: 'unreachable', why: 'fetch failed' }, START, '');

  assert.equal(face.kind, 'unreachable');
});

test('a 429 keeps the seconds the server asked for', () => {
  const face = faceOf(
    { kind: 'limited', why: 'at most 120 requests a minute per administrator', retryAfterSeconds: 42 },
    START,
    '',
  );

  assert.equal(face.kind === 'limited' && face.retryAfterSeconds, 42);
});

/**
 * A revoke that changed nothing is not an error, and reports the ORIGINAL time.
 *
 * <p>Another administrator, another window, or the CLI on the host can have got there first. The
 * server answers 200 with `changed:false` and the time it actually stopped working — saying the
 * time of THIS attempt would be a fact the tab invented.</p>
 */
test('a revoke that changed nothing says so and names when it really happened', () => {
  const already: Answer<Revocation> = {
    kind: 'ok',
    value: { id: 'aaaa1111', revokedUtc: '2026-09-17T09:00:00.0000000Z', changed: false },
  };

  const said = afterRevoke(already, 'the tuesday workshop');

  assert.match(said, /already revoked/u);
  assert.ok(said.includes('2026-09-17T09:00:00.0000000Z'), 'the original time, not this attempt');
  assert.doesNotMatch(said, /not revoked/iu, 'it IS revoked; that is the point');
});

test('a revoke that changed something says so plainly', () => {
  const now: Answer<Revocation> = {
    kind: 'ok',
    value: { id: 'aaaa1111', revokedUtc: '2026-09-17T10:00:00.0000000Z', changed: true },
  };

  assert.match(afterRevoke(now, 'the tuesday workshop'), /was revoked\./u);
});

/** 404 is a stale row, which is a different sentence from a refusal. */
test('a revoke of a key the server does not have says the list was stale', () => {
  const gone: Answer<Revocation> = { kind: 'missing', why: 'no key aaaa1111 exists' };

  assert.match(afterRevoke(gone, 'the tuesday workshop'), /not on this server any more/u);
});

test('a refused revoke carries the server sentence', () => {
  const limited: Answer<Revocation> = { kind: 'limited', why: 'too many', retryAfterSeconds: 30 };

  assert.match(afterRevoke(limited, 'x'), /Not revoked: too many/u);
});

/**
 * The lost-issuance case, which is the one story 2 ordered the listing for.
 *
 * <p>No retry: the server commits before it answers, so a second attempt can leave two live keys.
 * The sentence has to send a person to the newest row rather than to the button they just pressed.</p>
 */
test('an issuance with no answer says a key MAY exist and where to look', () => {
  const said = afterFailedIssue({ kind: 'unreachable', why: 'fetch failed' });

  assert.match(said, /MAY have been created/u);
  assert.match(said, /newest row/u);
  assert.match(said, /revoke it if you do not hold it/u);
  assert.doesNotMatch(said, /try again|retry/iu, 'retrying is what creates the second key');
});

test('an issuance the server refused says so without inviting a retry', () => {
  const said = afterFailedIssue({ kind: 'refused', why: 'a note that looks like an email address is refused' });

  assert.match(said, /No key was issued/u);
  assert.ok(said.includes('email address'), "the server's own words");
});

/**
 * The confirmation names the note and the month, never the id.
 *
 * <p>The plan's reason: ids are hex and look alike, and this is a destructive action that takes
 * effect at once.</p>
 */
test('the revoke confirmation names the note and the last-use month', () => {
  const asked = confirmRevoke(key());

  assert.ok(asked.includes('the tuesday workshop'), 'the note is what a person recognises');
  assert.ok(asked.includes('2026-09'), 'and the month tells them whether it is in use');
  assert.doesNotMatch(asked, /aaaa1111/u, 'the id is what they cannot tell apart');
  assert.match(asked, /cannot be undone/u);
});

test('a key nobody has used is confirmed as never used, not as a blank', () => {
  assert.match(confirmRevoke(neverUsed()), /never used/u);
});

test('a key with no note is still nameable', () => {
  assert.match(confirmRevoke(key({ note: '   ' })), /key with no note/u);
});

/** A trail is a value: taking a step must not edit the one held by the caller. */
test('walking the trail leaves the previous one untouched', () => {
  const first: Trail = START;
  const second = forward(first, 'cursor-one');

  assert.deepEqual(first.used, [''], 'forward must not mutate what it was given');
  assert.deepEqual(second.used, ['', 'cursor-one']);
});

/**
 * A server that ANSWERED and refused is not a server that could not be reached.
 *
 * <p>Folding the two said the wrong thing twice: it blamed the connection for a reply, and it
 * offered a Try again that would repeat the same refused request. A stale cursor is the ordinary
 * way to arrive here, so the way out is back to the newest page. (Code round, codex.)</p>
 */
test('a 400 gets its own face and is not called unreachable', () => {
  const face = faceOf({ kind: 'refused', why: "before is 'nonsense'" }, START, '');

  assert.equal(face.kind, 'refused');
  assert.equal(face.kind === 'refused' && face.why, "before is 'nonsense'");
});

test('a 404 and a malformed answer are refusals too, because the server answered', () => {
  assert.equal(faceOf({ kind: 'missing', why: 'no such thing' }, START, '').kind, 'refused');
  assert.equal(faceOf({ kind: 'malformed', why: 'not a page' }, START, '').kind, 'refused');
});

test('an address that may not carry a key has a face that blames neither the key nor the server', () => {
  const face = faceOf({ kind: 'unsafe', why: 'it is not https' }, START, '');

  assert.equal(face.kind, 'unsafe');
});

/**
 * EVERY face carries `said`.
 *
 * <p>The sentence explaining what an action came to was rendered only by the listing, so a revoke
 * that failed and then a listing that also failed told the person nothing about the revoke. The
 * most valuable sentence this tab produces arrives exactly when the face is not the listing.</p>
 */
test('what the last action said survives whichever face the server earned', () => {
  const warned = 'a key MAY have been created';

  for (const answer of [
    { kind: 'rejected', why: 'w' } as const,
    { kind: 'unreachable', why: 'w' } as const,
    { kind: 'refused', why: 'w' } as const,
    { kind: 'unsafe', why: 'w' } as const,
    { kind: 'limited', why: 'w', retryAfterSeconds: 1 } as const,
  ]) {
    assert.equal(faceOf(answer, START, warned).said, warned, `${answer.kind} must carry it`);
  }

  assert.equal(faceOf(page(), START, warned).said, warned, 'and so must the listing');
});

/** An empty page reached by FOLLOWING a cursor is the end of a walk, not an empty server. */
test('the end of a walk is not the same as a server with no keys', () => {
  const walked = faceOf(page({ items: [] }), forward(START, 'cursor-one'), '', true);
  const firstPage = faceOf(page({ items: [] }), START, '', false);

  assert.equal(walked.kind === 'keys' && walked.pagedPastTheEnd, true);
  assert.equal(firstPage.kind === 'keys' && firstPage.pagedPastTheEnd, false);
});

test('a page that has rows is never the end of a walk, however it was reached', () => {
  const face = faceOf(page(), forward(START, 'cursor-one'), '', true);

  assert.equal(face.kind === 'keys' && face.pagedPastTheEnd, false);
});

/** An unsafe address means nothing was sent, so there is nothing to go looking for. */
test('an issuance to an unsafe address says nothing was sent', () => {
  const said = afterFailedIssue({ kind: 'unsafe', why: 'it is not https' });

  assert.match(said, /nothing was sent/u);
  assert.doesNotMatch(said, /MAY have been created/u, 'it cannot have been: it never left');
});

/** A malformed answer means the server DID act, so it gets the same warning as a timeout. */
test('an issuance whose answer could not be read warns that a key may exist', () => {
  const said = afterFailedIssue({ kind: 'malformed', why: 'not an issued key' });

  assert.match(said, /MAY have been\s+created/u);
  assert.match(said, /newest row/u);
});
