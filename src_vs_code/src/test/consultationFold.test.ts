import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FOLD_AFTER, foldedCell } from '../consultationFold';
import { consultationsHtml, roundsLogHtml } from '../roundsLog';
import { EMPTY_LOG } from '../roundsDb';
import type { DbConsultation, DbLog } from '../roundsDb';

/**
 * The Consultations tab folds each long problem and advice (operator, 2026-09-25: "список
 * консультантов по умолчанию должен быть свернут. а то сильно много листать").
 *
 * <p>One consultation's problem was the whole screen on its own. A fold is the same native
 * `<details>` the page already uses for a round's orders: closed by default, opened by a click, no
 * script needed to open it — and the page's own script keeps an opened one open when a live push
 * replaces the table.</p>
 */

const LONG = 'A design fork I cannot settle by measurement, on a bug I have already root-caused. '.repeat(8).trim();

function consultation(over: Partial<DbConsultation> = {}): DbConsultation {
  return {
    id: 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5',
    callerKind: 'claude',
    repoPath: 'D:/repo',
    branch: 'feat/x',
    vendor: 'codex',
    model: 'gpt-5.6-luna',
    turns: 2,
    status: 'closed',
    reason: '',
    startedUtc: '2026-09-17T09:00:00.000Z',
    endedUtc: '2026-09-17T09:30:00.000Z',
    seconds: 41,
    tokensIn: 40_000,
    tokensOut: 1_100,
    costUsd: 0.15,
    problem: LONG,
    advice: 'your loop stops one short',
    alert: '',
    kind: 'stuck',
    plan: '',
    epics: '',
    ...over,
  };
}

function table(...ones: DbConsultation[]): string {
  const log: DbLog = { ...EMPTY_LOG, consultations: ones, read: true };

  return consultationsHtml(log, [], () => undefined);
}

/** The text between two markers, or '' — enough of a reader for one cell's own markup. */
function between(html: string, open: string, close: string): string {
  const at = html.indexOf(open);

  return at < 0 ? '' : html.slice(at + open.length, html.indexOf(close, at + open.length));
}

test('a long problem is folded, closed, and the whole of it is inside the fold', () => {
  const cell = foldedCell(LONG, 'id-1:problem');

  assert.match(cell, /^<details class="fold" data-fold="id-1:problem">/, 'a long field is not folded at all');
  assert.doesNotMatch(cell, /<details[^>]*\sopen/, 'the fold opens on arrival, which is the scroll this removes');
  assert.ok(cell.includes(`<div class="whole">${LONG}</div>`), 'the whole text is not inside the fold');
});

test('the summary is the first line, never longer than the cap, with … when anything is hidden', () => {
  const firstLineTooLong = `${'x'.repeat(FOLD_AFTER * 3)}\nsecond line`;
  const preview = between(foldedCell(firstLineTooLong, 'k'), '<span class="preview">', '</span>');

  assert.equal(preview, `${'x'.repeat(FOLD_AFTER)}…`, 'the summary of a long first line is not capped');
  assert.equal(between(foldedCell('first line\nsecond', 'k'), '<span class="preview">', '</span>'), 'first line…',
    'a short first line followed by more is not the summary, with … for what it hides');
});

test('a short single-line field stays plain, and an empty one is empty', () => {
  assert.equal(foldedCell('your loop stops one short', 'k'), 'your loop stops one short');
  assert.equal(foldedCell('x'.repeat(FOLD_AFTER), 'k'), 'x'.repeat(FOLD_AFTER), 'a field exactly at the cap was folded');
  assert.equal(foldedCell('', 'k'), '');
});

test('a short field with a line break is folded, because the break hides what follows', () => {
  assert.match(foldedCell('one\ntwo', 'k'), /^<details /);
  // Every line terminator a vendor may send, not only \n. (codex, the code round.)
  for (const brk of ['\r', '\r\n', '\u2028', '\u2029']) {
    assert.match(foldedCell(`one${brk}two`, 'k'), /^<details /, `a break of ${JSON.stringify(brk)} was not seen`);
  }
});

test('a break with nothing after it hides nothing, so it does not fold', () => {
  assert.equal(foldedCell('your loop stops one short\n', 'k'), 'your loop stops one short\n',
    'a field whose only "second line" is empty was folded behind a summary that hides nothing');
});

test('the summary is the first line with words in it, not a blank one', () => {
  // A field that begins with a blank line showed "…" alone as its summary. (Our own code reviewer.)
  const preview = between(foldedCell('\n  \nthe real first line\nmore', 'k'), '<span class="preview">', '</span>');

  assert.equal(preview, 'the real first line…');
});

test('the text and the key are escaped on every road', () => {
  const plain = foldedCell('<img src=x onerror=alert(1)>', 'k');
  const folded = foldedCell(`<script>alert(1)</script>\n${LONG}`, 'a"b<c>:problem');

  assert.ok(!plain.includes('<img'), 'a short field with markup went into the page as markup');
  assert.ok(!folded.includes('<script>'), 'a long field with markup went into the page as markup');
  assert.ok(folded.includes('data-fold="a&quot;b&lt;c&gt;:problem"'), 'the key broke out of its attribute');
});

test('the table folds each long field under its own key, and leaves a short one plain', () => {
  const html = table(consultation({ id: 'c1' }), consultation({ id: 'c2', problem: 'short', advice: LONG }));

  assert.ok(html.includes('data-fold="c1:problem"'), 'the first row\'s long problem is not folded');
  assert.ok(html.includes('data-fold="c2:advice"'), 'the second row\'s long advice is not folded');
  assert.ok(!html.includes('data-fold="c1:advice"'), 'a short advice was folded');
  assert.ok(!html.includes('data-fold="c2:problem"'), 'a short problem was folded');
});

// ------------------------------------------------------------------------------------------------
// The page's own script, RUN — a live push replaces the table, and must not snap an opened fold shut.

interface Fold { readonly key: string; open: boolean }

/** A `#consultations-body` whose folds are rebuilt from the HTML it is given, as the browser would. */
function consultationsBody() {
  let folds: Fold[] = [];
  const decoded = (raw: string) => raw.replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
  const body = {
    get innerHTML() { return ''; },
    set innerHTML(html: string) {
      folds = [...html.matchAll(/<details class="fold" data-fold="([^"]*)"(\sopen)?/g)]
        .map((m) => ({ key: decoded(m[1] ?? ''), open: m[2] !== undefined }));
    },
    querySelectorAll: (selector: string) => (selector.includes('details')
      ? folds.filter((one) => !selector.includes('[open]') || one.open).map((one) => ({
        get open() { return one.open; },
        set open(value: boolean) { one.open = value; },
        getAttribute: (name: string) => (name === 'data-fold' ? one.key : null),
      }))
      : []),
  };

  return { body, folds: () => folds };
}

function runPage() {
  const html = roundsLogHtml([], [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const code = script.slice(script.indexOf('>') + 1);
  const consultations = consultationsBody();
  const posted: unknown[] = [];
  let message: ((event: { data: unknown }) => void) | undefined;
  let click: ((event: { target: unknown }) => void) | undefined;
  const element = (): Record<string, unknown> => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const document_ = {
    addEventListener(kind: string, fn: (event: { target: unknown }) => void) {
      if (kind === 'click') { click = fn; }
    },
    getElementById: (id: string) => (id === 'consultations-body' ? consultations.body : element()),
    querySelectorAll: () => [],
    querySelector: () => element(),
    body: element(),
  };
  const window_ = {
    addEventListener(kind: string, fn: (event: { data: unknown }) => void) {
      if (kind === 'message') { message = fn; }
    },
    matchMedia: () => ({ matches: false, addEventListener() {} }),
  };

  new Function('document', 'window', 'acquireVsCodeApi', code)(
    document_, window_,
    () => ({ postMessage: (one: unknown) => posted.push(one), getState: () => undefined, setState() {} }),
  );
  assert.ok(message && click, 'the page registers no message or click listener, so this test drives nothing');

  return { deliver: (html: string) => message!({ data: { type: 'consultations', html } }), click: click!, posted, folds: consultations.folds };
}

test('a fold the person opened stays open when a live push replaces the table, and a closed one stays closed', () => {
  const page = runPage();
  const tricky = 'a"b<c>';
  page.deliver(table(consultation({ id: tricky }), consultation({ id: 'c2' })));
  const opened = page.folds().find((one) => one.key === `${tricky}:problem`);
  assert.ok(opened, 'the first push drew no fold for the long problem');
  opened.open = true;

  page.deliver(table(consultation({ id: tricky, turns: 3 }), consultation({ id: 'c2', turns: 3 })));

  const after = new Map(page.folds().map((one) => [one.key, one.open]));
  assert.equal(after.get(`${tricky}:problem`), true, 'the push snapped the opened fold shut');
  assert.equal(after.get('c2:problem'), false, 'the push opened a fold nobody opened');
});

test('a click on a fold\'s summary posts nothing', () => {
  // A real chain for `closest` to walk — summary, its details, the cell, the row — each answering the
  // selectors the page's delegated handler asks, so a row or cell that grows a data-* attribute the
  // handler acts on turns this red. A stub answering null to everything could never fail. (Our own
  // code reviewer.)
  const page = runPage();
  const before = page.posted.length;
  const chain: { readonly tag: string; readonly attrs: Readonly<Record<string, string>> }[] = [
    { tag: 'summary', attrs: {} },
    { tag: 'details', attrs: { class: 'fold', 'data-fold': 'c1:problem' } },
    { tag: 'td', attrs: { class: 'what' } },
    { tag: 'tr', attrs: {} },
  ];
  const matches = (one: (typeof chain)[number], selector: string): boolean =>
    selector.split(',').some((part) => {
      const m = /^\s*([a-z]*)((?:\[[\w-]+\])*)\s*$/.exec(part);
      const names = [...(m?.[2] ?? '').matchAll(/\[([\w-]+)\]/g)].map((x) => x[1] ?? '');
      return m !== null && (m[1] === '' || m[1] === one.tag) && names.every((name) => name in one.attrs);
    });
  const summary = { closest: (selector: string) => chain.find((one) => matches(one, selector)) ?? null };

  page.click({ target: summary });

  assert.deepEqual(page.posted.slice(before), [], 'toggling a fold sent the host a command');
});
