import assert from 'node:assert/strict';
import { test } from 'node:test';

import { KeyRow } from '../bugsAdminApi';
import { Users, View, lastSeen, live, safe, standing, usersPageHtml, withControls } from '../bugsKeysPage';

/**
 * The Users tab, RUN — because a list with a Revoke button is exactly where wiring goes wrong.
 *
 * <p>`.agents/PROJECT.md` refuses a new behavioural assertion over page source text, and this is
 * the page that earns the rule rather than merely obeying it: the defect that matters here is a
 * revoke wired to the neighbouring row, and every regex anybody could write about the markup passes
 * while that is true. So the shipped script is executed and what it POSTS is what is asserted.</p>
 *
 * <p>The plan round named the assertions this file must make — two distinct keys, each row's own
 * control clicked, the confirmation naming THAT row — because "the page is run by its test" can
 * otherwise mean a test that executes the script and asserts nothing anybody would notice.</p>
 */

const key = (id: string, note: string, extra: Partial<KeyRow> = {}): KeyRow => ({
  id,
  note,
  createdUtc: '2026-09-17T10:00:00.0000000Z',
  lastSeenMonth: '2026-09',
  sent: 3,
  waiting: 1,
  ...extra,
});


/** A row for a key nobody has used: the property is ABSENT, which is what the server sends. */
const neverUsed = (id: string, note: string): KeyRow => ({
  id,
  note,
  createdUtc: '2026-09-17T10:00:00.0000000Z',
  sent: 0,
  waiting: 0,
});

interface Posted {
  readonly type: string;
  readonly id?: string;
}

/** A control the script finds by id, or a revoke button it finds by `data-revoke`. */
class Control {
  disabled = false;

  constructor(readonly id: string, private readonly revokes = '') {}

  getAttribute(name: string): string | null {
    return name === 'data-revoke' && this.revokes.length > 0 ? this.revokes : null;
  }

  closest(selector: string): Control | null {
    return selector === '[data-revoke]' && this.revokes.length > 0 ? this : null;
  }
}

/** The page's own script, cut out of the page it ships in. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the page has no script to run');

  return html.slice(start, end);
}

interface Page {
  readonly html: string;
  readonly posted: readonly Posted[];
  /** One per row the page ACTUALLY rendered a revoke button for, in order. */
  readonly revokeButtons: readonly Control[];
  control(id: string): Control;
  click(what: Control): void;
}

/**
 * Run the page.
 *
 * <p>The revoke buttons are built from the ids the page RENDERED, read out of its own markup — not
 * from the fixture. A shim handed the fixture's ids would pass with no button on the page at all,
 * and would pass just as well if every button carried the first row's id, which is the defect.</p>
 */
function run(users: Users): Page {
  const html = usersPageHtml(users, 'test-nonce');
  const revokeButtons = [...html.matchAll(/data-revoke="([^"]+)"/gu)]
    .map((m) => new Control('', m[1]!));
  const controls = new Map<string, Control>();
  const posted: Posted[] = [];
  let onClick: ((event: { target: unknown }) => void) | undefined;

  const document = {
    addEventListener: (kind: string, handler: (event: { target: unknown }) => void) => {
      if (kind === 'click') {
        onClick = handler;
      }
    },
    querySelectorAll: (): readonly Control[] => [],
    getElementById: (id: string): Control | undefined => controls.get(id),
  };

  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('acquireVsCodeApi', 'document', pageScript(html));
  body(() => ({ postMessage: (m: Posted) => posted.push(m) }), document);

  assert.ok(onClick !== undefined, 'the page never attached a click listener');

  return {
    html,
    posted,
    revokeButtons,
    control: (id: string) => {
      assert.ok(html.includes(`id="${id}"`), `the page rendered no control with id ${id}`);
      const made = controls.get(id) ?? new Control(id);
      controls.set(id, made);

      return made;
    },
    click: (what: Control) => onClick!({ target: what }),
  };
}

const listing = (rows: readonly KeyRow[], over: Partial<Extract<View, { kind: 'keys' }>> = {}): Users => ({
  view: {
    kind: 'keys',
    rows,
    total: rows.length,
    hasNext: false,
    hasBack: false,
    said: '',
    pagedPastTheEnd: false,
    ...over,
  },
});

test('the page script is a program, not a string that looks like one', () => {
  assert.doesNotThrow(() => run(listing([key('aaaa1111', 'alice')])));
});

/**
 * THE assertion this file exists for: each row's control revokes ITS OWN row.
 *
 * <p>Two keys, because one cannot tell a button wired to its own row from a button wired to the
 * first. Both are clicked and both are checked, because a page that always posts the LAST id passes
 * a test that only clicks the last one.</p>
 */
test('each row revokes the key in that row, not its neighbour', () => {
  const page = run(listing([key('aaaa1111', 'alice'), key('bbbb2222', 'bob')]));

  assert.equal(page.revokeButtons.length, 2, 'a row with no control cannot be revoked');

  page.click(page.revokeButtons[0]!);
  page.click(page.revokeButtons[1]!);

  assert.deepEqual(
    page.posted.filter((m) => m.type === 'revoke').map((m) => m.id),
    ['aaaa1111', 'bbbb2222'],
    'each button must carry the id of the row it sits in',
  );
});

/** A revoked row offers no button — the action is gone, not merely discouraged. */
test('an already revoked key has no revoke control', () => {
  const page = run(listing([
    key('aaaa1111', 'alice'),
    key('bbbb2222', 'bob', { revokedUtc: '2026-09-17T11:00:00.0000000Z' }),
  ]));

  assert.deepEqual(page.revokeButtons.map((b) => b.getAttribute('data-revoke')), ['aaaa1111']);
});

/** A click that is not a control posts nothing — the page must not act on stray clicks. */
test('clicking nothing in particular posts nothing but the ready message', () => {
  const page = run(listing([key('aaaa1111', 'alice')]));

  page.click(new Control('somewhere-else'));

  assert.deepEqual(page.posted.map((m) => m.type), ['ready']);
});

test('the bar posts the action its button names', () => {
  const page = run(listing([key('aaaa1111', 'alice')], { hasNext: true, hasBack: true }));

  for (const id of ['issue', 'refresh', 'next', 'back']) {
    page.click(page.control(id));
  }

  assert.deepEqual(
    page.posted.map((m) => m.type),
    ['ready', 'issue', 'refresh', 'next', 'back'],
  );
});

/** Back and Next are disabled when there is nowhere to go — the cursor stack decides, not the page. */
test('back and next are disabled when the extension says there is nowhere to go', () => {
  const page = run(listing([key('aaaa1111', 'alice')]));

  assert.match(page.html, /id="back" disabled/u);
  assert.match(page.html, /id="next" disabled/u);
});

/**
 * A pending issuance offers BOTH ways out, and says what discarding does.
 *
 * <p>The copy-before-dismiss promise cannot be kept by a modal — a webview cannot veto its own
 * disposal — so the key is held in `SecretStorage` and this block is what a person meets on the
 * next open. Discard REVOKES; a discard that merely forgot would leave a live key nobody holds,
 * which is the defect the whole rule was written against.</p>
 */
test('a key that was never copied offers copy and discard, and says discard revokes', () => {
  const page = run({
    view: {
      kind: 'keys', rows: [], total: 0, hasNext: false, hasBack: false, said: '', pagedPastTheEnd: false,
    },
    pending: {
      id: 'aaaa1111',
      key: 'the-key-itself',
      note: 'a workshop',
      createdUtc: '2026-09-17T10:00:00.0000000Z',
      server: 'https://bugs.example',
    },
  });

  page.click(page.control('copy'));
  page.click(page.control('discard'));

  assert.deepEqual(page.posted.map((m) => m.type), ['ready', 'copy', 'discard']);
  assert.match(page.html, /revokes it on the server/u, 'discarding must say that it revokes');
  assert.ok(page.html.includes('the-key-itself'), 'the key it is offering must be the one it holds');
});

/**
 * The 401 face names no cause, because the server refuses to.
 *
 * <p>The plan asked for "empty, invalid and rotated" states and the round refused all three: an
 * absent `COAI_BUGS_ADMIN_KEYS`, a wrong key and a revoked one are one answer by design. A tab that
 * picked one would be guessing, and the wrong guess sends somebody to replace a working key.</p>
 */
test('a rejected key says the server would not accept it and does NOT say why', () => {
  const page = run({ view: { kind: 'rejected', why: "an administrator's key is required", said: '' } });

  assert.match(page.html, /did not accept this key/u);
  assert.match(page.html, /whether the key is wrong, has been revoked, or no administrators/u);
  // ESCAPED, because the server's sentence is somebody else's text like any other — the apostrophe
  // comes through as an entity, which is the escaping doing its job rather than a lost word.
  assert.ok(
    page.html.includes('an administrator&#39;s key is required'),
    "the server's own words, escaped",
  );
  assert.doesNotMatch(
    page.html,
    /your key is invalid|the key is invalid|invalid key/iu,
    'this server cannot tell an invalid key from an unconfigured server, so neither may the tab',
  );
  page.click(page.control('setkey'));
  assert.deepEqual(page.posted.map((m) => m.type), ['ready', 'setkey']);
});

/** An unreachable server must not read as a credential problem. */
test('an unreachable server says so, and says it is not the key', () => {
  const page = run({ view: { kind: 'unreachable', why: 'fetch failed', said: '' } });

  assert.match(page.html, /could not be reached/u);
  assert.match(page.html, /not a problem with your key/u);
  assert.ok(page.html.includes('fetch failed'));
});

/** A 429 says what to wait, and says nothing is retried behind the person's back. */
test('a rate-limited tab names the wait and refuses to retry silently', () => {
  const page = run({
    view: {
      kind: 'limited',
      why: 'at most 120 requests a minute per administrator',
      retryAfterSeconds: 42,
      said: '',
    },
  });

  assert.match(page.html, /42 s/u);
  assert.match(page.html, /Nothing is retried automatically/u);
});

test('with no key at all, the tab offers to set one and says why not settings', () => {
  const page = run({ view: { kind: 'no-key', said: '' } });

  assert.match(page.html, /secret storage/iu);
  assert.match(page.html, /settings sync/iu);
});

/** The two things this tab cannot show, said rather than left as an absence. */
test('the page says keys cannot be read back and administrators are not listed', () => {
  const page = run(listing([key('aaaa1111', 'alice')]));

  assert.match(page.html, /shown ONCE/u);
  assert.match(page.html, /Administrators are not listed here/u);
});

test('an empty listing is not the same page as a failure', () => {
  assert.match(run(listing([])).html, /No keys have been issued yet/u);
});

/** `total` is a count of KEYS. A page count cannot be computed from an opaque cursor. */
test('the total is rendered as keys and never as pages', () => {
  const page = run(listing([key('aaaa1111', 'alice'), key('bbbb2222', 'bob')]));

  assert.match(page.html, /2 keys/u);
  assert.doesNotMatch(page.html, /page \d+ of/iu);
});

test('one key is a key and not 1 keys', () => {
  assert.match(run(listing([key('aaaa1111', 'alice')])).html, /1 key</u);
});

/** A note is somebody else's text and reaches the page as markup otherwise. */
test('a note that is markup is escaped', () => {
  const page = run(listing([key('aaaa1111', '<img src=x onerror=alert(1)>')]));

  assert.ok(!page.html.includes('<img src=x'), 'a note must never reach the page as an element');
  assert.match(page.html, /&lt;img src=x/u);
});

test('a key nobody has used reads as never, not as a blank', () => {
  assert.equal(lastSeen(neverUsed('a', 'n')), 'never', 'an absent month is never used');
  assert.equal(lastSeen(key('a', 'n', { lastSeenMonth: '' })), 'never', 'and so is an empty one');
  assert.equal(lastSeen(key('a', 'n', { lastSeenMonth: '2026-09' })), '2026-09');
});

test('standing says in force or when it ended', () => {
  assert.equal(standing(key('a', 'n')), 'in force');
  assert.equal(standing(key('a', 'n', { revokedUtc: '2026-09-17T11:00:00.0000000Z' })), 'revoked 2026-09-17');
  assert.equal(live(key('a', 'n')), true);
  assert.equal(live(key('a', 'n', { revokedUtc: '2026-09-17T11:00:00.0000000Z' })), false);
});

test('safe escapes every character that could close an attribute or open a tag', () => {
  assert.equal(safe(`<&>"'`), '&lt;&amp;&gt;&quot;&#39;');
});

/** While something is in flight, nothing that would queue a second action is pressable. */
test('every control is disabled while an action is in flight', () => {
  const page = run({
    view: {
      kind: 'keys',
      rows: [key('aaaa1111', 'alice')],
      total: 1,
      hasNext: true,
      hasBack: true,
      said: '',
      pagedPastTheEnd: false,
    },
    busy: true,
  });

  for (const id of ['issue', 'refresh', 'back', 'next']) {
    assert.match(page.html, new RegExp(`id="${id}" disabled`, 'u'), `${id} must not be pressable`);
  }

  assert.match(page.html, /data-revoke="aaaa1111" disabled/u, 'least of all a revoke');
  assert.match(page.html, /id="busy"/u, 'and it must SAY that something is happening');
});

/**
 * And the page that gives them BACK, which is the other half of the same defect.
 *
 * <p>The flag dropped when the action ended and nothing painted again, so the disabled page above
 * stayed on screen — with Refresh disabled like everything else, which left no button able to
 * recover it. What follows is the page the coordinator repaints at that moment, RUN. (Code round 2,
 * codex.)</p>
 */
test('the page a finished action repaints has its controls back', () => {
  const working: Users = {
    view: {
      kind: 'keys',
      rows: [key('aaaa1111', 'alice')],
      total: 1,
      hasNext: true,
      hasBack: true,
      said: 'A key was issued. Copy it now.',
      pagedPastTheEnd: false,
    },
    busy: true,
  };

  const done = withControls(working, false);
  assert.ok(done !== undefined, 'a disabled page is left disabled when its action ends');

  const page = run(done);
  for (const id of ['issue', 'refresh', 'back', 'next']) {
    assert.doesNotMatch(page.html, new RegExp(`id="${id}" disabled`, 'u'), `${id} is dead after the action finished`);
  }

  assert.doesNotMatch(page.html, /id="busy"/u, 'it still says something is happening when nothing is');
  assert.ok(page.html.includes('A key was issued'), 'what the action said is repainted with the page');
});

/** A repaint replaces the whole page, so one that changes nothing costs a scroll position. */
test('nothing is repainted when what is on screen already says what it should', () => {
  const live: Users = { view: { kind: 'no-key', said: '' } };

  assert.equal(withControls(live, false), undefined, 'a live page is repainted for nothing');
  assert.equal(withControls(undefined, true), undefined, 'there is nothing on screen to repaint');
  assert.ok(withControls(live, true) !== undefined, 'an action began and the page still looks pressable');
});

/** The sentence an action produced is shown above every face, not only the listing. */
test('what the last action said is rendered on a face that is not the listing', () => {
  const warned = 'a key MAY have been created — check the newest row';
  const page = run({ view: { kind: 'unreachable', why: 'fetch failed', said: warned } });

  assert.ok(page.html.includes('a key MAY have been created'), 'the warning must survive the face');
});

/** The end of a walk says so, rather than claiming the server has no keys. */
test('paging past the end says it is the end, not that none exist', () => {
  const page = run(listing([], { pagedPastTheEnd: true, hasBack: true }));

  assert.match(page.html, /end of the list/u);
  assert.doesNotMatch(page.html, /No keys have been issued yet/u);
});

test('an empty first page still says none have been issued', () => {
  assert.match(run(listing([])).html, /No keys have been issued yet/u);
});

/** An orphaned attempt cannot show the key, so it says what IS knowable. */
test('an issuance nobody heard the answer to points at the newest row', () => {
  const page = run({
    view: { kind: 'keys', rows: [], total: 0, hasNext: false, hasBack: false, said: '', pagedPastTheEnd: false },
    orphaned: { server: 'https://bugs.example', note: 'the tuesday workshop' },
  });

  assert.match(page.html, /may have been issued/u);
  assert.match(page.html, /NEWEST row/u);
  assert.ok(page.html.includes('https://bugs.example'), 'and which server it was asked of');

  page.click(page.control('dismiss'));
  assert.deepEqual(page.posted.map((m) => m.type), ['ready', 'dismiss']);
});

/** An address that may not carry a key blames neither the key nor the server. */
test('an unsafe address says the key was not sent anywhere', () => {
  const page = run({ view: { kind: 'unsafe', why: 'it is not https', said: '' } });

  assert.match(page.html, /not sent anywhere/u);
  assert.doesNotMatch(page.html, /did not accept this key/u, 'the key was never offered');
});

/** A server that refused is not a server that could not be reached. */
test('a refusal says the server answered and offers the way back', () => {
  const page = run({ view: { kind: 'refused', why: "before is 'nonsense'", said: '' } });

  assert.match(page.html, /refused that request/u);
  assert.match(page.html, /not a connection problem/u);
  assert.ok(page.html.includes("before is &#39;nonsense&#39;"), "the server's own words, escaped");
});
