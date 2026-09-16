import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DbTotals, EMPTY_TOTALS } from '../roundsDb';
import { LogRow, PAGE_SIZE, roundsLogHtml } from '../roundsLog';
import { Element, Rule, couldMatch, painters, stylesheet } from './cssRules';

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
  checked: boolean;
  indeterminate: boolean;
  readonly heard: Record<string, (event?: unknown) => void>;
  addEventListener(kind: string, listener: (event?: unknown) => void): void;
  /** Answered for controls served by SELECTOR, which have no id to be found by. */
  getAttribute(asked: string): string | null;
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
      innerHTML: '', textContent: '', hidden: false, disabled: false, value: '', className: '',
      checked: false, indeterminate: false, heard,
      addEventListener(kind, listener) { heard[kind] = listener; },
      getAttribute: (): string | null => null,
      setAttribute: () => undefined,
      querySelectorAll: () => [],
    };
    elements.set(id, fresh);

    return fresh;
  };

  // CONTROLS THAT HAVE NO ID, served from the markup the page really rendered — and the selector is
  // served BY NAME, with anything unknown a throw rather than an empty list. Answering [] for every
  // selector is how a test passes against a page that found nothing: the loop runs zero times and
  // "the filter did not travel" is true because no filter was ever wired. That is precisely what had
  // happened here — `[data-filter]` had never been served, so the facet handlers had never run in
  // any test on this page.
  const byAttribute = (attribute: string, tag = '[a-z]+'): Stub[] => {
    // A SPACE before the attribute, and the escape is DOUBLED because this is a template literal:
    // a single `\s` in one is just the letter s, and a single `\b` is the BACKSPACE character. Both
    // spellings were written here first, and both made the pattern match nothing at all — which
    // looks exactly like a page that renders no filters.
    const pattern = new RegExp(`<${tag}[^>]*\\s${attribute}="([^"]*)"[^>]*>`, 'g');

    return [...html.matchAll(pattern)].map((found) => {
      const key = `${attribute}:${found[1]}`;
      const stub = at(key);
      stub.getAttribute = (asked: string) => (asked === attribute ? found[1] ?? null : null);

      return stub;
    });
  };
  const serve = (selector: string): Stub[] => {
    if (selector === '[data-filter]') { return byAttribute('data-filter'); }
    if (selector === '[data-tab]') { return byAttribute('data-tab'); }
    if (selector === 'th[data-sort]') { return byAttribute('data-sort', 'th'); }
    const one = /^\[data-filter="([\w-]+)"\]$/.exec(selector);
    if (one !== null) { return byAttribute('data-filter').filter((s) => s.getAttribute('data-filter') === one[1]); }

    throw new Error(
      `the page asked for ${JSON.stringify(selector)}, which this harness does not model — answering [] `
      + 'would let a test pass while the page found nothing. Teach the harness that selector.',
    );
  };

  const clicks: ((event: unknown) => void)[] = [];
  const messages: ((event: unknown) => void)[] = [];
  const posted: unknown[] = [];
  const document_ = {
    getElementById: at,
    querySelectorAll: (selector: string) => serve(selector),
    querySelector: (selector: string) => serve(selector)[0] ?? null,
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

  assert.match(page.at('rows').innerHTML, /data-pick="k1"[^>]*checked/, 'the box is ticked');
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

  // And now FILTER one of them out of view. Without this the test never exercised the rule it is
  // named for — a selection that survives a narrowing filter — and would have passed just as well if
  // filtering had cleared the selection outright. (CodeRabbit, on the pull request.)
  const search = page.at('search');
  search.value = 'feat/other';
  search.heard['input']?.({ target: { value: 'feat/other' } });

  assert.match(page.at('exportpicked').textContent, /Export 2 selected \(1 hidden\)/,
    'a selection is a decision and a filter is a view: narrowing the view must not unpick anything, '
    + 'but exporting rows somebody cannot see has to be said out loud');
  assert.doesNotMatch(page.at('rows').innerHTML, /data-pick="k1"/, 'k1 really is off screen');
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

test('the header box says PARTLY when some of what is shown is selected', () => {
  // An unticked box beside five selected rows says nothing here is picked, which is false.
  const page = open([row({ key: 'k1' }), row({ key: 'k2' })]);

  page.click(hitNested({ '[data-pick]': 'k1' }));

  assert.equal(page.at('pickall').checked, false);
  assert.equal(page.at('pickall').indeterminate, true, 'partly selected');
});

test('the header box is checked and not indeterminate when everything shown is selected', () => {
  const page = open([row({ key: 'k1' }), row({ key: 'k2' })]);

  page.at('pickall').heard['click']?.();

  assert.equal(page.at('pickall').checked, true);
  assert.equal(page.at('pickall').indeterminate, false);
});

/**
 * THE PAGER'S APPEARANCE — issue #297.
 *
 * <p>«кнопки активны, а при нажатии ничего не происходит» — the buttons are active and pressing them
 * does nothing. They are not active: `prev.disabled` and `next.disabled` are set correctly and the
 * four tests above press them and assert the clamping. What is missing is that NOTHING SAYS SO. The
 * button stylesheet had no `:disabled` rule at all, so a disabled control kept the filled colour and
 * the hand cursor — and `button:hover` matched it too, because a browser matches `:hover` on a
 * disabled element and suppresses only the pointer events. A control that invites the pointer, lights
 * up under it and does nothing is the whole report.</p>
 *
 * <p>Asserted against the PARSED stylesheet and a modelled element rather than against its text: a
 * rule keyed on something no button carries satisfies every substring search and paints nothing.</p>
 */

/** A pager button as the page renders it, in each of the two states that matter. */
const LIVE: Element = { tag: 'button', classes: ['secondary'], attrs: {} };
const DEAD: Element = { tag: 'button', classes: ['secondary'], attrs: { disabled: '' } };
const ON_THE_PAGE: readonly Element[] = [{ tag: 'div', classes: ['pager'], attrs: {} }];

function sheet(): Rule[] {
  return stylesheet(roundsLogHtml([], [], 'n0nce', '', '', TOTALS, ''));
}

test('a pager button that cannot be pressed does not look pressable', () => {
  const { matching, unreadable } = painters(sheet(), DEAD, ON_THE_PAGE);
  assert.deepEqual(
    unreadable.filter((rule) => /\bbutton\b|\bpager\b/.test(rule.selector)).map((rule) => rule.selector),
    [],
    'a rule that could paint a pager button is in a form this test cannot read, so the verdict is not trustworthy',
  );
  const dims = matching.filter((rule) => /(^|;)\s*opacity\s*:/.test(rule.body));
  assert.ok(
    dims.length > 0,
    'nothing in the stylesheet dims a disabled button, so a control that cannot be pressed is painted exactly like one that can',
  );
  assert.ok(
    dims.some((rule) => /cursor:\s*default/.test(rule.body)),
    'a disabled button keeps the hand cursor, which is the page inviting a press it will not accept',
  );
});

test('hovering a dead control does not light it up', () => {
  // The half that adding a rule does not fix: `button:hover` stays in the sheet and still MATCHES a
  // disabled button, so a new `:hover:not(:disabled)` rule simply does not apply there and the old
  // one is left unopposed. The hover selectors have to be rewritten, not joined.
  const rules = sheet();
  const onDead = rules.filter(
    (rule) => rule.selector.includes(':hover') && couldMatch(rule.selector, DEAD, ON_THE_PAGE) === true,
  );
  assert.deepEqual(
    onDead.map((rule) => rule.selector), [],
    'a hover rule still reaches a disabled button, so the dead control highlights under the pointer',
  );

  // And the live one still does, or the fix has taken the feedback off every button.
  const onLive = rules.filter(
    (rule) => rule.selector.includes(':hover') && couldMatch(rule.selector, LIVE, ON_THE_PAGE) === true,
  );
  assert.ok(onLive.length > 0, 'no hover rule reaches a button that CAN be pressed any more');
});

test('the pager says which page it is on even when there is only one', () => {
  // Two true statements that together read as a broken pager: both buttons are disabled because
  // today holds less than a page, and the line under them announces that the database has thousands
  // of rounds. The pager has to speak for itself.
  const page = open(
    [row({ key: 'a', startedUtc: '2026-09-16T10:00:00Z' })],
    { rounds: 4211, findings: 8687, accepted: 900, rejected: 300, gating: 120, tokensIn: 1, tokensOut: 2, costUsd: 3 },
  );

  assert.match(
    page.at('pageinfo').textContent, /page 1 of 1/,
    'at one page the pager says nothing about paging, so the footer’s much larger count reads as its own',
  );
});

/**
 * TWO VIEWS OVER ONE TABLE — issue #297.
 *
 * <p>Conversations used to sit in the Rounds list with a `Kind` column telling them apart. The
 * operator asked for them to MOVE, so the tab says which kind and the column is gone.</p>
 *
 * <p>The load-bearing property is ORDER: the view filters before the search, the facets, the sort,
 * the page count and the slice. Filtering after the slice gives the Conversations tab one row out of
 * a page of two hundred rounds with <i>Older</i> enabled onto an empty page, while the counts and
 * the search still speak for both kinds — and a test over a single mixed page passes while that is
 * broken. So every test below crosses a page boundary. (The plan round.)</p>
 */

/** A conversation row, as `chatRows` builds one: no repository, no stage, no findings. */
function chat(over: Partial<LogRow> = {}): LogRow {
  return row({
    key: 'chat:c1:2026-09-05T07:41:00.000Z', kind: 'conversation',
    repoPath: '', repoName: '', branch: '', stage: '', verdict: '', gating: 0, findings: null,
    subject: 'why does the gate refuse', answered: 'codex/gpt-5.5',
    ...over,
  });
}

/** Press a tab the way the delegated handler is reached. */
function toTab(page: Page, which: string): void {
  page.click(hit('[data-tab]', which));
}

/** Type into the search box, which reads the event rather than the element. */
function search(page: Page, text: string): void {
  page.at('search').heard['input']?.({ target: { value: text } });
}

test('the conversations tab shows conversations, and never a round', () => {
  const rows = [...many(PAGE_SIZE + 5), chat({ key: 'chat:a' }), chat({ key: 'chat:b' })];
  const page = open(rows);

  toTab(page, 'conversations');

  assert.match(
    page.at('pageinfo').textContent, /rows 1–2 of 2 · page 1 of 1/,
    'the conversations view is still counting the rounds beside them, so its pager is about a list nobody asked for',
  );
});

test('the rounds tab shows rounds, and never a conversation', () => {
  const rows = [...many(PAGE_SIZE + 5), chat({ key: 'chat:a' }), chat({ key: 'chat:b' })];
  const page = open(rows);

  assert.match(
    page.at('pageinfo').textContent, new RegExp(`of ${PAGE_SIZE + 5} · page 1 of 2`),
    'the rounds view still holds the conversations that were moved out of it',
  );
});

test('a search that matches both kinds answers for the view it is asked in', () => {
  // The case a single-page test cannot see: the counts, not only the first rows, have to be the
  // view's own.
  const rows = [
    ...many(PAGE_SIZE + 5).map((r, i) => ({ ...r, subject: `shared word ${i}` })),
    chat({ key: 'chat:a', subject: 'shared word in a conversation' }),
  ];
  const page = open(rows);
  search(page, 'shared word');

  assert.match(page.at('pageinfo').textContent, new RegExp(`of ${PAGE_SIZE + 5} `), 'the rounds view counted the conversation too');

  toTab(page, 'conversations');
  assert.match(page.at('pageinfo').textContent, /rows 1–1 of 1/, 'the conversations view counted the rounds too');
});

test('switching view goes back to the first page, because it is a different list', () => {
  // BOTH views hold more than one page, deliberately. With a one-page conversations list the clamp
  // in `render` pulls an out-of-range page back to the first one by itself, and this test passed
  // with `firstPage()` deleted — found by deleting it. The clamp is a floor, not the rule.
  const chats = Array.from({ length: PAGE_SIZE + 3 }, (_, i) => chat({ key: `chat:${i}`, number: i + 1 }));
  const page = open([...many(PAGE_SIZE + 5), ...chats]);
  page.at('next').heard['click']?.();
  assert.match(page.at('pageinfo').textContent, /page 2 of 2/, 'the round view did not turn its page');

  toTab(page, 'conversations');

  assert.match(
    page.at('pageinfo').textContent, /page 1 of 2/,
    'a view change kept a page number that belonged to the other list',
  );
});

test('a conversation is not offered an export, and is not counted as a round on screen', () => {
  // One shared table means the tick-boxes, both Export controls and the database footer are all
  // still in the DOM. They are hidden by the view rather than left to be pressed over rows whose
  // findings do not exist. (The plan round.)
  const css = stylesheet(roundsLogHtml([], [], 'n0nce', 'usage', 'spots', TOTALS));
  const inChat: readonly Element[] = [{ tag: 'section', classes: ['view-conversations'], attrs: { id: 'tab-rounds' } }];
  const gone = (id: string): boolean => css.some(
    (rule) => /display:\s*none/.test(rule.body)
      && couldMatch(rule.selector, { tag: 'button', classes: [], attrs: { id } }, inChat) === true,
  );

  assert.ok(gone('exportpicked'), 'the conversations view still offers to export rows that have no findings');
  assert.ok(gone('clearpicked'), 'and still offers to clear a selection it cannot make');
  const footer = css.some((rule) => /display:\s*none/.test(rule.body)
    && couldMatch(rule.selector, { tag: 'div', classes: ['hint'], attrs: { id: 'recorded' } }, inChat) === true);
  assert.ok(footer, 'the conversations view still announces how many ROUNDS the database holds');
});

test('the two views name the same column differently, and neither header is built by script', () => {
  const html = roundsLogHtml([], [], 'n0nce', 'usage', 'spots', TOTALS);
  const head = html.slice(html.indexOf('<thead>'), html.indexOf('</thead>'));
  // THE HEADERS AS CELLS, keyed by the column they sort — not a search of the page for a word.
  // A substring can be found anywhere, including in a tooltip or a comment; a cell is the thing a
  // person reads. (The code round asked for this.)
  const cells = new Map(
    [...head.matchAll(/<th\s[^>]*data-sort="([^"]+)"[^>]*>([\s\S]*?)<\/th>/g)]
      .map((found) => [found[1] ?? '', found[2] ?? '']),
  );
  // A PARSE THAT FOUND NOTHING MUST NOT READ AS A PAGE THAT HAS NOTHING. This pattern was written
  // three times before it worked — a heredoc turned its `\b` into an actual BACKSPACE character, and
  // an empty Map then made every negative assertion below true for the wrong reason.
  assert.ok(cells.size > 10, `the header did not parse into cells, so nothing below is asked: ${cells.size}`);

  assert.ok(!cells.has('kind'), 'the Kind column is back, so the table is one list with a label again');
  assert.equal(
    cells.get('number'),
    '<span class="asRound">Round</span><span class="asTurn">Turn</span>'.replace('asTurn', 'asChat'),
    'a round’s Round is not offered as a conversation’s Turn',
  );
  assert.equal(
    cells.get('answered'),
    '<span class="asRound">Reviewers</span><span class="asChat">Who answered</span>',
    'a round’s Reviewers is not offered as the one model that answered',
  );

  // WHICH of the two a view shows is the stylesheet's, so the header needs no script to relabel.
  const css = stylesheet(html);
  const hides = (klass: string, within: string): boolean => css.some(
    (rule) => /display:\s*none/.test(rule.body)
      && couldMatch(rule.selector, { tag: 'span', classes: [klass], attrs: {} },
        [{ tag: 'section', classes: [within], attrs: {} }]) === true,
  );
  assert.ok(hides('asChat', 'view-rounds'), 'the rounds view shows both labels at once');
  assert.ok(hides('asRound', 'view-conversations'), 'the conversations view shows both labels at once');
});

test('a filter a conversation cannot answer does not travel into its view', () => {
  // The shared table's one real trap, and six reviewers found it. `chatRows` leaves repository,
  // branch, stage and verdict empty on purpose, so a value chosen while looking at rounds matches no
  // conversation at all — and the control that did it is hidden in that view, so nothing on screen
  // would say why the tab was empty.
  //
  // The event carries the STUB as its target, not a literal with a value on it: the handler reads
  // `event.target.getAttribute('data-filter')` to know WHICH facet moved, and a target without one
  // wires nothing. The first version of this test did that, and passed with the fix deleted.
  const page = open([
    row({ key: 'r1', stage: 'code review' }),
    row({ key: 'r2', stage: 'plan review' }),
    row({ key: 'r3', stage: 'plan review' }),
    chat({ key: 'chat:a' }),
  ]);
  const stage = page.at('data-filter:stage');
  stage.value = 'code review';
  stage.heard['change']?.({ target: stage });
  assert.match(
    page.at('pageinfo').textContent, /rows 1–1 of 1 /,
    'the stage filter did not narrow the rounds view, so nothing below is being tested',
  );

  toTab(page, 'conversations');

  assert.match(
    page.at('pageinfo').textContent, /rows 1–1 of 1/,
    'a stage chosen over rounds emptied the conversations tab, with the control that did it hidden',
  );
  assert.equal(stage.value, '', 'the hidden control still shows a choice that is no longer applied');
});

test('the facets a conversation cannot answer are off the screen in its view', () => {
  const css = stylesheet(roundsLogHtml([], [], 'n0nce', 'usage', 'spots', TOTALS));
  const inChat: readonly Element[] = [{ tag: 'section', classes: ['view-conversations'], attrs: { id: 'tab-rounds' } }];
  const hidden = (facet: string): boolean => css.some(
    (rule) => /display:\s*none/.test(rule.body)
      && couldMatch(rule.selector, { tag: 'label', classes: ['facet', `facet-${facet}`], attrs: {} }, inChat) === true,
  );

  for (const facet of ['repoPath', 'branch', 'stage', 'verdict']) {
    assert.ok(hidden(facet), `the conversations view still offers the ${facet} filter, which matches no conversation`);
  }

  // The two a conversation CAN answer stay: it has an outcome and a vendor.
  for (const facet of ['status', 'vendor']) {
    assert.ok(!hidden(facet), `the conversations view hid the ${facet} filter, which it can answer`);
  }
});
