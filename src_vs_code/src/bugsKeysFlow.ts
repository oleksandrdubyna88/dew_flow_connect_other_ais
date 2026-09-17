import { Answer, KeyRow, KeysPage, Revocation } from './bugsAdminApi';
import { View } from './bugsKeysPage';

/**
 * What the Users tab DOES, with no webview and no VS Code in sight.
 *
 * <p>Separated from the panel for the reason every seam here is: a panel is a thing you can only
 * drive from an extension host, and every decision the plan round found worth arguing about — which
 * face a 401 gets, what Back means when the cursor is opaque, what a revoke that changed nothing
 * says — is a decision that has nothing to do with a webview. Keeping them here is what lets a test
 * assert on them at all.</p>
 */

/**
 * The cursors already used, newest last.
 *
 * <p><b>This is the whole of "Back".</b> The server's `nextBefore` is opaque and forward-only, so a
 * previous page cannot be computed, asked for, or composed — the plan round found this three times
 * over. What CAN be done is remember: each page pushes the cursor that fetched it, Back pops, and
 * at the root Back is disabled because the root is the newest page and there is nothing newer.</p>
 */
export interface Trail {
  /** The cursor that fetched each page so far. The first is empty — the newest page. */
  readonly used: readonly string[];
}

export const START: Trail = { used: [''] };

/** Where we are now. */
export function here(trail: Trail): string {
  return trail.used[trail.used.length - 1] ?? '';
}

/** One page forward, onto a cursor a page handed us. */
export function forward(trail: Trail, next: string): Trail {
  return { used: [...trail.used, next] };
}

/** One page back, never past the root. */
export function back(trail: Trail): Trail {
  return trail.used.length > 1 ? { used: trail.used.slice(0, -1) } : trail;
}

/** Whether Back would do anything. */
export function canGoBack(trail: Trail): boolean {
  return trail.used.length > 1;
}

/**
 * The face a listing answer gets.
 *
 * <p>Every case the plan round added is here, and the two that matter most are the ones that do NOT
 * mention a key: an unreachable server is not a credential problem, and a 401 names no cause because
 * the server refuses to distinguish three of them.</p>
 */
export function faceOf(
  answer: Answer<KeysPage>,
  trail: Trail,
  said: string,
  followedACursor = false,
): View {
  if (answer.kind === 'ok') {
    return {
      kind: 'keys',
      rows: answer.value.items,
      total: answer.value.total,
      hasNext: (answer.value.nextBefore ?? '').length > 0,
      hasBack: canGoBack(trail),
      said,
      // An empty page reached by FOLLOWING a cursor is the end of the walk, which the server's
      // contract says always happens once. An empty first page is a server with no keys. Saying
      // the second about the first tells an administrator who just paged through fifty of them
      // that none exist. (Code round, gemini.)
      pagedPastTheEnd: followedACursor && answer.value.items.length === 0,
    };
  }

  if (answer.kind === 'rejected') {
    return { kind: 'rejected', why: answer.why, said };
  }

  if (answer.kind === 'limited') {
    return {
      kind: 'limited',
      why: answer.why,
      retryAfterSeconds: answer.retryAfterSeconds,
      said,
    };
  }

  if (answer.kind === 'unsafe') {
    return { kind: 'unsafe', why: answer.why, said };
  }

  // A server that ANSWERED and refused is not a server that could not be reached, and the code
  // round was right that folding them said the wrong thing twice: it blamed the connection for a
  // reply, and it offered a Try again that would repeat the same refused request. A malformed body
  // belongs here too — the server answered, and what came back was not the contract.
  if (answer.kind === 'refused' || answer.kind === 'missing' || answer.kind === 'malformed') {
    return { kind: 'refused', why: answer.why, said };
  }

  return { kind: 'unreachable', why: answer.why, said };
}

/**
 * What to say after a revoke.
 *
 * <p>The three answers are three different facts and the plan round found the first draft treating
 * them as one. `changed: false` is NOT an error — somebody else got there first — and the time it
 * carries is the ORIGINAL revocation, so reporting the moment of this attempt would be wrong about
 * when the key actually stopped working.</p>
 */
export function afterRevoke(answer: Answer<Revocation>, note: string): string {
  if (answer.kind === 'ok') {
    return answer.value.changed
      ? `${describe(note)} was revoked.`
      : `${describe(note)} was already revoked, at ${answer.value.revokedUtc}. Nothing changed.`;
  }

  if (answer.kind === 'missing') {
    return `${describe(note)} is not on this server any more; the list has been refreshed.`;
  }

  if (answer.kind === 'limited') {
    return `Not revoked: ${answer.why}`;
  }

  return `Not revoked: ${answer.why}`;
}

/**
 * What to say when an issuance did not come back.
 *
 * <p><b>Never a retry.</b> The server commits the key before it answers, so a request that failed
 * without a reply may well have created one — and a second attempt would leave two live keys, one
 * of which nobody has. The recovery is the listing, newest first, which is exactly what story 2
 * ordered it that way for.</p>
 */
export function afterFailedIssue(answer: Answer<unknown>): string {
  if (answer.kind === 'ok') {
    // Unreachable by contract — the caller only asks when the issuance did NOT come back — and said
    // rather than assumed, because a silent empty string here would read as "nothing happened".
    throw new Error('afterFailedIssue was given a successful issuance');
  }

  if (answer.kind === 'unsafe') {
    // Nothing left this machine, so there is nothing to look for and nothing to undo.
    return `No key was issued, and nothing was sent: ${answer.why}`;
  }

  if (answer.kind === 'refused' || answer.kind === 'rejected' || answer.kind === 'limited') {
    return `No key was issued: ${answer.why}`;
  }

  if (answer.kind === 'malformed') {
    // The server ANSWERED, so it may well have created the key; what came back was simply not
    // readable as one. That is the same danger as a timeout and gets the same warning.
    return 'The server answered with something this could not read, so a key MAY have been '
      + `created. It would be the newest row below — check it, and revoke it if you do not hold `
      + `it. (${answer.why})`;
  }

  return 'The server did not answer, so a key MAY have been created. It would be the newest row '
    + `below — check it, and revoke it if you do not hold it. (${answer.why})`;
}

/** How a key is named to a person: ids are hex and look alike, so the note leads. */
export function describe(note: string): string {
  return note.trim().length > 0 ? `The key "${note.trim()}"` : 'The key with no note';
}

/**
 * The sentence a DISCARD is confirmed with.
 *
 * <p>Discarding is one press away from Copy and it revokes — the same irreversible act the table's
 * Revoke button asks about first. Both halves of what is lost are said, because they are different
 * losses: the key stops working for anybody who already has it, and the copy on this machine is
 * gone whatever is answered. (Code round 2, gemini.)</p>
 */
export function confirmDiscard(note: string): string {
  return `Discard ${describe(note).toLowerCase()}? Discarding REVOKES it, which cannot be undone, `
    + 'and the key itself is gone from this machine either way — it cannot be read back, so anybody '
    + 'waiting for it will need a new one.';
}

/**
 * The sentence a revoke is confirmed with.
 *
 * <p>It names the note and the last-use month rather than the id, because the plan says so and the
 * reason is that hex ids look alike: `aaaa1111` and `aaaa1112` are one glance apart, and the thing
 * being confirmed is destructive and immediate.</p>
 */
export function confirmRevoke(row: KeyRow): string {
  const used = row.lastSeenMonth === undefined || row.lastSeenMonth.length === 0
    ? 'never used'
    : `last used ${row.lastSeenMonth}`;

  return `Revoke ${describe(row.note).toLowerCase()}? It is ${used}, has sent ${row.sent}, `
    + `and ${row.waiting} of its pairs are still waiting. This cannot be undone, and anything `
    + 'sending with it stops immediately.';
}
