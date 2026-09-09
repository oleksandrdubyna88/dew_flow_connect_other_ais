import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DbTotals } from '../roundsDb';
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
    key: 'k1', startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE — the thing', status: 'done', decided: null, verdict: 'proceed', gating: 1,
    findings: 1, seconds: 130, tokensIn: null, tokensOut: null, costUsd: null, costInUsd: null,
    costOutUsd: null, costTotalUsd: null, costIsEstimate: false, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'], reviewers: ['codex/Architecture — done'],
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
