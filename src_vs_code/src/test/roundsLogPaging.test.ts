import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DbTotals, EMPTY_TOTALS } from '../roundsDb';
import { LogRow, PAGE_SIZE, roundsLogHtml } from '../roundsLog';

/**
 * The page holds a PAGE, and the numbers under it come from SQL.
 *
 * <p>These run the page's own script against a stub DOM, the way `bundledPage.test.ts` does — the
 * paging, the footer and the five states of an opened row are decisions the script makes, and
 * asserting on the source text of a branch is not the same as watching it taken.</p>
 *
 * <p>The reason any of it exists is a measurement: `--log --limit 300` answered <b>3.83 MB</b>, of
 * which the round rows were <b>0.05 MB</b>. The rest was 3 484 findings shipped for every round
 * although the page opens them one at a time.</p>
 */

/**
 * A row, complete.
 *
 * <p>No `as LogRow`. The whole value of a fixture standing in for a real type is that the compiler
 * checks it, and a cast turns the day a field is added into a silent pass. The code round named
 * this, and the repository's own doctrine forbids it.</p>
 */
function row(over: Partial<LogRow> = {}): LogRow {
  return {
    key: 'k1', kind: 'review',
    startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE — the thing', status: 'done', decided: null, verdict: 'proceed', gating: 1,
    findings: 1, seconds: 130, decideSeconds: null, tokensIn: null, tokensOut: null, costUsd: null, costInUsd: null,
    costOutUsd: null, costTotalUsd: null, costIsEstimate: false, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'], reviewers: ['codex/Architecture — done'],
    calledBy: 'codex 0.9 · model not stated',
    reviewerColours: ['#fff'], found: [], foundCount: 0, foundState: 'unasked', origin: 'db',
    dbKey: { sessionId: 's1', stage: 'CodeReview', number: 1 },
    ...over,
  };
}

function many(howMany: number): LogRow[] {
  return Array.from({ length: howMany }, (_, i) => row({
    key: `k${i}`,
    number: i + 1,
    // Newest first once sorted, so row 0 is the newest.
    startedUtc: new Date(Date.parse('2026-09-05T00:00:00.000Z') + (howMany - i) * 60_000).toISOString(),
  }));
}

const TOTALS: DbTotals = {
  rounds: 250, findings: 3484, accepted: 900, rejected: 2000, gating: 700,
  tokensIn: 1, tokensOut: 2, costUsd: 3,
};

/** One element of the stub DOM: enough of one that the page script cannot tell the difference. */
interface Stub {
  innerHTML: string;
  textContent: string;
  hidden: boolean;
  disabled: boolean;
  value: string;
  className: string;
  readonly heard: Record<string, (event?: unknown) => void>;
  addEventListener(kind: string, listener: (event?: unknown) => void): void;
  getAttribute(): null;
  setAttribute(): void;
  querySelectorAll(): [];
}

interface Page {
  readonly at: (id: string) => Stub;
  readonly click: (target: unknown) => void;
  readonly deliver: (message: unknown) => void;
  readonly posted: unknown[];
}

/** Runs the page script of a freshly built page and hands back the levers a person has. */
function open(rows: readonly LogRow[], totals: DbTotals = TOTALS): Page {
  const html = roundsLogHtml(rows, [], 'n0nce', 'usage', 'spots', totals);
  const tag = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = tag.slice(tag.indexOf('>') + 1);

  const elements = new Map<string, Stub>();
  const at = (id: string): Stub => {
    const known = elements.get(id);
    if (known !== undefined) {
      return known;
    }
    const heard: Record<string, (event?: unknown) => void> = {};
    const fresh: Stub = {
      innerHTML: '', textContent: '', hidden: false, disabled: false, value: '', className: '', heard,
      addEventListener(kind, listener) { heard[kind] = listener; },
      getAttribute: () => null,
      setAttribute: () => undefined,
      querySelectorAll: () => [],
    };
    elements.set(id, fresh);

    return fresh;
  };

  const clicks: ((event: unknown) => void)[] = [];
  const messages: ((event: unknown) => void)[] = [];
  const posted: unknown[] = [];
  const document_ = {
    getElementById: at,
    querySelectorAll: () => [],
    addEventListener(kind: string, listener: (event: unknown) => void) {
      if (kind === 'click') {
        clicks.push(listener);
      }
    },
  };
  const window_ = {
    addEventListener(kind: string, listener: (event: unknown) => void) {
      if (kind === 'message') {
        messages.push(listener);
      }
    },
  };

  // eslint-disable-next-line no-new-func
  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_, window_, () => ({ postMessage: (m: unknown) => posted.push(m) }));

  // The page opens on TODAY, and these rows are dated whenever the fixture says. Clearing the range
  // is what a person does when they want the whole log, and it makes these tests independent of the
  // clock they run on.
  at('alldates').heard['click']?.();

  return {
    at,
    posted,
    click: (target) => clicks.forEach((one) => one({ target })),
    deliver: (message) => messages.forEach((one) => one({ data: message })),
  };
}

/**
 * A click target NESTED inside a row, answering `closest` for every selector above it.
 *
 * <p>`hit` answers for one selector and null for everything else, which cannot express a button
 * INSIDE a row — and a test using it can never observe a handler falling through to the row branch,
 * because the fall-through has nothing to find. The export button's "does not also expand" test was
 * green with its own `return` deleted until this existed.</p>
 */
function hitNested(attributes: Readonly<Record<string, string>>): unknown {
  return {
    closest: (asked: string) => {
      const found = Object.keys(attributes).find((selector) => selector === asked);

      return found === undefined ? null : { getAttribute: () => attributes[found] };
    },
  };
}

/** A click target that answers `closest` for exactly one selector, as a real element would. */
function hit(selector: string, attribute: string): unknown {
  return { closest: (asked: string) => (asked === selector ? { getAttribute: () => attribute } : null) };
}

// ---------- a page is two hundred rows ----------

test('a page holds two hundred rows however many there are, and says which ones', () => {
  // The operator, 2026-09-09: «у нас есть пагинаций. 200 на стр достаточно.»
  const page = open(many(250));

  assert.equal(PAGE_SIZE, 200);
  assert.equal((page.at('rows').innerHTML.match(/<tr data-key=/g) ?? []).length, 200);
  assert.match(page.at('pageinfo').textContent, /rows 1–200 of 250/);
  assert.equal(page.at('prev').disabled, true, 'there is nothing newer than the first page');
  assert.equal(page.at('next').disabled, false);
});

test('Older moves on by a page and Newer comes back, and neither can walk off the end', () => {
  const page = open(many(250));

  page.at('next').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /rows 201–250 of 250/);
  assert.equal((page.at('rows').innerHTML.match(/<tr data-key=/g) ?? []).length, 50);
  assert.equal(page.at('next').disabled, true, 'and there is nothing older');

  page.at('next').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /rows 201–250 of 250/, 'Older past the end stays put');

  page.at('prev').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /rows 1–200 of 250/);

  page.at('prev').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /rows 1–200 of 250/, 'and Newer past the start stays put');
});

test('the totals under the table are the database\u0027s, not the length of what was sent', () => {
  // «суммы - скл счиатть (сколько всего и тд.)». The page holds 250 rows and 0 findings; the line
  // under it says 250 rounds and 3 484 findings, because that is what SQL counted.
  const page = open(many(250));

  assert.match(page.at('recorded').textContent, /250 rounds and 3484 findings/);
  assert.match(page.at('recorded').textContent, /900 accepted, 2000 rejected, 700 gating/);
  assert.match(page.at('recorded').textContent, /Counted by the database, not by this page/);
});

test('searching returns to the first page, because Older would move in the unfiltered stream', () => {
  // Plan round, gemini: a client-side filter over a server-side window cannot page. Rows would skip
  // or repeat across the boundary, and nobody could tell which.
  const page = open(many(250));

  page.at('next').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /rows 201–250/);

  const search = page.at('search');
  search.value = '';
  search.heard['input']?.({ target: { value: '' } });

  assert.match(page.at('pageinfo').textContent, /rows 1–200 of 250/, 'back to the first page');
});

// ---------- the five states of an opened row ----------

/** Opens the one row on the page and hands back what the expanded cell drew. */
function detailOf(one: LogRow): { html: string; posted: unknown[] } {
  const page = open([one]);
  page.click(hit('tr[data-key]', one.key));

  return { html: page.at('rows').innerHTML, posted: page.posted };
}

test('opening a row asks for that round\u0027s findings, once, and says it is asking', () => {
  const { html, posted } = detailOf(row({ foundState: 'unasked', foundCount: 3 }));

  assert.equal(posted.filter((m) => (m as { command?: string }).command === 'findings').length, 1);
  assert.match(html, /Reading what this round found/);
});

test('the request carries the round\u0027s identity, so the host looks nothing up', () => {
  // It used to be looked up in a module-level copy of the last rows the extension built — shared
  // mutable state that answers wrongly for any row a later refresh dropped. Three reviewers of the
  // code round objected independently.
  const { posted } = detailOf(row({
    foundState: 'unasked', foundCount: 3, dbKey: { sessionId: 'sX', stage: 'PlanReview', number: 7 },
  }));

  assert.deepEqual(posted.filter((m) => (m as { command?: string }).command === 'findings'), [{
    type: 'command', command: 'findings', id: 'k1', session: 'sX', stage: 'PlanReview', number: 7,
  }]);
});

test('a round that found nothing SAYS it found nothing', () => {
  const { html } = detailOf(row({ foundState: 'loaded', foundCount: 0, found: [] }));

  assert.match(html, /This round found nothing/);
});

test('a round the database never recorded says that, and is never asked about', () => {
  const { html, posted } = detailOf(row({ foundState: 'absent', origin: 'session' }));

  assert.match(html, /never written down/);
  assert.deepEqual(posted.filter((m) => (m as { command?: string }).command === 'findings'), [],
    'the row already knows; spawning a process to be told so is waste');
});

test('a read that failed offers a retry, and the retry asks again', () => {
  // All three reviewers of the plan round raised this independently: the plan had four states and
  // would have left a killed process spinning for ever.
  const page = open([row({ foundState: 'failed' })]);
  page.click(hit('tr[data-key]', 'k1'));

  assert.match(page.at('rows').innerHTML, /could not be read/);
  assert.match(page.at('rows').innerHTML, /Try again/);

  page.click(hit('[data-retry]', 'k1'));

  assert.equal(page.posted.filter((m) => (m as { command?: string }).command === 'findings').length, 1,
    'a failure is not cached');
});

test('the findings arrive addressed to one row, and are drawn there', () => {
  const page = open([row({ foundState: 'unasked', foundCount: 1 })]);
  page.click(hit('tr[data-key]', 'k1'));
  page.deliver({
    type: 'found',
    id: 'k1',
    state: 'loaded',
    findings: [{
      ordinal: 0, severity: 'Blocking', category: 'Reliability', file: 'src/x.ts', line: 4,
      title: 'the fan rebuilt its buffer', why: 'quadratic', fix: 'append', role: 'Architecture',
      isGating: true, providers: 'codex', resolution: 'accept', reason: '', reRaised: false,
    }],
  });

  assert.match(page.at('rows').innerHTML, /the fan rebuilt its buffer/);
  assert.doesNotMatch(page.at('rows').innerHTML, /Reading what this round found/);
});

test('the totals arrive by a push, because the page is painted before the database is read', () => {
  // The line under the table was embedded in the HTML and never sent again, and the panel stored the
  // totals without ever pushing them — so the whole SQL-counted line stayed empty for ever. (Code
  // round, CodeRabbit.)
  const page = open(many(3), EMPTY_TOTALS);

  assert.equal(page.at('recorded').textContent, '', 'nothing counted yet, so nothing claimed');

  page.deliver({ type: 'totals', totals: TOTALS });

  assert.match(page.at('recorded').textContent, /250 rounds and 3484 findings/);
});

test('a loaded row whose count disagrees with what arrived offers a retry, not an endless wait', () => {
  // The count comes from the list and the sentences from a later read; the database moves between
  // them. Without this the row drew "Reading…" for ever, because ask() refuses a loaded row.
  const page = open([row({ foundState: 'loaded', foundCount: 4, found: [] })]);
  page.click(hit('tr[data-key]', 'k1'));

  assert.match(page.at('rows').innerHTML, /recorded 4 findings, and the read came back with none/);
  assert.match(page.at('rows').innerHTML, /Try again/);

  page.click(hit('[data-retry]', 'k1'));

  assert.equal(page.posted.filter((m) => (m as { command?: string }).command === 'findings').length, 1,
    'and the retry is allowed to act on it');
});

test('a tick that rebuilds every row does not forget the findings one of them already holds', () => {
  // The rows are rebuilt from the session files every five seconds and know nothing about a read
  // somebody made. Without carrying it across, every expanded row would close and ask again.
  const page = open([row({ foundState: 'unasked', foundCount: 1 })]);
  page.click(hit('tr[data-key]', 'k1'));
  page.deliver({
    type: 'found', id: 'k1', state: 'loaded',
    findings: [{ ordinal: 0, severity: 'Minor', title: 'a name could be clearer', why: 'it reads oddly' }],
  });
  page.deliver({ type: 'rows', rows: [row({ foundState: 'unasked', foundCount: 1 })] });

  assert.match(page.at('rows').innerHTML, /a name could be clearer/);
});

// ---------- the Took column says how long the deciding took, too ----------

test('a round nobody has decided shows ONE time in Took, exactly as it always did', () => {
  const page = open([row({ seconds: 130, decideSeconds: null })]);

  const cell = page.at('rows').innerHTML;
  assert.match(cell, /2m 10s/, 'the reviewers ran for 2m 10s');
  assert.doesNotMatch(cell, /deciding/, 'there is no second number, so there is no span for one');
});

test('a decided round shows BOTH times, the deciding one quieter and titled', () => {
  const page = open([row({ seconds: 130, decideSeconds: 300 })]);

  const cell = page.at('rows').innerHTML;
  assert.match(cell, /2m 10s/, 'how long the reviewers ran');
  assert.match(cell, /class="deciding"[^>]*>[^<]*5m 0s/, 'and how long the deciding took, beside it');
  // The title must say what the second number MEASURES. A round somebody came back to after lunch
  // counts the lunch, so calling it "deciding" without qualification would overclaim.
  assert.match(cell, /from the round finishing to its last decision/);
});

test('a deciding time of zero is a real measurement and is shown', () => {
  // Nought seconds is what a caller that resolved in the same second actually did. It is only the
  // UNKNOWN that must not render as zero, and the two are different states.
  const page = open([row({ seconds: 130, decideSeconds: 0 })]);

  assert.match(page.at('rows').innerHTML, /class="deciding"[^>]*>[^<]*0 s/);
});

test('a round whose OWN duration is unknown shows a dash, never a floating middle dot', () => {
  // An interrupted round has no duration of its own, and it can still have been decided. Without a
  // stand-in the cell opened on " · 5m 0s". (Code round, gemini.)
  const page = open([row({ seconds: null, decideSeconds: 300 })]);

  const cell = page.at('rows').innerHTML;
  assert.match(cell, /— <span class="deciding">/, 'the dash stands where the round duration would be');
  assert.doesNotMatch(cell, />\s*<span class="deciding">/, 'and nothing renders a bare dot with nothing before it');
});

// ---------- a round leaves as a file ----------

test('clicking Export posts one export request carrying the ROW, not just its key', () => {
  // The host's copy of the rows is the unfiltered set as of the last tick, and a row somebody
  // selected may already have left it. The request carries what it is about.
  const page = open([row({ key: 'k1', subject: 'the one I picked' })]);

  page.click(hit('[data-export]', 'k1'));

  const sent = page.posted.filter((m) => (m as { command?: string }).command === 'export');
  assert.equal(sent.length, 1);
  const rounds = (sent[0] as { rounds: { key: string; subject: string }[] }).rounds;
  assert.equal(rounds.length, 1);
  assert.equal(rounds[0]!.key, 'k1');
  assert.equal(rounds[0]!.subject, 'the one I picked', 'the whole row travels, not a key to look up');
});

test('clicking Export does NOT also expand the row', () => {
  // The delegated handler ends with `tr[data-key]`, so a button inside a row falls through to it
  // unless its own branch returns first. Exporting a round and opening it are different acts.
  const page = open([row({ key: 'k1', foundCount: 1 })]);

  // Nested, so the handler CAN fall through to the row branch if its own does not return. With a
  // single-selector target the fall-through has nothing to find and the test proves nothing.
  page.click(hitNested({ '[data-export]': 'k1', 'tr[data-key]': 'k1' }));

  assert.equal(
    page.posted.some((m) => (m as { command?: string }).command === 'findings'), false,
    'an expanded row asks for its findings; this one must not have expanded');
});

test('an export for a key no row has sends nothing', () => {
  const page = open([row({ key: 'k1' })]);

  page.click(hit('[data-export]', 'gone'));

  assert.deepEqual(page.posted.filter((m) => (m as { command?: string }).command === 'export'), []);
});

// ---------- a selection leaves as one file ----------

test('ticking a row selects it and does NOT open it', () => {
  const page = open([row({ key: 'k1', foundCount: 1 })]);

  page.click(hitNested({ '[data-pick]': 'k1', 'tr[data-key]': 'k1' }));

  assert.match(page.at('rows').innerHTML, /data-pick="k1" checked/, 'the box is ticked');
  assert.equal(
    page.posted.some((m) => (m as { command?: string }).command === 'findings'), false,
    'and the row did not expand');
});

test('the bulk button is disabled at nothing and names the count once something is picked', () => {
  const page = open([row({ key: 'k1' }), row({ key: 'k2' })]);

  assert.equal(page.at('exportpicked').disabled, true, 'nothing selected, nothing to export');

  page.click(hitNested({ '[data-pick]': 'k1' }));

  assert.equal(page.at('exportpicked').disabled, false);
  assert.match(page.at('exportpicked').textContent, /Export 1 selected/);
});

test('ticking twice unticks', () => {
  const page = open([row({ key: 'k1' })]);

  page.click(hitNested({ '[data-pick]': 'k1' }));
  page.click(hitNested({ '[data-pick]': 'k1' }));

  assert.equal(page.at('exportpicked').disabled, true);
});

test('the bulk button exports every selected round in ONE request', () => {
  const page = open([row({ key: 'k1' }), row({ key: 'k2' }), row({ key: 'k3' })]);

  page.click(hitNested({ '[data-pick]': 'k1' }));
  page.click(hitNested({ '[data-pick]': 'k3' }));
  page.at('exportpicked').heard['click']?.();

  const sent = page.posted.filter((m) => (m as { command?: string }).command === 'export');
  assert.equal(sent.length, 1, 'one request, not one per round');
  const keys = (sent[0] as { rounds: { key: string }[] }).rounds.map((r) => r.key);
  assert.deepEqual(keys.sort(), ['k1', 'k3']);
});

test('the header box selects every MATCHING round, across pages', () => {
  // Paging is a window on a list, and a person ticking the top box means the list.
  const page = open(many(250));

  page.at('pickall').heard['click']?.();

  assert.match(page.at('exportpicked').textContent, /Export 250 selected/);
});

test('the header box unticks the same rows it ticked', () => {
  const page = open(many(30));

  page.at('pickall').heard['click']?.();
  page.at('pickall').heard['click']?.();

  assert.equal(page.at('exportpicked').disabled, true);
});

test('a selection SURVIVES a filter, and the button says how many are out of sight', () => {
  // A selection is a decision, and a filter is a view. Hiding a row does not unmake the decision —
  // but exporting more than you can see without being told would be a surprise.
  const page = open([
    row({ key: 'k1', branch: 'main' }),
    row({ key: 'k2', branch: 'feat/other' }),
  ]);

  page.click(hitNested({ '[data-pick]': 'k1' }));
  page.click(hitNested({ '[data-pick]': 'k2' }));
  page.deliver({ type: 'rows', rows: [row({ key: 'k1', branch: 'main' }), row({ key: 'k2', branch: 'feat/other' })] });
  assert.match(page.at('exportpicked').textContent, /Export 2 selected/);
});

test('a selected round that LEAVES the loaded set is dropped from the selection', () => {
  // Not merely filtered out of view: gone from ROWS entirely. It cannot be exported, so a count
  // including it would be a lie, and a request carrying it would name a round the page forgot.
  const page = open([row({ key: 'k1' }), row({ key: 'k2' })]);

  page.click(hitNested({ '[data-pick]': 'k1' }));
  page.click(hitNested({ '[data-pick]': 'k2' }));
  assert.match(page.at('exportpicked').textContent, /Export 2 selected/);

  page.deliver({ type: 'rows', rows: [row({ key: 'k2' })] });

  assert.match(page.at('exportpicked').textContent, /Export 1 selected/,
    'the round that left is no longer counted');
});

test('a selection reaching past the filter can be cleared in one gesture', () => {
  // Unticking the header box clears only the rows it would tick, so somebody who picked a hundred,
  // filtered to ten and unticked would be left with ninety they cannot see and no way to drop them.
  // (Plan round, gemini.)
  const page = open([row({ key: 'k1' }), row({ key: 'k2' })]);

  page.click(hitNested({ '[data-pick]': 'k1' }));
  page.click(hitNested({ '[data-pick]': 'k2' }));
  assert.equal(page.at('clearpicked').hidden, false, 'the way out appears once there is something to clear');

  page.at('clearpicked').heard['click']?.();

  assert.equal(page.at('exportpicked').disabled, true, 'nothing is selected any more');
  assert.equal(page.at('clearpicked').hidden, true, 'and the control goes away with the selection');
});

test('the clear control is hidden while nothing is selected', () => {
  const page = open([row({ key: 'k1' })]);

  assert.equal(page.at('clearpicked').hidden, true);
});
