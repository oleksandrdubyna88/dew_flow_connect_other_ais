import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  DETAIL_LIMIT,
  NotificationRecord,
  TITLE_LIMIT,
  isKnownClass,
  notificationLine,
  parseNotificationLine,
  parseNotifications,
  safeText,
} from '../notifications';

/**
 * The record shape, the line it becomes, and the line read back.
 *
 * <p>The first test is the one that matters and it is deliberately written NOT to name the fields
 * it covers: it reads them off the record. A test that listed `title`, `detail`, `cure` would
 * repeat a list the code also holds and would not notice the field added next year — which is the
 * defect the plan round found in this plan's own first draft, where the redaction covered `detail`
 * and left eight other strings composed from the same exception text.</p>
 */

/** A secret no redactor may let through, in a shape all of them recognise. */
const SECRET = 'Bearer abcdefghijklmnopqrstuvwxyz0123456789';

/** Every string field this record type has, filled — so the sweep below has something to sweep. */
function everyFieldFilled(value: string): NotificationRecord {
  return {
    utc: '2026-09-16T17:05:39.812Z',
    class: 'stand-down',
    source: value,
    code: value,
    subject: value,
    title: value,
    detail: value,
    cure: value,
    action: value,
    answer: value,
    run: value,
    repo: value,
    branch: value,
    session: value,
    provider: value,
    role: value,
    pid: 37308,
    seq: 10,
  };
}

test('no string field of a record reaches the line carrying a secret', () => {
  const record = everyFieldFilled(`something went wrong: ${SECRET}`);

  const line = notificationLine(record);

  assert.ok(!line.includes('abcdefghijklmnopqrstuvwxyz0123456789'), line);
  assert.ok(line.includes('[redacted]'));

  // And the sweep really did visit every string: read the fields back off the record rather than
  // naming them, so a field added later is covered by this test the day it is added.
  const strings = Object.entries(record).filter(([, v]) => typeof v === 'string');
  assert.ok(strings.length >= 15, `expected the record to have string fields to sweep, got ${strings.length}`);
  const read = parseNotificationLine(line.trim());
  assert.ok(read !== undefined);
  const back = read as unknown as Record<string, unknown>;
  for (const [field] of strings) {
    const after: unknown = back[field];
    if (typeof after === 'string') {
      assert.ok(
        !after.includes('abcdefghijklmnopqrstuvwxyz0123456789'),
        `${field} kept the secret: ${after}`,
      );
    }
  }
});

test('a url carrying its own credentials loses them and keeps its host', () => {
  const safe = safeText('failed against https://someone:hunter2@coai.remsoft.dev/api', DETAIL_LIMIT);

  assert.ok(!safe.includes('hunter2'), safe);
  assert.ok(safe.includes('coai.remsoft.dev'), safe);
});

test('a query parameter named as a credential is redacted and an ordinary one is not', () => {
  const safe = safeText('GET /models?api-version=2024-02-01&api_key=abcd1234efgh', DETAIL_LIMIT);

  assert.ok(!safe.includes('abcd1234efgh'), safe);
  assert.ok(safe.includes('api-version=2024-02-01'), `an api version is not a secret: ${safe}`);
});

test('vendor key shapes are recognised on sight', () => {
  for (const key of ['sk-abcdefghijklmnop', 'ghp_abcdefghijklmnop', 'xoxb-abcdefghijklmnop']) {
    const safe = safeText(`the cli said: ${key}`, DETAIL_LIMIT);
    assert.ok(!safe.includes('abcdefghijklmnop'), `${key} survived as ${safe}`);
  }
});

test('control characters never reach the line, because a torn line is unreadable to everyone', () => {
  const safe = safeText('before' + String.fromCharCode(0, 7) + 'after', TITLE_LIMIT);

  assert.equal(safe, 'beforeafter');
});

test('a long detail is cut and says so; a long title is cut harder', () => {
  const long = 'x'.repeat(DETAIL_LIMIT * 2);

  const detail = safeText(long, DETAIL_LIMIT);
  const title = safeText(long, TITLE_LIMIT);

  assert.ok(detail.startsWith('x'.repeat(DETAIL_LIMIT)));
  assert.ok(detail.endsWith('(truncated)'), detail.slice(-20));
  assert.ok(title.length < detail.length);
  assert.ok(title.startsWith('x'.repeat(TITLE_LIMIT)));
});

test('a record round-trips through its line', () => {
  const record: NotificationRecord = {
    utc: '2026-09-16T17:05:39.812Z',
    class: 'stand-down',
    source: 'serverSettingsSync',
    code: 'settings-stood-down',
    title: 'The server settings were left alone',
    cure: 'Reload the window.',
    action: 'Reload Window',
    run: 'a1c9',
    pid: 37308,
    seq: 1,
  };

  const read = parseNotificationLine(notificationLine(record).trim());

  assert.deepEqual(read, record);
});

test('a record missing any of the four required fields is not a record', () => {
  const whole = {
    utc: '2026-09-16T17:05:39.812Z', class: 'failure', source: 'rolesPanel', code: 'prompt-unreadable',
  };

  assert.ok(parseNotificationLine(JSON.stringify(whole)) !== undefined);
  for (const missing of ['utc', 'class', 'source', 'code']) {
    const without: Record<string, unknown> = { ...whole };
    delete without[missing];
    assert.equal(
      parseNotificationLine(JSON.stringify(without)),
      undefined,
      `a line with no ${missing} was read as a record`,
    );
  }
});

test('a class this build has never heard of is KEPT, not dropped', () => {
  const line = JSON.stringify({
    utc: '2026-09-16T17:05:39.812Z', class: 'whatever-ships-next', source: 'future', code: 'x',
  });

  const read = parseNotificationLine(line);

  assert.ok(read !== undefined, 'a newer build must not make its records vanish from an older reader');
  assert.equal(read?.class as string, 'whatever-ships-next');
  assert.equal(isKnownClass('whatever-ships-next'), false);
  assert.equal(isKnownClass('stand-down'), true);
});

test('a torn line costs itself and nothing else', () => {
  const good = notificationLine({
    utc: '2026-09-16T17:05:39.812Z', class: 'failure', source: 'a', code: 'b',
  });
  const also = notificationLine({
    utc: '2026-09-16T17:06:00.000Z', class: 'refusal', source: 'c', code: 'd',
  });

  const read = parseNotifications(`${good}{"utc":"2026-09-16T17:0\n${also}`);

  assert.equal(read.length, 2);
  assert.deepEqual(read.map((r) => r.code), ['b', 'd']);
});

test('seq zero is kept, because absent and zero are different facts', () => {
  const line = JSON.stringify({
    utc: '2026-09-16T17:05:39.812Z', class: 'failure', source: 'a', code: 'b', seq: 0,
  });

  assert.equal(parseNotificationLine(line)?.seq, 0);
  assert.equal(
    parseNotificationLine(JSON.stringify({
      utc: '2026-09-16T17:05:39.812Z', class: 'failure', source: 'a', code: 'b',
    }))?.seq,
    undefined,
  );
});

test('a credential NAMED and given in ordinary text is taken out, not only one in a URL', () => {
  // The redaction covered URL parameters, URL userinfo, Authorization-style prefixes and vendor key
  // shapes — and nothing at all for the shape these records actually carry most often, because they
  // are built from text nobody chose and a server that refuses a request quotes what it was given
  // back at you. (codex, the code round.)
  const line = notificationLine({
    utc: '2026-09-16T17:05:39.812Z',
    class: 'failure',
    source: 'coai-mcp',
    code: 'server-refused',
    title: 'the gateway said password=letmein was wrong',
    detail: 'api_key: abc123def456 and {"client_secret": "s3cr3t-value"} and PASSWORD = hunter2',
  });

  assert.ok(!line.includes('letmein'), line);
  assert.ok(!line.includes('abc123def456'), line);
  assert.ok(!line.includes('s3cr3t-value'), line);
  assert.ok(!line.includes('hunter2'), line);
  assert.ok(line.includes('[redacted]'));
});

test('an ordinary labelled value is left alone, because a redaction that fires on everything is noise', () => {
  const line = notificationLine({
    utc: '2026-09-16T17:05:39.812Z',
    class: 'failure',
    source: 'coai-mcp',
    code: 'server-refused',
    title: 'author=octocat and api-version=2024-02-01 and count: 17',
  });

  assert.ok(line.includes('octocat'), line);
  assert.ok(line.includes('2024-02-01'), line);
  assert.ok(line.includes('17'), line);
});

test('a URL survives being written down, because a sentence that loses its endpoint is useless', () => {
  // The cheap version of the labelled rule — anything before a colon — eats `https://host`. This is
  // the assertion that says it does not: the scheme is not a credential word, so nothing matches.
  const said = 'could not reach https://coai.example.com:8443/api/v1/rounds';
  const line = notificationLine({
    utc: '2026-09-16T17:05:39.812Z',
    class: 'failure',
    source: 'coai-mcp',
    code: 'server-refused',
    title: said,
  });

  // The whole field, compared exactly, rather than a substring of the line. Stronger - a redaction
  // that mangled the sentence AROUND the URL would slip past an includes() - and it also stops
  // CodeQL reading this as a URL check written the unsafe way, which it raised as a high-severity
  // alert on this PR. It was never a sanitiser, but the shape it objects to is the shape a real one
  // gets wrong, and the exact comparison is the better assertion regardless.
  assert.equal((JSON.parse(line) as { readonly title: string }).title, said);
});

test('a field a newer writer adds survives an older reader', () => {
  // The two halves ship SEPARATELY - the Team server is deployed by hand - so an extension one
  // release behind reading a server-notices.jsonl written by a newer server is routine, not exotic.
  // The parser already keeps a CLASS it has never heard of for this reason; it used to drop a FIELD
  // it had never heard of on the same line. (codex, the code round.)
  const back = parseNotificationLine(JSON.stringify({
    utc: '2026-09-17T09:00:00.000Z',
    class: 'failure',
    source: 'coai-mcp',
    code: 'server-refused',
    serverOnlyField: 'a fact this build has no name for',
    serverOnlyCount: 7,
  }));

  assert.equal(back?.more?.['serverOnlyField'], 'a fact this build has no name for');
  assert.equal(back?.more?.['serverOnlyCount'], 7);
});

test('an unknown field is flattened back on the way out, so a round trip is what arrived', () => {
  const line = JSON.stringify({
    utc: '2026-09-17T09:00:00.000Z', class: 'failure', source: 's', code: 'c', novelty: 'kept',
  });
  const twice = notificationLine(parseNotificationLine(line) as NotificationRecord).trim();

  assert.equal(JSON.parse(twice).novelty, 'kept');
  assert.equal('more' in JSON.parse(twice), false, 'the bag is an implementation detail, not a field');
});

test('an unknown field is redacted like any other, and is not a hole in the measure', () => {
  const back = parseNotificationLine(JSON.stringify({
    utc: '2026-09-17T09:00:00.000Z', class: 'failure', source: 's', code: 'c',
    whatTheServerSent: 'the call failed with api_key: abc123def456',
  }));
  const line = notificationLine(back as NotificationRecord);

  assert.ok(!line.includes('abc123def456'), line);
});

test('an unknown value that is not flat is dropped, because a line here has to stay flat', () => {
  const back = parseNotificationLine(JSON.stringify({
    utc: '2026-09-17T09:00:00.000Z', class: 'failure', source: 's', code: 'c',
    nested: { a: 1 }, list: [1, 2], nothing: null,
  }));

  assert.equal(back?.more, undefined, 'none of the three is a string or a finite number');
});

test('the identity of a row is cleaned but never rewritten', () => {
  // `class`, `source`, `code` and `run` are literals chosen in the source, not text anybody typed,
  // so there is nothing in them to redact - and rewriting them risks the one thing that must not
  // happen here: a persisted key that no longer matches the key the suppressor admitted, which
  // would make a row ungroupable with its own repeats. (codex, the code round.)
  const line = notificationLine({
    utc: '2026-09-17T09:00:00.000Z',
    class: 'failure',
    source: 'auth-probe',
    code: 'oauth-token-expired',
    run: 'a1c9f0d3e5b7',
  });
  const back = JSON.parse(line);

  assert.equal(back.code, 'oauth-token-expired');
  assert.equal(back.source, 'auth-probe');
  assert.equal(back.run, 'a1c9f0d3e5b7');
});

test('an identity field is still bounded and still stripped of what cannot be read back', () => {
  // Not redacted is not "not cleaned". A control character in a code makes a line no JSON.parse can
  // read, which costs the whole row rather than the field.
  const line = notificationLine({
    utc: '2026-09-17T09:00:00.000Z',
    class: 'failure',
    source: `a${String.fromCharCode(0)}b`,
    code: 'x'.repeat(TITLE_LIMIT + 50),
  });
  const back = JSON.parse(line);

  assert.equal(back.source, 'ab');
  assert.ok(back.code.length < TITLE_LIMIT + 50, 'bounded');
  assert.deepEqual(typeof back.code, 'string');
});

test('the redactor rewrites no code this product can emit', () => {
  // The guard that replaced an exemption. The code round asked for identity fields to skip
  // redaction, on the ground that a code like `oauth-token-expired` could come back as
  // `oauth-[redacted]-expired` and stop matching the key the suppressor admitted. Exempting them
  // broke the older and better invariant - EVERY string field is redacted, because "a measure
  // applied at SOME of its sites" is the defect security.md names - and an existing test said so.
  //
  // So the invariant stays whole and the concern becomes this: every `code` literal the product
  // can write is in the generated inventory, and redaction must be a no-op on all of them. A code
  // that the redactor would rewrite now fails here on the day it is added, rather than producing a
  // row that silently cannot be grouped with its own repeats.
  const inventory = JSON.parse(
    readFileSync(join(__dirname, '..', '..', 'notification-sites.json'), 'utf8'),
  ) as { readonly codes: readonly string[] };

  assert.ok(inventory.codes.length > 50, `only ${inventory.codes.length} codes found - has the scan broken?`);
  for (const code of inventory.codes) {
    assert.equal(safeText(code, TITLE_LIMIT), code, `redaction rewrites the code ${code}`);
  }
});
