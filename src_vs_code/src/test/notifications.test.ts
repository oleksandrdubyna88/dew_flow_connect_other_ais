import assert from 'node:assert/strict';
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
  const line = notificationLine({
    utc: '2026-09-16T17:05:39.812Z',
    class: 'failure',
    source: 'coai-mcp',
    code: 'server-refused',
    title: 'could not reach https://coai.example.com:8443/api/v1/rounds',
  });

  assert.ok(line.includes('https://coai.example.com:8443/api/v1/rounds'), line);
});
