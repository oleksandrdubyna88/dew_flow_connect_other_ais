import { IssuedKey, KeyRow, KeysPage, Revocation } from './bugsAdminShapes';

/**
 * What the server sent, checked — because a response is external data like any other.
 *
 * <p><b>Why this file exists.</b> The client used to cast: `(await response.json()) as T`, which
 * tells the compiler to stop looking and tells the runtime nothing. The code round found what that
 * costs on the one path that cannot be retried: a `201` whose `key` field is missing or is not a
 * string passes the cast, gets written to `SecretStorage` as a pending issuance, and is then
 * rejected when it is read back — so the only copy of a committed key is destroyed by the code
 * meant to preserve it. On the listing the same cast turns `items: null` into a crash during the
 * redraw, leaving the tab on whatever it was showing before.</p>
 *
 * <p>So every success body is read field by field here, and anything that does not arrive as its
 * contract says is a failure the caller can render rather than an exception it cannot. This is the
 * "validate at the boundary, never trust external data" rule applied to the one boundary this
 * extension has that it does not own.</p>
 */

/** What reading a body came to: the value, or the sentence saying what was wrong with it. */
export type Read<T> =
  | { readonly kind: 'read'; readonly value: T }
  | { readonly kind: 'malformed'; readonly why: string };

/** A page of keys, or why this is not one. */
export function readKeysPage(body: unknown): Read<KeysPage> {
  const page = asRecord(body);
  if (page === undefined) {
    return malformed('a page of keys');
  }

  const items = page['items'];
  if (!Array.isArray(items)) {
    // `items: null` is the case that used to crash the redraw rather than show anything.
    return malformed('a page whose `items` is a list');
  }

  const rows: KeyRow[] = [];
  for (const candidate of items) {
    const row = readKeyRow(candidate);
    if (row.kind === 'malformed') {
      return { kind: 'malformed', why: row.why };
    }

    rows.push(row.value);
  }

  const next = page['nextBefore'];

  return {
    kind: 'read',
    value: {
      items: rows,
      limit: whole(page['limit']),
      total: whole(page['total']),
      ...(typeof next === 'string' && next.length > 0 ? { nextBefore: next } : {}),
    },
  };
}

/** One key of a listing. */
function readKeyRow(body: unknown): Read<KeyRow> {
  const row = asRecord(body);
  if (row === undefined || typeof row['id'] !== 'string' || row['id'].length === 0) {
    return malformed('a key with an id');
  }

  const revoked = row['revokedUtc'];
  const month = row['lastSeenMonth'];

  return {
    kind: 'read',
    value: {
      id: row['id'],
      note: text(row['note']),
      createdUtc: text(row['createdUtc']),
      // ABSENT rather than empty, both of them: `revokedUtc` absent means in force, and
      // `lastSeenMonth` absent means never used. An empty string would render as a blank cell
      // where the promise says the word is "never".
      ...(typeof revoked === 'string' && revoked.length > 0 ? { revokedUtc: revoked } : {}),
      ...(typeof month === 'string' && month.length > 0 ? { lastSeenMonth: month } : {}),
      sent: whole(row['sent']),
      waiting: whole(row['waiting']),
    },
  };
}

/**
 * The one response that carries a key.
 *
 * <p>Checked hardest of the three, because it is the only one that cannot be asked for again: a
 * body that passes here is about to be the sole copy of a credential the server has committed.</p>
 */
export function readIssued(body: unknown): Read<IssuedKey> {
  const issued = asRecord(body);
  if (issued === undefined || typeof issued['id'] !== 'string' || issued['id'].length === 0) {
    return malformed('an issued key with an id');
  }

  if (typeof issued['key'] !== 'string' || issued['key'].length === 0) {
    return malformed('an issued key that carries the key itself');
  }

  return {
    kind: 'read',
    value: {
      id: issued['id'],
      key: issued['key'],
      note: text(issued['note']),
      createdUtc: text(issued['createdUtc']),
    },
  };
}

/** What revoking came to. */
export function readRevocation(body: unknown): Read<Revocation> {
  const done = asRecord(body);
  if (done === undefined || typeof done['id'] !== 'string') {
    return malformed('a revocation naming the key');
  }

  return {
    kind: 'read',
    value: {
      id: done['id'],
      revokedUtc: text(done['revokedUtc']),
      // Anything but an explicit `false` is treated as "it changed", because the safe reading of a
      // missing flag is that something happened rather than that nothing did.
      changed: done['changed'] !== false,
    },
  };
}

function malformed<T>(wanted: string): Read<T> {
  return {
    kind: 'malformed',
    why: `the server's answer is not ${wanted}. Nothing was changed here; the server may still `
      + 'have acted, so check the listing before trying again.',
  };
}

function asRecord(body: unknown): Record<string, unknown> | undefined {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
    ? (body as Record<string, unknown>)
    : undefined;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** A count. Anything that is not a finite number is zero, which is what an absent count means. */
function whole(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}
