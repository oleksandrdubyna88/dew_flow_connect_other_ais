import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Grouped } from '../notificationsRead';
import { PageState, notificationsPageHtml, sourcesOf, waitingPageHtml } from '../notificationsPage';
import { asInstant, compareRows } from '../pageTables';

/**
 * The notifications page, RUN — because a table that renders is not a table that filters.
 *
 * <p>An operator ruling stands behind this: a source-text assertion once passed a model picker that
 * matched every regex written about it while being wired to nothing, and four gate reviewers found
 * what no markup assertion could. So the shipped script is EXECUTED here and what it does to the
 * DOM is what gets asserted.</p>
 *
 * <p><b>The shim is built FROM the rendered markup</b>, never from a hand-written fixture. A fixture
 * handed to a shim passes with no row on the page at all — which is the second half of the same
 * ruling. And the shim <b>throws on a selector it does not understand</b>: a shim that answered
 * "the first element" to an unsupported selector once had page tests binding to the wrong node and
 * passing, which is worse than no test because it reads like one.</p>
 */

const AT = Date.UTC(2026, 8, 17, 9, 0, 0);

function row(over: Partial<Grouped> = {}): Grouped {
  const when = over.when ?? new Date(AT).toISOString();

  return {
    key: JSON.stringify(['failure', over.code ?? 'a-code', over.subject ?? '']),
    class: 'failure',
    code: 'a-code',
    subject: '',
    source: 'serverSettingsSync',
    when,
    first: when,
    title: 'The settings mirror stood down',
    cure: 'Reload the window',
    ledger: 'extension',
    repeats: 1,
    runs: [{ run: 'run-a', count: 1, ceilinged: false }],
    ratePerMin: undefined,
    read: false,
    ...over,
  };
}

const state = (rows: readonly Grouped[], over: Partial<PageState> = {}): PageState => ({
  rows,
  dataDir: 'V:/connectOtherAis',
  older: false,
  loaded: rows.length,
  generation: 7,
  ...over,
});

/** One element the script can read, write and hide. */
class Node {
  hidden = false;
  value = '';
  disabled = false;
  textContent = '';
  style: Record<string, string> = {};
  readonly children: Node[] = [];
  readonly dataset: Record<string, string>;
  private readonly listeners = new Map<string, Array<() => void>>();
  private readonly attributes = new Map<string, string>();

  constructor(dataset: Record<string, string> = {}, attributes: Record<string, string> = {}) {
    this.dataset = dataset;
    for (const [name, value] of Object.entries(attributes)) {
      this.attributes.set(name, value);
    }
  }

  addEventListener(kind: string, handler: () => void): void {
    this.listeners.set(kind, [...(this.listeners.get(kind) ?? []), handler]);
  }

  fire(kind: string): void {
    for (const handler of this.listeners.get(kind) ?? []) {
      handler();
    }
  }

  setAttribute(name: string, value: string): void {
    this.attributes.set(name, value);
  }

  getAttribute(name: string): string | null {
    return this.attributes.get(name) ?? null;
  }

  querySelector(selector: string): Node | null {
    if (selector === '.sort') {
      return this;
    }
    throw new Error(`the shim does not understand querySelector(${selector})`);
  }

  querySelectorAll(selector: string): readonly Node[] {
    if (selector === 'tbody tr') {
      return this.children;
    }
    throw new Error(`the shim does not understand querySelectorAll(${selector})`);
  }
}

/** Everything the page rendered, read back out of its own markup. */
function shimFor(html: string): {
  readonly html: string;
  readonly document: unknown;
  readonly window: unknown;
  readonly fromTheHost: (message: unknown) => void;
  readonly tabs: readonly Node[];
  readonly sections: readonly Node[];
  readonly heads: readonly Node[];
  readonly byId: Record<string, Node>;
  readonly posted: unknown[];
} {
  const tabs = [...html.matchAll(/<button type="button" role="tab"[^>]*data-tab="([^"]+)"/gu)]
    .map((hit) => new Node({ tab: hit[1] as string }, { 'aria-selected': 'false' }));
  const selected = /<button type="button" role="tab" id="tab-([a-z-]+)"[^>]*aria-selected="true"/u.exec(html);
  for (const tab of tabs) {
    if (tab.dataset['tab'] === selected?.[1]) {
      tab.setAttribute('aria-selected', 'true');
    }
  }

  const sections = [...html.matchAll(/<section (hidden )?id="section-([a-z-]+)"[\s\S]*?(?=<section |<p class="pager")/gu)]
    .map((hit) => {
      const node = new Node({ section: hit[2] as string });
      node.hidden = hit[1] !== undefined;
      for (const cells of [...(hit[0].match(/<tr [\s\S]*?<\/tr>/gu) ?? [])]) {
        const attrs: Record<string, string> = {};
        for (const attr of cells.matchAll(/data-(\w+)="([^"]*)"/gu)) {
          attrs[attr[1] as string] = attr[2] as string;
        }
        const tr = new Node(attrs);
        for (const cell of cells.matchAll(/<td[^>]*data-sort="([^"]*)"/gu)) {
          tr.children.push(new Node({ sort: cell[1] as string }));
        }
        node.children.push(tr);
      }

      return node;
    });

  // The RENDERED aria-sort, not a constant: the page opens sorted by When and the header has to
  // say so, which a shim that hard-coded "none" could never catch.
  const heads = [...html.matchAll(/<th scope="col" data-key="(\w+)" aria-sort="(\w+)"/gu)]
    .map((hit) => new Node({ key: hit[1] as string }, { 'aria-sort': hit[2] as string }));

  const byId: Record<string, Node> = {};
  for (const id of ['find', 'source', 'from', 'to', 'clear', 'prev', 'next', 'where', 'range-note', 'ack-note', 'mark-all', 'notice']) {
    assert.ok(html.includes(`id="${id}"`), `the page did not render #${id}`);
    byId[id] = new Node();
  }

  const posted: unknown[] = [];
  const document = {
    getElementById: (id: string): Node | null => byId[id] ?? null,
    querySelector: (selector: string): Node | null => {
      if (selector === '[role="tab"][aria-selected="true"]') {
        return tabs.find((tab) => tab.getAttribute('aria-selected') === 'true') ?? null;
      }
      const section = /^\[data-section="([a-z-]+)"\]$/u.exec(selector);
      if (section !== null) {
        return sections.find((one) => one.dataset['section'] === section[1]) ?? null;
      }
      throw new Error(`the shim does not understand querySelector(${selector})`);
    },
    querySelectorAll: (selector: string): readonly Node[] => {
      if (selector === '[role="tab"]') {
        return tabs;
      }
      if (selector === '[data-section]') {
        return sections;
      }
      if (selector === 'th[data-key]') {
        return heads;
      }
      if (selector === '[data-section]:not([hidden]) th') {
        return heads;
      }
      throw new Error(`the shim does not understand querySelectorAll(${selector})`);
    },
  };

  // The host answers the page through `window.addEventListener('message', …)`, so the shim has to
  // be able to deliver one — an acknowledgement the disk refused arrives this way and nothing else.
  const heard: Array<(event: { data: unknown }) => void> = [];
  const window = {
    addEventListener: (kind: string, handler: (event: { data: unknown }) => void): void => {
      if (kind !== 'message') {
        throw new Error(`the shim does not understand window.addEventListener(${kind})`);
      }
      heard.push(handler);
    },
  };
  const fromTheHost = (message: unknown): void => {
    for (const handler of heard) {
      handler({ data: message });
    }
  };

  return { html, document, window, fromTheHost, tabs, sections, heads, byId, posted };
}

/** The page, rendered and RUN. */
function run(pageState: PageState): ReturnType<typeof shimFor> {
  const html = notificationsPageHtml(pageState, 'a-nonce');
  const shim = shimFor(html);
  const script = /<script nonce="a-nonce">([\s\S]*?)<\/script>/u.exec(html)?.[1];
  assert.ok(script !== undefined, 'the page rendered no script');

  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('document', 'window', 'acquireVsCodeApi', script);
  body(shim.document, shim.window, () => ({ postMessage: (m: unknown) => shim.posted.push(m) }));

  return shim;
}

const shown = (shim: ReturnType<typeof shimFor>, tab: string): readonly Node[] =>
  (shim.sections.find((one) => one.dataset['section'] === tab)?.children ?? []).filter((tr) => !tr.hidden);

test('the page renders its rows and the script shows them', () => {
  const shim = run(state([row({ code: 'one' }), row({ code: 'two', subject: 'V:/x' })]));

  assert.equal(shown(shim, 'failure').length, 2, 'both rows are visible after the first draw');
  assert.match(shim.byId['where']?.textContent ?? '', /Page 1 of 1 — 2 row\(s\)/u);
});

test('a search narrows the rows, and says so when nothing matches', () => {
  const shim = run(state([
    row({ code: 'one', title: 'the mirror stood down' }),
    row({ code: 'two', subject: 'V:/x', title: 'a role was refused' }),
  ]));

  const find = shim.byId['find'] as Node;
  find.value = 'mirror';
  find.fire('input');
  assert.equal(shown(shim, 'failure').length, 1, 'one row survives the search');

  find.value = 'nothing on any row';
  find.fire('input');
  assert.equal(shown(shim, 'failure').length, 0);
  assert.equal(shim.byId['where']?.textContent, 'No notifications match these filters.');
});

test('the search reaches the MESSAGE, which is the trap the rounds log set once already', () => {
  const shim = run(state([row({ title: 'the settings mirror stood down' })]));

  const find = shim.byId['find'] as Node;
  find.value = 'stood down';
  find.fire('input');

  assert.equal(shown(shim, 'failure').length, 1, 'a log whose search cannot find the sentence on screen is the defect');
});

test('a filter turns the acknowledgement OFF, and says which of the two is happening', () => {
  // Three rows on screen must not claim three thousand were read. The line above the table is how
  // a person knows which it is.
  const shim = run(state([row()]));

  assert.equal(shim.byId['ack-note']?.textContent, 'Opening this page marks the loaded records read.');

  const find = shim.byId['find'] as Node;
  find.value = 'mirror';
  find.fire('input');

  assert.equal(shim.byId['ack-note']?.textContent, 'A filter is on, so nothing is being marked read.');
});

test('a range whose end is before its start filters nothing, and says why', () => {
  // Silently showing an empty table is indistinguishable from "nothing matched".
  const shim = run(state([row()]));

  (shim.byId['from'] as Node).value = '2026-09-17T10:00';
  (shim.byId['to'] as Node).value = '2026-09-17T08:00';
  (shim.byId['to'] as Node).fire('change');

  assert.equal(shim.byId['range-note']?.hidden, false);
  assert.match(shim.byId['range-note']?.textContent ?? '', /before its start/u);
  assert.equal(shown(shim, 'failure').length, 1, 'and the row is still there');
});

test('choosing a tab hides every other section, derived from the markup', () => {
  const shim = run(state([row(), row({ class: 'storm', code: 'a-storm' })]));

  const storm = shim.tabs.find((tab) => tab.dataset['tab'] === 'storm') as Node;
  storm.fire('click');

  assert.equal(storm.getAttribute('aria-selected'), 'true');
  assert.equal(shim.sections.find((one) => one.dataset['section'] === 'storm')?.hidden, false);
  assert.ok(
    shim.sections.filter((one) => one.dataset['section'] !== 'storm').every((one) => one.hidden),
    'every other section is hidden',
  );
});

test('clicking a column sorts, and clicking it again turns it round', () => {
  const shim = run(state([
    row({ code: 'older', when: new Date(AT).toISOString() }),
    row({ code: 'newer', subject: 'V:/x', when: new Date(AT + 60_000).toISOString() }),
  ]));

  const sortOn = (key: string): Node => {
    const th = shim.heads.find((one) => one.dataset['key'] === key) as Node;
    (th.querySelector('.sort') as Node).fire('click');

    return th;
  };

  // The page opens sorted by `when`, newest first, so the FIRST click on that column turns it
  // round rather than re-asserting what is already true. A column nobody has touched sorts
  // descending, because the interesting end of every column here is the big end.
  const when = sortOn('when');
  assert.equal(when.getAttribute('aria-sort'), 'ascending', 'the column already sorted flips');

  const repeats = sortOn('repeats');
  assert.equal(repeats.getAttribute('aria-sort'), 'descending', 'a fresh column starts at the big end');
  assert.equal(when.getAttribute('aria-sort'), 'none', 'and the old one stops claiming to be sorted');
  assert.ok(
    shim.heads.filter((th) => th !== repeats).every((th) => th.getAttribute('aria-sort') === 'none'),
    'exactly one column says it is sorted',
  );
});

test('Clear empties every filter and comes back to the first page', () => {
  const shim = run(state([row(), row({ code: 'two', subject: 'V:/x' })]));

  (shim.byId['find'] as Node).value = 'nothing matches this';
  (shim.byId['find'] as Node).fire('input');
  assert.equal(shown(shim, 'failure').length, 0);

  (shim.byId['clear'] as Node).fire('click');

  assert.equal(shim.byId['find']?.value, '');
  assert.equal(shown(shim, 'failure').length, 2);
});

test('Mark everything read posts once per press, and says it is working', () => {
  const shim = run(state([row()]));
  const button = shim.byId['mark-all'] as Node;

  button.fire('click');

  assert.deepEqual(
    shim.posted.filter((message) => (message as { type: string }).type === 'markAll'),
    [{ type: 'markAll' }],
    'one message per press',
  );
  // Two file reads and two appends against what may be a network share. A button that looks idle
  // while that runs is a button pressed three times. (codex and gemini, the S5 code round.)
  assert.equal(button.disabled, true, 'and it cannot be pressed again while it runs');
  assert.match(button.textContent, /Marking/u);
  assert.match(shim.byId['notice']?.textContent ?? '', /both ledgers/u);
});

test('the host can answer, and the answer is the button becoming pressable again', () => {
  // The terminal half of the same finding. A failed acknowledgement leaves the button disabled for
  // ever unless something re-enables it, and "nothing happened" is the one outcome this whole
  // feature exists to stop showing.
  const shim = run(state([row()]));
  const button = shim.byId['mark-all'] as Node;
  button.fire('click');

  shim.fromTheHost({ type: 'notice', said: 'Some of it could not be written down.' });

  assert.equal(shim.byId['notice']?.textContent, 'Some of it could not be written down.');
  assert.equal(shim.byId['notice']?.hidden, false, 'and it is visible, not a hidden live region');
  assert.equal(button.disabled, false);
  assert.equal(button.textContent, 'Mark everything read');
});

test('a message the page does not understand changes nothing', () => {
  const shim = run(state([row()]));

  shim.fromTheHost(null);
  shim.fromTheHost('a string');
  shim.fromTheHost({ type: 'something-else', said: 'ignore me' });

  assert.equal(shim.byId['notice']?.textContent, '');
});

test('nothing is marked read until the PAGE says it was shown, and it names the draw', () => {
  // The host cannot otherwise know the webview rendered at all, and a `shown` from a page that has
  // since been replaced must not acknowledge the snapshot that replaced it. (Two reviewers, S5.)
  const shim = run(state([row()], { generation: 42 }));

  assert.deepEqual(
    shim.posted,
    [{ type: 'shown', generation: 42, filtered: false }],
    'once, naming its own generation, unfiltered',
  );
});

test('a filtered page re-offers itself only when the filter has GONE', () => {
  const shim = run(state([row()], { generation: 3 }));
  const find = shim.byId['find'] as Node;

  find.value = 'mirror';
  find.fire('input');

  assert.equal(shim.posted.length, 1, 'a filter being typed offers nothing');

  find.value = '';
  find.fire('input');

  assert.deepEqual(shim.posted[1], { type: 'shown', generation: 3, filtered: false });
});

test('the When column says it is sorted before anybody clicks anything', () => {
  // The page opens sorted by When, newest first. `aria-sort="none"` on every column told a
  // screen-reader user the table was unsorted, and the first click then reversed a direction they
  // had never been told about. (codex, the S5 code round.)
  const shim = run(state([row()]));

  assert.equal(
    shim.heads.find((th) => th.dataset['key'] === 'when')?.getAttribute('aria-sort'),
    'descending',
  );
  assert.ok(
    shim.heads.filter((th) => th.dataset['key'] !== 'when')
      .every((th) => th.getAttribute('aria-sort') === 'none'),
    'and exactly one column claims it',
  );
});

test('a notice the host wrote into the page is on screen from the first paint', () => {
  const html = notificationsPageHtml(state([row()], { notice: 'Everything is marked read.' }), 'n');

  assert.match(html, /Everything is marked read\./u);
  assert.match(html, /id="notice" role="status" aria-live="polite"/u);
});

test('the page names the directory it is reading, and what it is NOT showing', () => {
  // Two data folders exist on this machine. "It is empty" and "you are looking at the other one"
  // are different problems, and a page that does not say which cannot tell them apart.
  // RUN, not matched: a sentence rendered onto a page whose script threw on load is a sentence on a
  // page that does not work, and the markup would look identical. (gemini, the S5 code round.)
  const shim = run(state([row()], { older: true, loaded: 3000 }));

  assert.match(shim.html, /V:\/connectOtherAis/u);
  assert.match(shim.html, /3000 record\(s\) loaded/u);
  assert.match(shim.html, /Older records are in the file/u);
  assert.equal(shown(shim, 'failure').length, 1, 'and the page it says that on is a working one');
});

test('a ledger that could not be read is SAID, never an empty table', () => {
  const html = notificationsPageHtml(state([], { unreadable: 'permission denied' }), 'n');

  assert.match(html, /could not be read: permission denied/u);
  assert.ok(!html.includes('<table>'), 'and no empty table pretending there is nothing to show');
  // And no script: not one element it binds to exists on this page, so it would throw on load and
  // fill the console the NEXT defect would have to be found in. (codex, the S5 code round.)
  assert.ok(!html.includes('<script'), 'a failure page runs nothing');
});

test('the waiting page says which directory it is reading, and runs nothing', () => {
  const html = waitingPageHtml('V:/connectOtherAis', 'n');

  assert.match(html, /Reading <code>V:\/connectOtherAis<\/code>/u);
  assert.ok(!html.includes('<script'), 'there is nothing to bind to yet');
});

test('nothing embedded in the page can end the script tag it lives in', () => {
  // A `</script` inside an embedded function — in a string, in a regex, in a comment — ends the
  // element there and dumps the rest of the page as markup. The page functions are pasted in BY
  // SOURCE, so the guard belongs on the source, and it is cheap enough to run on all of it.
  // (gemini, the S5 code round.)
  const closes = /<\/script/iu;
  for (const embedded of [compareRows, asInstant]) {
    assert.ok(!closes.test(embedded.toString()), `${embedded.name} would close the script element`);
  }

  const html = notificationsPageHtml(state([row({ title: '</script><img src=x>' })]), 'a-nonce');
  const script = /<script nonce="a-nonce">([\s\S]*?)<\/script>/u.exec(html)?.[1] ?? '';

  assert.ok(!closes.test(script), 'and nothing else in the rendered script does either');
  assert.ok(script.length > 500, 'the whole script is there, not a fragment ended early');
});

test('every value that reaches the page is escaped, at every sink', () => {
  // These records are built from a vendor's stderr and a server's response body, which is the
  // definition of untrusted, and all of it goes into a webview. Naming the helper is not using it.
  const nasty = '"><img src=x onerror=alert(1)>';
  const shim = run(state([row({
    title: nasty,
    cure: nasty,
    source: nasty,
    subject: nasty,
    code: nasty,
  })]));

  assert.ok(!shim.html.includes('onerror=alert(1)>'), 'the payload never reaches the page as markup');
  assert.ok(shim.html.includes('&lt;img'), 'it is there, as text');
  // And RUN: a quote that broke out of an attribute would end the row's attribute list, so the row
  // would still be a row and the markup assertions above would still pass. The page working on it
  // is the assertion neither of them can make.
  assert.equal(shown(shim, 'failure').length, 1, 'one row, its attributes intact');
  const find = shim.byId['find'] as Node;
  find.value = 'img';
  find.fire('input');
  assert.equal(shown(shim, 'failure').length, 1, 'and searchable by what it actually says');
});

test('the source facet is derived from the rows, never a list somebody maintains', () => {
  assert.deepEqual(
    sourcesOf([row({ source: 'b' }), row({ source: 'a' }), row({ source: 'b' })]),
    ['a', 'b'],
  );
});
