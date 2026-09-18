import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DROPPED, KEPT, ReviewPair, UNDECIDED, decision, reviewPageHtml, undecided } from '../bugzReviewPage';
import { Tab } from '../tabStrip';
import { readable } from './readableHtml';

/**
 * The review page, RUN — because a multi-select that renders is not a multi-select that selects.
 *
 * <p>Story 4 taught this at cost: a model picker matched every regex written about it while being
 * wired to nothing, and four gate reviewers found what no markup assertion could. `.agents/PROJECT.md`
 * refuses a new behavioural assertion over page source text for exactly that reason, so the script
 * here is executed and what it POSTS is what gets asserted.</p>
 *
 * <p>The section on what a row SAYS about itself (story 2.1) is the one place this file reads
 * markup, and it stays inside the ruling's own carve-out: rendered values have no program to run,
 * and "a value appearing escaped" is named as legitimate. Each of those assertions was proven to
 * have teeth by deleting the thing it checks and watching it go red.</p>
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
  repoPath: 'D:/repo',
  headSha: 'aaaa111',
  fixSha: 'bbbb222',
  file: 'src/Totals.cs',
  line: 5,
  why: 'it races',
  fix: 'hold the lock',
});

interface Posted {
  readonly type: string;
  readonly keep?: number;
  readonly ids?: number[];
  readonly id?: number;
  readonly open?: boolean;
  readonly delta?: number;
  /** The zoom and tone controls are SHARED, and they post a field this page never fills. */
  readonly field?: string;
}

/**
 * The `<tr>` a pair's controls sit inside — which is what makes `closest` mean anything here.
 *
 * <p>A shim whose elements answer `closest` only about THEMSELVES cannot see the defect this page
 * is most likely to grow: a branch that matches the ROW rather than the control, so that ticking a
 * box also opens the code under it. That is the `roundsLog.ts` failure PROJECT.md records, and a
 * flat shim is green through it.</p>
 */
class Row {
  constructor(readonly key: string) {}

  /** A `<tr>` has no `id` attribute here, and neither do the controls in it. */
  readonly id = '';

  getAttribute(name: string): string | null {
    return name === 'data-row' ? this.key : null;
  }

  closest(selector: string): Row | null {
    return selector === '[data-row]' ? this : null;
  }
}

/** A checkbox the script can find, tick and read back. */
class Box {
  checked = false;

  /** No `id` attribute, like the real one — an `id` here would answer branches it must not. */
  readonly id = '';

  constructor(readonly key: string, private readonly row: Row) {}

  getAttribute(name: string): string | null {
    return name === 'data-pick' ? this.key : null;
  }

  closest(selector: string): Box | Row | null {
    return selector === '[data-pick]' ? this : this.row.closest(selector);
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

/**
 * A filter strip, so the tabs in it have a real ancestor to be found through.
 *
 * <p>Same argument as {@link Row}: the handler reads which strip a press came from by walking UP
 * from the button, so a shim whose tabs answer only about themselves would be green through a page
 * that rendered both strips without saying which was which — and the host would then narrow by
 * whichever axis it guessed. A tab is built from the rendered markup with the wrapper it was
 * actually inside.</p>
 */
class Strip {
  constructor(readonly which: string) {}

  readonly id = '';

  getAttribute(name: string): string | null {
    return name === 'data-strip' ? this.which : null;
  }

  closest(selector: string): Strip | null {
    return selector === '[data-strip]' ? this : null;
  }
}

/** One tab. It has an `id`, like the real button, and a strip above it. */
class TabButton {
  /** Whether the page put the keyboard on this one. */
  focused = false;

  constructor(readonly id: string, readonly key: string, private readonly strip: Strip) {}

  focus(): void {
    this.focused = true;
  }

  getAttribute(name: string): string | null {
    return name === 'data-tab' ? this.key : null;
  }

  closest(selector: string): TabButton | Strip | null {
    return selector === '[data-tab]' ? this : this.strip.closest(selector);
  }
}

/**
 * A row's disclosure button, which the click handler finds with `closest` — because the press can
 * land on the chevron inside it and an `id` test would miss that entirely.
 */
class Toggle {
  private showing: boolean;

  /** No `id` attribute, like the real button. */
  readonly id = '';

  constructor(readonly key: string, open: boolean, private readonly row: Row) {
    this.showing = open;
  }

  get open(): boolean {
    return this.showing;
  }

  getAttribute(name: string): string | null {
    if (name === 'data-toggle') {
      return this.key;
    }

    return name === 'aria-expanded' ? String(this.showing) : null;
  }

  setAttribute(name: string, value: string): void {
    if (name === 'aria-expanded') {
      this.showing = value === 'true';
    }
  }

  closest(selector: string): Toggle | Row | null {
    return selector === '[data-toggle]' ? this : this.row.closest(selector);
  }
}

/**
 * The descriptive part of a summary line — the severity, the title, the state word.
 *
 * <p>It exists to be CLICKED, and what it answers `closest` with is decided by where the page put
 * it: inside the disclosure button, or merely inside the row beside it. That is the difference
 * between a whole summary line that opens the pair and one where only the method name does.</p>
 */
class Line {
  readonly id = '';

  constructor(readonly key: string, private readonly within: Toggle | Row) {}

  getAttribute(): string | null {
    return null;
  }

  closest(selector: string): Toggle | Row | null {
    return this.within.closest(selector);
  }
}

/** A row's code, hidden until somebody asks for it. */
class Region {
  hidden: boolean;

  constructor(readonly key: string, open: boolean) {
    this.hidden = !open;
  }

  getAttribute(name: string): string | null {
    return name === 'data-detail' ? this.key : null;
  }

  closest(): null {
    return null;
  }
}

/** One button of the ± zoom or ± tone control: it listens for itself rather than being dispatched to. */
class Knob {
  private pressed: (() => void) | undefined;
  readonly dataset: Record<string, string>;

  constructor(kind: string, delta: string) {
    this.dataset = { [kind]: delta };
  }

  addEventListener(kind: string, handler: () => void): void {
    if (kind === 'click') {
      this.pressed = handler;
    }
  }

  press(): void {
    assert.ok(this.pressed !== undefined, 'the control was rendered but never wired');
    this.pressed();
  }
}

/** `document.body.style`, as much of it as the two shared scripts write to. */
class Style {
  fontSize = '';
  color = '';
  readonly custom: Record<string, string> = {};

  setProperty(name: string, value: string): void {
    this.custom[name] = value;
  }
}

/** The host's side of the conversation: what `pushUiScaleTo` / `pushTextToneTo` post into the page. */
class Host {
  private readonly heard: ((event: { data: unknown }) => void)[] = [];

  addEventListener(kind: string, handler: (event: { data: unknown }) => void): void {
    if (kind === 'message') {
      this.heard.push(handler);
    }
  }

  push(data: unknown): void {
    for (const handler of this.heard) {
      handler({ data });
    }
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
  readonly toggles: readonly Toggle[];
  readonly regions: readonly Region[];
  readonly lines: readonly Line[];
  readonly zoom: readonly Knob[];
  readonly tone: readonly Knob[];
  readonly style: Style;
  readonly host: Host;
  readonly controls: Readonly<Record<string, Control>>;
  /** Every tab on the page, with the strip it came out of. */
  readonly tabs: readonly TabButton[];
  click(what: Box | Control | Toggle | Line | TabButton): void;
  /** Whether the pair with this `findingId` is showing its code, as the page currently stands. */
  showing(findingId: number): boolean;
}

/** What the page is drawn with, beyond the pairs — every field optional, as the page has it. */
interface Options {
  readonly projects?: readonly Tab[];
  readonly languages?: readonly Tab[];
  readonly project?: string;
  readonly language?: string;
  readonly focus?: { readonly strip: string; readonly key: string };
  readonly trouble?: string;
  readonly expanded?: ReadonlySet<number>;
  readonly uiScale?: number;
  readonly textTone?: number;
}

/**
 * Run the page over its own rows.
 *
 * <p>Every element is built from the MARKUP THE PAGE RENDERED — the `data-pick` ids, the
 * `data-detail` rows and whether each one came out `hidden`, the `aria-expanded` each toggle was
 * given — not from a list the test invented. A fixture handed to the shim would pass with no
 * checkbox on the page at all, which is the mistake the Bugz section test made first; the same
 * argument makes the collapse tests read their initial state from the page rather than assume it.</p>
 */
function run(pairs: readonly ReviewPair[], options: Options = {}): Page {
  const html = reviewPageHtml({ pairs, nonce: 'test-nonce', ...options });
  // One `<tr>` per pair, and the controls of a pair are INSIDE it — so `closest` walks the same
  // way it does in a browser and a branch that matches the row is visible to these tests.
  const rows = new Map([...html.matchAll(/data-row="(\d+)"/g)].map((m) => [m[1]!, new Row(m[1]!)]));
  const rowFor = (key: string): Row => {
    const found = rows.get(key);
    assert.ok(found !== undefined, `the page rendered a control for pair ${key} outside any row`);

    return found;
  };
  const boxes = [...html.matchAll(/data-pick="(\d+)"/g)].map((m) => new Box(m[1]!, rowFor(m[1]!)));
  // Each strip with its own buttons, from the markup: the wrapper is matched first and its buttons
  // are taken from INSIDE it, so a tab can only be attributed to the strip it was really in.
  const tabs = [...html.matchAll(/<div class="tabs"[^>]*data-strip="(\w+)">([\s\S]*?)<\/div>/g)]
    .flatMap((strip) => {
      const above = new Strip(strip[1]!);

      return [...strip[2]!.matchAll(/id="([^"]+)"[^>]*data-tab="([^"]*)"/g)]
        .map((one) => new TabButton(one[1]!, one[2]!, above));
    });
  const regions = [...html.matchAll(/data-detail="(\d+)"( hidden)?>/g)]
    .map((m) => new Region(m[1]!, m[2] === undefined));
  const toggles = [...html.matchAll(/data-toggle="(\d+)"\s+aria-expanded="(true|false)"/g)]
    .map((m) => new Toggle(m[1]!, m[2] === 'true', rowFor(m[1]!)));
  // The severity/title line and the state word, with the element they are actually INSIDE. Whether
  // that is the disclosure button or merely the row is the whole of the defect a code reviewer
  // found — the first version wrapped only the chevron and the symbol, so pressing the title did
  // nothing — and it is a containment question, which is why the parent is computed from the
  // markup rather than asserted about it.
  const lines = toggles.map((toggle) => {
    const opens = html.indexOf(`data-toggle="${toggle.key}"`);
    const shuts = html.indexOf('</button>', opens);
    const inside = html.slice(opens, shuts);

    return new Line(toggle.key,
      inside.includes('class="said"') && inside.includes('class="state ')
        ? toggle
        : rowFor(toggle.key));
  });
  const zoom = [...html.matchAll(/data-zoom="(-?\d+)"/g)].map((m) => new Knob('zoom', m[1]!));
  const tone = [...html.matchAll(/data-tone="(-?\d+)"/g)].map((m) => new Knob('tone', m[1]!));
  const controls: Record<string, Control> = {
    keep: new Control('keep'),
    drop: new Control('drop'),
    clear: new Control('clear'),
    picked: new Control('picked'),
    pickall: new Control('pickall'),
    expandAll: new Control('expandAll'),
    collapseAll: new Control('collapseAll'),
    zoomOffset: new Control('zoomOffset'),
    toneOffset: new Control('toneOffset'),
  };
  // Only the explanation the page ACTUALLY rendered exists, so a test can ask which one it got
  // without reading the markup for it.
  for (const id of ['nothing', 'trouble']) {
    if (html.includes(`id="${id}"`)) {
      controls[id] = new Control(id);
    }
  }

  const posted: Posted[] = [];
  const style = new Style();
  const host = new Host();
  let onClick: ((event: { target: unknown }) => void) | undefined;

  const byAttribute: Record<string, readonly (Box | Region | Toggle | Knob)[]> = {
    '[data-pick]': boxes,
    '[data-detail]': regions,
    '[data-toggle]': toggles,
    'button[data-zoom]': zoom,
    'button[data-tone]': tone,
  };
  const document = {
    addEventListener: (kind: string, handler: (event: { target: unknown }) => void) => {
      if (kind === 'click') {
        onClick = handler;
      }
    },
    querySelectorAll: (selector: string): readonly unknown[] => byAttribute[selector] ?? [],
    querySelector: (selector: string): Region | Toggle | undefined => {
      const one = /^\[data-(detail|toggle)="(\d+)"]$/.exec(selector);
      assert.ok(one !== null, `the page asked for a selector the shim cannot answer: ${selector}`);
      const among = one[1] === 'detail' ? regions : toggles;

      return (among as readonly (Region | Toggle)[]).find((e) => e.key === one[2]);
    },
    // Tabs as well as controls, because the page looks one up BY ID to give the keyboard back
    // what it was on, and a shim that knew only the controls would be green through that being
    // wired to nothing.
    getElementById: (id: string): Control | TabButton | undefined =>
      controls[id] ?? tabs.find((one) => one.id === id),
    body: { style },
  };

  const body = new Function('acquireVsCodeApi', 'document', 'window', pageScript(html));
  body(
    () => ({ postMessage: (m: Posted) => posted.push(m) }),
    document,
    host,
  );

  assert.ok(onClick !== undefined, 'the page never attached a click listener');

  return {
    posted,
    tabs,
    boxes,
    toggles,
    lines,
    regions,
    zoom,
    tone,
    style,
    host,
    controls,
    click: (what) => onClick!({ target: what }),
    showing: (findingId) => {
      const region = regions.find((r) => r.key === String(findingId));
      assert.ok(region !== undefined, `no pair ${findingId} is on this page`);

      return !region.hidden;
    },
  };
}

/** The toggle for one pair, by the id it belongs to — never by its position in the list. */
function toggleFor(page: Page, findingId: number): Toggle {
  const found = page.toggles.find((t) => t.key === String(findingId));
  assert.ok(found !== undefined, `no pair ${findingId} is on this page`);

  return found;
}

/** The severity/title/state part of one pair's summary line. */
function lineFor(page: Page, findingId: number): Line {
  const found = page.lines.find((l) => l.key === String(findingId));
  assert.ok(found !== undefined, `no pair ${findingId} is on this page`);

  return found;
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

/**
 * Select-all takes every row, and the boxes SAY so.
 *
 * <p>The first version asserted `posted.at(-1)` after a second decision — and after a decision the
 * selection is spent, so a broken select-all posts nothing and `at(-1)` still refers to the earlier
 * message. The test would have stayed green while the control stopped working. A reviewer of the
 * pull request caught it; it is the same shape as every assertion that survives its own break.</p>
 */
test('select-all takes every row, and pressing it again clears them', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  page.click(page.controls['pickall']!);
  assert.deepEqual(page.boxes.map((b) => b.checked), [true, true, true]);
  assert.equal(page.controls['pickall']!.checked, true, 'the box shows what it just did');
  assert.equal(page.controls['keep']!.disabled, false);

  page.click(page.controls['pickall']!);
  assert.deepEqual(page.boxes.map((b) => b.checked), [false, false, false]);
  assert.equal(page.controls['pickall']!.checked, false);
  assert.equal(page.controls['keep']!.disabled, true, 'nothing is selected to decide about');

  // And it still SENDS what it selected.
  page.click(page.controls['pickall']!);
  page.click(page.controls['keep']!);
  assert.deepEqual([...page.posted.at(-1)!.ids!].sort((a, b) => a - b), [1, 2, 3]);
});

/**
 * After a decision, the select-all box is not left ticked over an empty selection.
 *
 * <p>It showed the opposite of what the next click would do: ticked, over nothing selected, so
 * pressing it appeared to CLEAR and actually selected everything.</p>
 */
test('a spent selection leaves the select-all box unticked', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.controls['pickall']!);
  page.click(page.controls['keep']!);

  assert.equal(page.controls['pickall']!.checked, false);
  assert.deepEqual(page.boxes.map((b) => b.checked), [false, false]);
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
// Collapsed by default, and opened one row at a time.
// --------------------------------------------------------------------------------------------

test('every row opens collapsed, and says so where a screen reader can hear it', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  assert.equal(page.regions.length, 3, 'a pair with no detail row has nothing to collapse');
  assert.deepEqual(page.regions.map((r) => r.hidden), [true, true, true]);
  assert.deepEqual(page.toggles.map((t) => t.open), [false, false, false]);
});

test('pressing a row shows its code and tells the panel which row', () => {
  const page = run([pair(1), pair(2)]);

  page.click(toggleFor(page, 2));

  assert.equal(page.showing(2), true, 'the row a person pressed is the row that opened');
  assert.equal(page.showing(1), false, 'and no other row opened with it');
  assert.deepEqual(page.posted.filter((m) => m.type === 'expand'),
    [{ type: 'expand', id: 2, open: true }]);
});

/**
 * The WHOLE summary line opens the pair, not just the method name.
 *
 * <p>The first version of this story put the severity, the title and the state word OUTSIDE the
 * disclosure button while its own stylesheet claimed the line was the button — so pressing any of
 * them did nothing at all, on the part of the row a person is most likely to aim at, since it is
 * the part that says what the defect IS. A code reviewer found it (codex, UX). The shim computes
 * each line's parent from the markup rather than assuming it, so moving those spans back out of
 * the button turns this red.</p>
 */
test('pressing the severity and title opens the pair, like pressing its name', () => {
  const page = run([pair(1), pair(2)]);

  page.click(lineFor(page, 2));

  assert.equal(page.showing(2), true, 'the part of the row that says what the defect is is dead');
  assert.equal(page.showing(1), false);
  assert.deepEqual(page.posted.filter((m) => m.type === 'expand'),
    [{ type: 'expand', id: 2, open: true }]);
});

test('pressing it again closes it, and the panel is told that too', () => {
  const page = run([pair(1)]);

  page.click(toggleFor(page, 1));
  page.click(toggleFor(page, 1));

  assert.equal(page.showing(1), false);
  assert.deepEqual(page.posted.filter((m) => m.type === 'expand').at(-1),
    { type: 'expand', id: 1, open: false });
});

/**
 * Ticking a pair selects it and leaves it shut — the two controls in one row are independent.
 *
 * <p><b>Which mutation this is red for, measured rather than assumed.</b> PROJECT.md's own rule says
 * to ask what an assertion would SEE if the behaviour were deleted, so I asked, by deleting it.</p>
 *
 * <p>The obvious answer was wrong. Removing the early `return` from this page's disclosure branch
 * changes NOTHING — every branch below it compares `target.id`, and neither the twist button nor the
 * checkbox has one, so the fall-through reaches no branch at all: 24 of 24 green both ways. The
 * `return` stays because the house pattern is worth keeping, not because a test defends it, and the
 * `roundsLog.ts` lesson PROJECT.md records does not transfer to this page unaltered.</p>
 *
 * <p>What the suite IS red for, both verified: rendering rows expanded (ten tests, this one
 * included), and widening `closest('[data-toggle]')` to `closest('[data-row]')` — one character's
 * edit, after which the branch reads the expanded state off an element that does not carry it and
 * a second press can never close what the first one opened. That second mutation is why the shim's
 * elements answer `closest` about their ANCESTORS rather than only about themselves; a flat shim
 * is green straight through it.</p>
 */
test('ticking a pair selects it and does not open it', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.boxes[0]!);

  assert.equal(page.showing(1), false, 'the code opened under a box that was only ticked');
  assert.equal(page.posted.filter((m) => m.type === 'expand').length, 0);
  assert.match(page.controls['picked']!.textContent, /1 selected/, 'and the tick still counted');
});

test('opening a row decides nothing and keeps the selection intact', () => {
  const page = run([pair(1), pair(2)]);

  page.click(page.boxes[0]!);
  page.click(toggleFor(page, 1));

  assert.equal(page.posted.filter((m) => m.type === 'decide').length, 0,
    'opening a row is not a decision about it');
  assert.match(page.controls['picked']!.textContent, /1 selected/,
    'the selection survived the row being opened');
  assert.equal(page.controls['keep']!.disabled, false);
});

test('expand all opens every row, and collapse all shuts them', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  page.click(page.controls['expandAll']!);
  assert.deepEqual(page.regions.map((r) => r.hidden), [false, false, false]);
  assert.deepEqual(page.toggles.map((t) => t.open), [true, true, true]);
  assert.deepEqual(page.posted.at(-1), { type: 'expandAll', ids: [1, 2, 3], open: true });

  page.click(page.controls['collapseAll']!);
  assert.deepEqual(page.regions.map((r) => r.hidden), [true, true, true]);
  assert.deepEqual(page.toggles.map((t) => t.open), [false, false, false]);
  assert.deepEqual(page.posted.at(-1), { type: 'expandAll', ids: [1, 2, 3], open: false });
});

// --------------------------------------------------------------------------------------------
// The code is coloured, and a skeleton still cannot become markup once it is IN the page.
// --------------------------------------------------------------------------------------------

test('a pair\'s code arrives coloured by its own language', () => {
  const html = reviewPageHtml({ pairs: [pair(1)], nonce: 'test-nonce', expanded: new Set([1]) });

  assert.match(html, /var\(--coai-hl-token-/u, 'the skeletons reached the page as plain text');
  // And the palette that resolves those variables came with them, or every token is an empty colour.
  assert.match(html, /--coai-hl-token-keyword:/u);
});

/**
 * What differs is marked, and an identical pair is marked nowhere.
 *
 * <p>The marks are counted inside `<tbody>` only: `HIGHLIGHT_CSS` names every `dl-` class in its
 * own rules, so a count over the whole page would find them on a page with no differences at all —
 * which is precisely the assertion that has to be able to fail.</p>
 */
test('the lines that differ are marked, and an identical pair is not', () => {
  const changed = {
    ...pair(1),
    skeletonBefore: 'void method_1()\n{\n    old_1();\n}',
    skeletonAfter: 'void method_1()\n{\n    neu_1();\n    var var_1 = 2;\n}',
  };
  const rowsOf = (html: string): string =>
    html.slice(html.indexOf('<tbody>'), html.indexOf('</tbody>'));

  const marked = rowsOf(reviewPageHtml({
    pairs: [changed], nonce: 'test-nonce', expanded: new Set([1]),
  }));
  assert.match(marked, /class="line dl-added"/u, 'the new line is not marked');
  assert.match(marked, /class="line dl-changed"/u, 'the rewritten line is not marked');

  const identical = rowsOf(reviewPageHtml({
    pairs: [{ ...changed, skeletonAfter: changed.skeletonBefore }],
    nonce: 'test-nonce',
    expanded: new Set([1]),
  }));
  assert.doesNotMatch(identical, /dl-/u, 'a pair where nothing differs must be coloured nowhere');
});

/**
 * The module escapes; this asks whether the PAGE does.
 *
 * <p>`codeHighlight.test.ts` proves `highlight()` cannot emit markup. That is a different claim from
 * "the page cannot", because the page is where the decision was made to stop calling `escapeHtml` on
 * the skeletons — the call moved INTO the highlighter, and a later edit that renders a skeleton
 * anywhere else on this page would reintroduce exactly what was removed. The corpus is code out of
 * somebody's repository, and this page is what a person reads before deciding what leaves the
 * machine, so it is worth asserting at both levels.</p>
 */
test('a skeleton full of markup cannot close the page\'s script or restyle it', () => {
  const nasty = {
    ...pair(1),
    skeletonBefore: 'var a = "</script><style>.pair{display:none}</style>";',
    skeletonAfter: 'var b = "<img src=x onerror=alert(1)>";',
  };
  const html = reviewPageHtml({ pairs: [nasty], nonce: 'test-nonce', expanded: new Set([1]) });
  const rows = html.slice(html.indexOf('<tbody>'), html.indexOf('</tbody>'));

  assert.ok(!rows.includes('</script>'), 'a pair could end the page\'s own script early');
  assert.ok(!/<style[\s>]/iu.test(rows), 'a pair could hide the rows around it');
  assert.ok(!/<img[\s>]/iu.test(rows), 'a pair could add an element to the page');
});

// --------------------------------------------------------------------------------------------
// What a row says about itself (story 2.1): where it was, why, the fix, and how complex.
// --------------------------------------------------------------------------------------------

/**
 * The `about` block of one pair's detail row — what the row says about itself, as markup.
 *
 * <p>Sliced from the detail row the page rendered for THAT id, so a block rendered for the wrong
 * pair, or outside any pair, is a failure here rather than a match elsewhere on the page.</p>
 */
function aboutOf(html: string, findingId: number): string {
  const detail = html.indexOf(`data-detail="${findingId}"`);
  assert.ok(detail >= 0, `no pair ${findingId} is on this page`);
  const opens = html.indexOf('<dl class="about">', detail);
  const shuts = html.indexOf('</dl>', opens);
  assert.ok(opens > detail && shuts > opens, `pair ${findingId} says nothing about itself`);

  return html.slice(opens, shuts);
}

const page = (one: ReviewPair): string =>
  reviewPageHtml({ pairs: [one], nonce: 'test-nonce', expanded: new Set([one.findingId]) });

test('the reviewers\' cause and fix are shown with the code', () => {
  const said = readable(aboutOf(page({ ...pair(1), why: 'it races on the cache', fix: 'hold the lock' }), 1));

  assert.match(said, /it races on the cache/u, 'the cause the reviewers recorded is not shown');
  assert.match(said, /hold the lock/u, 'the fix the reviewers proposed is not shown');
});

/**
 * A finding with nothing recorded says so — it never invents a cause.
 *
 * <p>Counted, not merely matched: "none recorded" has to appear exactly as many times as there are
 * empty fields, or a page that said it for the cause while showing an empty fix would pass.</p>
 */
test('a finding with no cause or fix recorded says so, once per absence', () => {
  const nothing = readable(aboutOf(page({ ...pair(1), why: '', fix: '   ' }), 1));
  assert.equal((nothing.match(/none recorded/gu) ?? []).length, 2);

  const half = readable(aboutOf(page({ ...pair(1), why: 'it races', fix: '' }), 1));
  assert.equal((half.match(/none recorded/gu) ?? []).length, 1);
  assert.match(half, /it races/u);

  const whole = readable(aboutOf(page(pair(1)), 1));
  assert.doesNotMatch(whole, /none recorded/u, 'a finding with both must not claim to lack either');
});

/**
 * Reviewer prose is external data, and it renders as text — the same property the skeletons hold.
 *
 * <p>`why` and `fix` are what a model wrote about somebody's code, and they reach this page through
 * a JSON document. The assertion is the sequence-cannot-appear-AND-the-text-is-still-there pair
 * `codeHighlight.test.ts` uses, for the reason it gives: an escaper that dropped the payload would
 * pass every "cannot appear" line on its own.</p>
 */
test('reviewer prose full of markup renders as text, never as markup', () => {
  const hostile = {
    ...pair(1),
    why: 'because </script><img src=x onerror=alert(1)> races',
    fix: 'wrap it: <style>.pair{display:none}</style> <!-- and hide -->',
  };
  const about = aboutOf(page(hostile), 1);

  assert.ok(!about.includes('</script>'), 'a finding\'s cause could end the page\'s own script');
  assert.ok(!/<img[\s>]/iu.test(about), 'a finding\'s cause could add an element');
  assert.ok(!/<style[\s>]/iu.test(about), 'a finding\'s fix could restyle the page');
  assert.ok(!/<!--/u.test(about), 'a finding\'s fix could comment out the rest of the row');
  // And the words are all still there.
  assert.ok(readable(about).includes('<img src=x onerror=alert(1)> races'), 'the cause itself must survive');
  assert.ok(readable(about).includes('<style>.pair{display:none}</style>'), 'the fix itself must survive');
});

/**
 * The path and the commit are shown as TEXT, the commit abbreviated as git would, and a missing
 * commit is said rather than guessed.
 *
 * <p>Not a link: a link promises "open at this revision", and whether that can be kept is epic 3's
 * revision rule. So the block is asserted to contain no anchor at all — the day one appears, it is
 * a decision that story makes, not something this one drifted into.</p>
 */
test('the path and the short commit are shown as text, and a missing commit says so', () => {
  const placed = page({ ...pair(1), file: 'src/Totals.cs', line: 5, headSha: 'aaaa111bbbb2222', repoPath: 'D:/repo' });
  const about = aboutOf(placed, 1);
  const said = readable(about);

  assert.match(said, /src\/Totals\.cs:5/u, 'the finding\'s path and line are not shown');
  assert.match(said, /at aaaa111\b/u, 'the commit the reviewers read is not shown, short');
  assert.doesNotMatch(said, /aaaa111bbbb2222/u, 'a full sha is a wall, not a label');
  assert.match(said, /D:\/repo/u, 'which checkout it was is not shown');
  assert.ok(!/<a[\s>]/iu.test(about), 'a link here promises what only epic 3 can keep');

  const nowhere = readable(aboutOf(page({ ...pair(1), file: '', line: 0, headSha: '', repoPath: '' }), 1));
  assert.match(nowhere, /no file recorded/u);
  assert.match(nowhere, /an unrecorded commit/u);
  assert.doesNotMatch(nowhere, /:0\b/u, 'a line of 0 is "none recorded", not line zero');
});

/**
 * The complexity is the skeleton's, and each side is labelled with the revision it came from.
 *
 * <p>The before skeleton is the method at the commit the reviewers read; the after skeleton is the
 * method at the commit the fix was found in — two different commits. A page labelling both with one
 * sha is confidently wrong about one of them, so the two labels are asserted separately. Verified
 * by mutation: labelling the after side with `headSha` turns this red.</p>
 */
test('the complexity of each side is labelled with the revision its skeleton came from', () => {
  const counted = page({
    ...pair(1),
    skeletonBefore: 'void method_1() { if (var_1) { } }',
    skeletonAfter: 'void method_1() { if (var_1 && var_2) { } else if (var_3) { } }',
    headSha: 'aaaa111',
    fixSha: 'bbbb222',
  });
  const said = readable(aboutOf(counted, 1));

  assert.match(said, /2 at aaaa111/u, 'the before count is not the skeleton\'s, or is not labelled');
  assert.match(said, /4 at bbbb222/u, 'the after count is not the skeleton\'s, or is labelled with the wrong commit');
});

test('a language the count does not read gets the count\'s own sentence, not a zero', () => {
  const said = readable(aboutOf(page({ ...pair(1), language: 'Fortran' }), 1));

  assert.match(said, /not computed/u);
  assert.match(said, /Fortran/u, 'the sentence must name the language');
  assert.doesNotMatch(said, /\b0 at\b/u, 'a count that could not be taken must not render as zero');
});

// --------------------------------------------------------------------------------------------
// The key is the findingId. These three are the tests a positional key passes anyway.
// --------------------------------------------------------------------------------------------

/**
 * A redraw that REORDERS keeps the open row open — the same row.
 *
 * <p>Every decision redraws, and a redraw reads the server afresh; nothing promises the rows come
 * back in the order they went out. An index would re-open whatever now sits in that position, which
 * is worse than forgetting: it shows somebody else's method under the heading they opened.</p>
 */
test('a redraw in a different order re-opens the same pair, not the same position', () => {
  const before = run([pair(1), pair(2), pair(3)], { expanded: new Set([3]) });
  assert.deepEqual(before.regions.map((r) => r.key), ['1', '2', '3']);
  assert.equal(before.showing(3), true);

  const after = run([pair(3), pair(1), pair(2)], { expanded: new Set([3]) });

  assert.deepEqual(after.regions.map((r) => r.key), ['3', '1', '2'], 'the fixture really did reorder');
  assert.equal(after.showing(3), true, 'the pair that was open is the pair that is open');
  assert.equal(after.showing(1), false);
  assert.equal(after.showing(2), false);
});

test('a redraw without the open pair opens nothing in its place', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([3]) });

  assert.deepEqual(page.regions.map((r) => r.hidden), [true, true],
    'an id that is no longer on the page must not open the row that took its slot');
});

test('two pairs with the same title are still two pairs', () => {
  const twins = [
    { ...pair(1), title: 'a race', symbolName: 'Same' },
    { ...pair(2), title: 'a race', symbolName: 'Same' },
  ];
  const page = run(twins);

  page.click(toggleFor(page, 2));

  assert.equal(page.showing(2), true);
  assert.equal(page.showing(1), false, 'they are told apart by id, not by what they say');
});

// --------------------------------------------------------------------------------------------
// Zoom and tone, with a collapsed row present — the two features have to coexist, not merely
// each work on a page where the other is absent.
// --------------------------------------------------------------------------------------------

test('the zoom control posts a press and applies what the host pushes back', () => {
  const page = run([pair(1)], { uiScale: 2 });

  assert.equal(page.zoom.length, 2, 'the ± pair is on the page');
  page.zoom.find((k) => k.dataset['zoom'] === '1')!.press();
  assert.deepEqual(page.posted.filter((m) => m.type === 'zoom').at(-1),
    { type: 'zoom', delta: 1, field: '' });

  page.host.push({ type: 'uiScale', px: 17.29, label: '+3' });

  assert.equal(page.style.fontSize, '17.29px');
  assert.equal(page.controls['zoomOffset']!.textContent, '+3');
  assert.equal(page.showing(1), false, 'and the row is still collapsed underneath it');
});

test('the tone control posts a press and applies both colours the host pushes back', () => {
  const page = run([pair(1)], { textTone: -1 });

  assert.equal(page.tone.length, 2);
  page.tone.find((k) => k.dataset['tone'] === '-1')!.press();
  assert.deepEqual(page.posted.filter((m) => m.type === 'tone').at(-1),
    { type: 'tone', delta: -1, field: '' });

  page.host.push({ type: 'textTone', color: 'rgb(1 2 3)', read: 'rgb(4 5 6)', label: '−2' });

  assert.equal(page.style.custom['--coai-text'], 'rgb(1 2 3)');
  // BOTH, because the skeletons are `--vscode-editor-foreground` and would otherwise ignore the
  // tone entirely — which is the text somebody dimming their screen at night is reading.
  assert.equal(page.style.custom['--coai-read'], 'rgb(4 5 6)');
  assert.equal(page.controls['toneOffset']!.textContent, '−2');
  assert.equal(page.showing(1), false);
});

test('a row opened by the person survives a tone the host pushes afterwards', () => {
  const page = run([pair(1), pair(2)]);

  page.click(toggleFor(page, 1));
  page.host.push({ type: 'textTone', color: 'rgb(1 2 3)', read: 'rgb(4 5 6)', label: '−2' });

  assert.equal(page.showing(1), true, 'a pushed setting repaints colours, not the disclosure state');
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

/**
 * An empty corpus and a FAILED read are different pages.
 *
 * <p>Driven through the shim rather than matched against the markup — a reviewer of the code round
 * caught me adding a source assertion to the very file whose header explains why they are refused.
 * What is asserted is what the page OFFERS: no rows to tick in either case, and which of the two
 * explanations the person is given.</p>
 */
test('an empty corpus and a failed read are told apart', () => {
  const empty = run([]);
  assert.equal(empty.boxes.length, 0, 'there is nothing to select');
  assert.ok(empty.controls['nothing'] !== undefined, 'the page says the corpus is empty');
  assert.ok(empty.controls['trouble'] === undefined);

  const failed = run([], { trouble: 'the server exited 74' });
  assert.equal(failed.boxes.length, 0);
  assert.ok(failed.controls['trouble'] !== undefined,
    'a read that failed must not be rendered as a corpus that is empty');
  assert.ok(failed.controls['nothing'] === undefined);
});


/** The two strips as a page is handed them — what `reviewTabs` builds, in miniature. */
const PROJECTS: readonly Tab[] = [
  { key: '*all*', label: 'All projects · 2', slug: 'all' },
  { key: 'd:/rsd/repo_a', label: 'repo_a · 1', slug: '0', title: 'd:/rsd/repo_a' },
  { key: 'd:/rsd/repo_b', label: 'repo_b · 1', slug: '1', title: 'd:/rsd/repo_b' },
];
const LANGUAGES: readonly Tab[] = [
  { key: '*all*', label: 'All languages · 2', slug: 'all' },
  { key: 'TypeScript', label: 'TypeScript · 1', slug: '0' },
  { key: 'C#', label: 'C# · 1', slug: '1' },
];

test('pressing a project tab tells the host which strip and which key, and paints nothing itself', () => {
  const page = run([pair(1), pair(2)], { projects: PROJECTS, languages: LANGUAGES, project: '*all*', language: '*all*' });
  const repoB = page.tabs.find((t) => t.key === 'd:/rsd/repo_b');
  assert.ok(repoB !== undefined, 'the project strip was not rendered');

  page.click(repoB);

  // The page announces itself on load, so the tab messages are what is filtered for here.
  assert.deepEqual(page.posted.filter((m) => m.type === 'tab'),
    [{ type: 'tab', strip: 'project', key: 'd:/rsd/repo_b' }]);
  // And the page did NOT filter itself: both rows are still there. The host holds the choice
  // because the document is replaced wholesale on every decision, so a selection living here would
  // die on the first one anybody made.
  assert.equal(page.boxes.length, 2);
});

test('a language press is not mistaken for a project press', () => {
  // The two strips post the same `data-tab`; only the ancestor says which axis was pressed. A
  // handler that read the button alone would narrow by whichever it guessed.
  const page = run([pair(1)], { projects: PROJECTS, languages: LANGUAGES, project: '*all*', language: '*all*' });
  const csharp = page.tabs.find((t) => t.key === 'C#');
  assert.ok(csharp !== undefined);

  page.click(csharp);

  assert.deepEqual(page.posted.filter((m) => m.type === 'tab'),
    [{ type: 'tab', strip: 'language', key: 'C#' }]);
});

test('the two strips have ids of their own and both point at the table', () => {
  const page = run([pair(1)], { projects: PROJECTS, languages: LANGUAGES, project: 'd:/rsd/repo_a', language: '*all*' });

  assert.deepEqual(page.tabs.map((t) => t.id),
    ['project-tab-all', 'project-tab-0', 'project-tab-1', 'language-tab-all', 'language-tab-0', 'language-tab-1'],
    'two strips on one page must not share an id, or the aria wiring points at the wrong thing');
});

test('a page with nothing to choose between renders no strip at all', () => {
  // The live corpus today: nine pairs, one project. `reviewTabs` hands over empty strips and the
  // page must then draw no tablist — an empty one announces a control a person cannot use.
  const page = run([pair(1)]);

  assert.deepEqual(page.tabs, []);
  assert.equal(page.boxes.length, 1, 'and the pairs are still there');
});


test('the keyboard gets back the tab it just activated', () => {
  // Every repaint replaces the document, so a person who tabbed to a project and pressed Enter was
  // returned to the top of a new page and had to navigate the whole thing again to reach the
  // language strip beside it — once per press. Found on the code round.
  const page = run([pair(1)], {
    projects: PROJECTS,
    languages: LANGUAGES,
    project: 'd:/rsd/repo_b',
    language: '*all*',
    focus: { strip: 'project', key: 'd:/rsd/repo_b' },
  });

  const focused = page.tabs.filter((one) => one.focused);

  assert.deepEqual(focused.map((one) => one.id), ['project-tab-1'],
    'exactly the activated tab, found by the key the host named rather than by position');
});

test('a draw nobody pressed does not move anybody\'s focus', () => {
  // The companion, and it is not symmetry for its own sake: a poll comes back every few seconds,
  // and a page that focused something on each one would take the keyboard away mid-sentence.
  const page = run([pair(1)], { projects: PROJECTS, languages: LANGUAGES, project: '*all*', language: '*all*' });

  assert.deepEqual(page.tabs.filter((one) => one.focused), []);
});

test('a focus the tabs cannot account for is not chased', () => {
  // A held press whose tab is gone — the corpus was recollected under it. Nothing is focused, and
  // nothing throws.
  const page = run([pair(1)], {
    projects: PROJECTS,
    languages: LANGUAGES,
    project: '*all*',
    language: '*all*',
    focus: { strip: 'project', key: 'd:/rsd/deleted' },
  });

  assert.deepEqual(page.tabs.filter((one) => one.focused), []);
});
