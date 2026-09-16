import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DROPPED, KEPT, ReviewPair, UNDECIDED, decision, reviewPageHtml, undecided } from '../bugzReviewPage';

/**
 * The review page, RUN — because a multi-select that renders is not a multi-select that selects.
 *
 * <p>Story 4 taught this at cost: a model picker matched every regex written about it while being
 * wired to nothing, and four gate reviewers found what no markup assertion could. `.agents/PROJECT.md`
 * refuses a new behavioural assertion over page source text for exactly that reason, so the script
 * here is executed and what it POSTS is what gets asserted.</p>
 */

const pair = (findingId: number, keep = UNDECIDED): ReviewPair => ({
  findingId,
  symbolName: `Method${findingId}`,
  language: 'CSharp',
  skeletonBefore: 'method_1() { }',
  skeletonAfter: 'method_1() { lock { } }',
  keep,
  severity: 'Major',
  category: 'Reliability',
  title: 'a race',
});

interface Posted {
  readonly type: string;
  readonly keep?: number;
  readonly ids?: number[];
}

/** A checkbox the script can find, tick and read back. */
class Box {
  checked = false;
  readonly id: string;

  constructor(id: string) {
    this.id = id;
  }

  getAttribute(name: string): string | null {
    return name === 'data-pick' ? this.id : null;
  }

  closest(selector: string): Box | null {
    return selector === '[data-pick]' ? this : null;
  }
}

/** A plain control — a button, the select-all box — found by id. */
class Control {
  disabled = false;
  textContent = '';
  checked = false;

  constructor(readonly id: string) {}

  closest(): null {
    return null;
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
  readonly posted: readonly Posted[];
  readonly boxes: readonly Box[];
  readonly controls: Readonly<Record<string, Control>>;
  click(what: Box | Control): void;
}

/**
 * Run the page over its own rows.
 *
 * <p>The boxes are built from the ROWS THE PAGE RENDERED, by reading the `data-pick` ids out of its
 * markup — not from a list the test invented. A fixture handed to the shim would pass with no
 * checkbox on the page at all, which is the mistake the Bugz section test made first.</p>
 */
function run(pairs: readonly ReviewPair[]): Page {
  const html = reviewPageHtml(pairs, 'test-nonce');
  const ids = [...html.matchAll(/data-pick="(\d+)"/g)].map((m) => m[1]!);
  const boxes = ids.map((id) => new Box(id));
  const controls: Record<string, Control> = {
    keep: new Control('keep'),
    drop: new Control('drop'),
    clear: new Control('clear'),
    picked: new Control('picked'),
    pickall: new Control('pickall'),
  };

  const posted: Posted[] = [];
  let onClick: ((event: { target: unknown }) => void) | undefined;

  const document = {
    addEventListener: (kind: string, handler: (event: { target: unknown }) => void) => {
      if (kind === 'click') {
        onClick = handler;
      }
    },
    querySelectorAll: (selector: string): readonly Box[] =>
      (selector === '[data-pick]' ? boxes : []),
    getElementById: (id: string): Control | undefined => controls[id],
  };

  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('acquireVsCodeApi', 'document', pageScript(html));
  body(
    () => ({ postMessage: (m: Posted) => posted.push(m) }),
    document,
  );

  assert.ok(onClick !== undefined, 'the page never attached a click listener');

  return {
    posted,
    boxes,
    controls,
    click: (what) => onClick!({ target: what }),
  };
}

test('the page script is a program, not a string that looks like one', () => {
  assert.doesNotThrow(() => run([pair(1)]));
});

test('the page renders one row per pair', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  assert.equal(page.boxes.length, 3, 'a row that renders no tick-box cannot be selected');
});

/**
 * Ticking a box SELECTS it — the assertion story 4 says to make.
 *
 * <p>Not that a checkbox is in the markup: that the script, run, treats it as a selection and sends
 * that selection when a decision is pressed.</p>
 */
test('ticking two of three sends exactly those two', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  page.click(page.boxes[0]!);
  page.click(page.boxes[2]!);
  page.click(page.controls['keep']!);

  const decided = page.posted.filter((m) => m.type === 'decide');
  assert.equal(decided.length, 1);
  assert.equal(decided[0]!.keep, KEPT);
  assert.deepEqual([...decided[0]!.ids!].sort((a, b) => a - b), [1, 3]);
});

test('ticking twice unticks, and sends nothing when nothing is selected', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.boxes[0]!);
  page.click(page.boxes[0]!);
  page.click(page.controls['keep']!);

  assert.equal(page.posted.filter((m) => m.type === 'decide').length, 0,
    'a decision about nothing is not a decision');
});

test('the decision buttons are disabled until something is selected', () => {
  const page = run([pair(1)]);

  assert.equal(page.controls['keep']!.disabled, true);

  page.click(page.boxes[0]!);
  assert.equal(page.controls['keep']!.disabled, false);
  assert.match(page.controls['picked']!.textContent, /1 selected/);
});

test('select-all takes every row, and pressing it again clears them', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  page.click(page.controls['pickall']!);
  page.click(page.controls['keep']!);
  assert.deepEqual([...page.posted.at(-1)!.ids!].sort((a, b) => a - b), [1, 2, 3]);

  page.click(page.controls['pickall']!);
  page.click(page.controls['pickall']!);
  page.click(page.controls['drop']!);
  assert.deepEqual([...page.posted.at(-1)!.ids!].sort((a, b) => a - b), [1, 2, 3],
    'off then on selects everything again');
});

test('drop sends the drop decision, not the keep one', () => {
  const page = run([pair(1)]);

  page.click(page.boxes[0]!);
  page.click(page.controls['drop']!);

  assert.equal(page.posted.filter((m) => m.type === 'decide').at(-1)!.keep, DROPPED);
});

test('clear throws the selection away without deciding anything', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.boxes[0]!);
  page.click(page.controls['clear']!);
  page.click(page.controls['keep']!);

  assert.equal(page.posted.filter((m) => m.type === 'decide').length, 0);
});

/**
 * The page does NOT paint the decision itself.
 *
 * <p>It posts and waits to be redrawn from what the database says. A page that painted its own
 * success would be showing a decision that may not have been written — the failure story 4's Collect
 * button had when it was drawn from a flag instead of from a row.</p>
 */
test('the selection is cleared on deciding, and no state is painted as saved', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.boxes[0]!);
  page.click(page.controls['keep']!);

  assert.equal(page.controls['picked']!.textContent, '', 'the selection is spent');
  assert.equal(page.controls['keep']!.disabled, true);
  assert.equal(page.boxes[0]!.checked, false);
});

// --------------------------------------------------------------------------------------------
// The page's pure decisions.
// --------------------------------------------------------------------------------------------

test('a decision reads as a word, and undecided is its own answer', () => {
  assert.equal(decision(KEPT), 'kept');
  assert.equal(decision(DROPPED), 'dropped');
  assert.equal(decision(UNDECIDED), 'undecided');
});

test('what is waiting is what nobody has looked at', () => {
  assert.equal(undecided([pair(1), pair(2, KEPT), pair(3, DROPPED)]), 1);
});

test('an empty corpus says so rather than showing an empty table', () => {
  const html = reviewPageHtml([], 'n');

  assert.match(html, /Nothing has been collected yet/);
  assert.ok(!html.includes('<tbody>'), 'a header over no rows is not an answer');
});
