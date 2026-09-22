import assert from 'node:assert/strict';
import { callsBlock } from '../callsBlock';
import { CALLS, REVISIONS } from '../livePatch';
import { test } from 'node:test';

import { DROPPED, KEPT, UNDECIDED, decision, reviewPageHtml, undecided } from '../bugzReviewPage';
import { ReviewPair } from '../reviewPair';
import { emptyMemory, FileAtRead, remember, stateOf } from '../openAtRevision';
import { RealMethod, RealRead, TOO_OLD_FOR_THE_REAL_METHOD, realView } from '../realMethodView';
import { revisionActions, RevisionState } from '../revisionActions';
import { Tab } from '../tabStrip';
import { readable, unescaped } from './readableHtml';
import { COMMENT_MOST_CHARS } from '../commentContract';
import { LEAVES_THE_MACHINE } from '../reviewComment';

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

/** The calls block as the panel would have rendered it before anything was asked. */
const callsBlockFor = (findingId: number): string => callsBlock(findingId, { phase: 'unasked' });

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
  comment: '',
  sentUtc: '',
  commentLost: '',
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
  /** A `fetchReal` carries the generation the row asked with; the host echoes it back. */
  readonly generation?: string;
  /** A `realText` press says which way the view went. */
  readonly on?: boolean;
  /** A `comment` carries what was in the box. */
  readonly text?: string;
}

/**
 * A pair's comment box, built from the markup the page rendered: its words, and whether it may change.
 *
 * <p>It sits in the DETAIL row, which carries `data-detail` and not `data-row`, so it answers
 * nothing about a pair row or a toggle — pressing it must therefore reach no branch of the click
 * handler, which is what the story's page test proves by pressing it.</p>
 */
class CommentBox {
  value: string;

  readonly id = '';

  constructor(readonly key: string, readonly readOnly: boolean, value: string) {
    this.value = value;
  }

  getAttribute(name: string): string | null {
    return name === 'data-comment' ? this.key : null;
  }

  closest(selector: string): CommentBox | null {
    return selector === '[data-comment]' ? this : null;
  }
}

/** A comment's counter — what the page's script rewrites as a person types. */
class Counter {
  constructor(readonly key: string, public textContent: string, public className: string) {}
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

  /** The attributes the page sets on a control — `aria-pressed` on the real-code toggle. */
  readonly attributes: Record<string, string> = {};

  constructor(readonly id: string) {}

  getAttribute(name: string): string | null {
    return this.attributes[name] ?? null;
  }

  setAttribute(name: string, value: string): void {
    this.attributes[name] = value;
  }

  closest(): null {
    return null;
  }
}

/**
 * One half of a row's code — the skeleton, or the real method — as the page rendered it.
 *
 * <p>Both halves are on the row and the toggle only flips `hidden`; a fetch fills the real one.
 * So a test can see, without reading markup, WHICH text a row is showing and whether a response
 * landed in it. Built from the rendered markup like every other element here, `hidden` and the
 * initial contents included.</p>
 */
class Half {
  hidden: boolean;
  innerHTML: string;

  constructor(readonly key: string, hidden: boolean, contents: string) {
    this.hidden = hidden;
    this.innerHTML = contents;
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

/**
 * One of the two ways out of a row to the code — at its revision, or as it is now — found with
 * `closest` because the press can land on the text inside the button.
 *
 * <p>It sits in the DETAIL row, which carries `data-detail` and not `data-row`, so it answers
 * nothing about a pair row: a click handler that reached for `[data-row]` from here would find
 * nothing, exactly as it would in a browser.</p>
 */
/** What a missing opener means, so the failure names the ACTION rather than the attribute. */
const OFFERS: Readonly<Record<string, string>> = {
  'data-open-at': 'way to open the file at its revision',
  'data-open-current': 'way to open the current file',
  'data-open-tree': 'way to check the commit out in a new window',
  'data-calls': 'way to ask who calls this method',
};

class Opener {
  readonly id = '';

  constructor(readonly kind: 'data-open-at' | 'data-open-current' | 'data-open-tree' | 'data-calls', readonly key: string) {}

  getAttribute(name: string): string | null {
    return name === this.kind ? this.key : null;
  }

  closest(selector: string): Opener | null {
    return selector === `[${this.kind}]` ? this : null;
  }
}

/** The container a row's revision actions live in — what the host's `revisions` answer lands in. */
class Note {
  private text: string;

  /** How many times the page ASSIGNED this container's markup. An identical patch must not. */
  writes = 0;

  constructor(readonly key: string, contents: string) {
    this.text = contents;
  }

  get innerHTML(): string {
    return this.text;
  }

  set innerHTML(html: string) {
    this.writes += 1;
    this.text = html;
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
  /** The containers one row's call answer is painted into. */
  readonly callBoxes: readonly Note[];
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
  /** Every way out of a row to the code, at its revision or as it is now. */
  readonly openers: readonly Opener[];
  click(what: Box | Control | Toggle | Line | TabButton | Opener | CommentBox): void;
  /** One row's opener of one kind — asserting it is there, because a row with none has lost the action. */
  opener(findingId: number, kind: 'data-open-at' | 'data-open-current' | 'data-open-tree' | 'data-calls'): Opener;
  /** The container one row's revision actions were rendered into. */
  revision(findingId: number): Note;
  /** Whether the pair with this `findingId` is showing its code, as the page currently stands. */
  showing(findingId: number): boolean;
  /** The skeleton half of one pair's code. */
  skeleton(findingId: number): Half;
  /** The real-method half of one pair's code. */
  real(findingId: number): Half;
  /** Every comment box on the page, as rendered. */
  readonly comments: readonly CommentBox[];
  /** One pair's comment box — asserting it is there. */
  comment(findingId: number): CommentBox;
  /** One pair's counter, as the page's script last left it. */
  counter(findingId: number): Counter;
  /** The sentence beside one pair's box, as rendered — a VALUE, read off the element. */
  notice(findingId: number): string;
  /** A person typing: the words replace the box's value and the page hears `input`. */
  type(box: CommentBox, text: string): void;
  /** The box losing focus: the page hears `change`. */
  leave(box: CommentBox): void;
  /** Every pause the page is waiting out, run now. */
  pause(): void;
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
  readonly realText?: boolean;
  readonly real?: ReadonlyMap<number, RealRead>;
  readonly revisions?: ReadonlyMap<number, RevisionState>;
  readonly calls?: ReadonlyMap<number, string>;
  readonly draw?: number;
  readonly comments?: ReadonlyMap<number, string>;
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
  // The two halves of each row's code, with what each was rendered holding. The real half's
  // contents run to the end of its element, which is the last thing in the cell.
  const halves = [...html.matchAll(/<div class="(skel|real)" data-(?:skel|real)="(\d+)"( hidden)?>/g)]
    .map((m) => {
      const opens = m.index! + m[0].length;
      const shuts = html.indexOf(m[1] === 'skel' ? '<div class="real"' : '</td>', opens);

      return { which: m[1]!, half: new Half(m[2]!, m[3] !== undefined, html.slice(opens, shuts).trim()) };
    });
  const halfOf = (which: string, key: string): Half | undefined =>
    halves.find((one) => one.which === which && one.half.key === key)?.half;
  const toggles = [...html.matchAll(/data-toggle="(\d+)"\s+aria-expanded="(true|false)"/g)]
    .map((m) => new Toggle(m[1]!, m[2] === 'true', rowFor(m[1]!)));
  // The revision actions, from the markup: each container with what it was rendered holding, and
  // each opener inside one. An opener rendered outside a container is a failure here, as a control
  // outside any row is above.
  const notes = [...html.matchAll(/<dd class="open" data-revision="(\d+)">([\s\S]*?)<\/dd>/g)]
    .map((m) => new Note(m[1]!, m[2]!.trim()));
  const callBoxes = [...html.matchAll(/<dd class="open" data-calls-for="(\d+)">([\s\S]*?)<\/dd>/g)]
    .map((m) => new Note(m[1]!, m[2]!.trim()));
  const openers = [...notes, ...callBoxes].flatMap((note) => [
    ...[...note.innerHTML.matchAll(/data-open-at="(\d+)"/g)].map((m) => new Opener('data-open-at', m[1]!)),
    ...[...note.innerHTML.matchAll(/data-open-current="(\d+)"/g)].map((m) => new Opener('data-open-current', m[1]!)),
    ...[...note.innerHTML.matchAll(/data-open-tree="(\d+)"/g)].map((m) => new Opener('data-open-tree', m[1]!)),
    ...[...note.innerHTML.matchAll(/data-calls="(\d+)"/g)].map((m) => new Opener('data-calls', m[1]!)),
  ]);
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
    realText: new Control('realText'),
  };
  // The toggle's rendered state, read off the markup as every other initial state here is.
  const pressed = /id="realText" aria-pressed="(true|false)"/.exec(html);
  assert.ok(pressed !== null, 'the page rendered no real-code toggle, or one without a state');
  controls['realText']!.setAttribute('aria-pressed', pressed[1]!);
  // Only the explanation the page ACTUALLY rendered exists, so a test can ask which one it got
  // without reading the markup for it.
  for (const id of ['nothing', 'trouble']) {
    if (html.includes(`id="${id}"`)) {
      controls[id] = new Control(id);
    }
  }

  // A comment box with the words it was rendered holding, decoded as a browser would decode them —
  // so a draft or a stored comment that comes back as the box's VALUE is what a person would see.
  const comments = [...html.matchAll(
    /<textarea id="comment-\d+" data-comment="(\d+)" maxlength="\d+" rows="2"( readonly)?>([\s\S]*?)<\/textarea>/g)]
    .map((m) => new CommentBox(m[1]!, m[2] !== undefined, unescaped(m[3]!)));
  const counters = [...html.matchAll(/<span class="(count(?: near)?)" data-count="(\d+)">([^<]*)<\/span>/g)]
    .map((m) => new Counter(m[2]!, m[3]!, m[1]!));
  const notices = new Map([...html.matchAll(/<p class="notice" data-notice="(\d+)">([^<]*)<\/p>/g)]
    .map((m) => [m[1]!, unescaped(m[2]!)] as const));

  const posted: Posted[] = [];
  const style = new Style();
  const host = new Host();
  let onClick: ((event: { target: unknown }) => void) | undefined;
  let onInput: ((event: { target: unknown }) => void) | undefined;
  let onChange: ((event: { target: unknown }) => void) | undefined;
  // The page's pauses, held rather than timed: a test that waited a real second and a half would be
  // slow, and one that raced a real timer would be flaky. `pause()` runs whatever is waiting.
  const pauses = new Map<number, () => void>();
  let nextPause = 1;

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
      } else if (kind === 'input') {
        onInput = handler;
      } else if (kind === 'change') {
        onChange = handler;
      }
    },
    querySelectorAll: (selector: string): readonly unknown[] => byAttribute[selector] ?? [],
    querySelector: (selector: string): Region | Toggle | Half | Note | Counter | undefined => {
      const one = /^\[data-(detail|toggle|skel|real|revision|calls-for|count)="(\d+)"]$/.exec(selector);
      assert.ok(one !== null, `the page asked for a selector the shim cannot answer: ${selector}`);
      if (one[1] === 'count') {
        return counters.find((counter) => counter.key === one[2]);
      }
      if (one[1] === 'skel' || one[1] === 'real') {
        return halfOf(one[1], one[2]!);
      }
      if (one[1] === 'revision') {
        return notes.find((note) => note.key === one[2]);
      }
      if (one[1] === 'calls-for') {
        return callBoxes.find((note) => note.key === one[2]);
      }
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

  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', pageScript(html));
  body(
    () => ({ postMessage: (m: Posted) => posted.push(m) }),
    document,
    host,
    (then: () => void): number => {
      const id = nextPause;
      nextPause += 1;
      pauses.set(id, then);

      return id;
    },
    (id: number | undefined): void => {
      if (id !== undefined) {
        pauses.delete(id);
      }
    },
  );

  assert.ok(onClick !== undefined, 'the page never attached a click listener');

  return {
    callBoxes,
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
    openers,
    click: (what) => onClick!({ target: what }),
    opener: (findingId, kind) => {
      const found = openers.find((one) => one.kind === kind && one.key === String(findingId));
      assert.ok(found !== undefined, `row ${findingId} offers no ${OFFERS[kind]}`);

      return found;
    },
    revision: (findingId) => {
      const found = notes.find((note) => note.key === String(findingId));
      assert.ok(found !== undefined, `pair ${findingId} rendered no revision actions`);

      return found;
    },
    showing: (findingId) => {
      const region = regions.find((r) => r.key === String(findingId));
      assert.ok(region !== undefined, `no pair ${findingId} is on this page`);

      return !region.hidden;
    },
    skeleton: (findingId) => {
      const half = halfOf('skel', String(findingId));
      assert.ok(half !== undefined, `pair ${findingId} rendered no skeleton half`);

      return half;
    },
    real: (findingId) => {
      const half = halfOf('real', String(findingId));
      assert.ok(half !== undefined, `pair ${findingId} rendered no real-method half`);

      return half;
    },
    comments,
    comment: (findingId) => {
      const box = comments.find((one) => one.key === String(findingId));
      assert.ok(box !== undefined, `pair ${findingId} rendered no comment box`);

      return box;
    },
    counter: (findingId) => {
      const counter = counters.find((one) => one.key === String(findingId));
      assert.ok(counter !== undefined, `pair ${findingId} rendered no counter`);

      return counter;
    },
    notice: (findingId) => {
      const said = notices.get(String(findingId));
      assert.ok(said !== undefined, `pair ${findingId} rendered no sentence beside its box`);

      return said;
    },
    type: (box, text) => {
      box.value = text;
      assert.ok(onInput !== undefined, 'the page never listened for typing');
      onInput({ target: box });
    },
    leave: (box) => {
      assert.ok(onChange !== undefined, 'the page never listened for a box being left');
      onChange({ target: box });
    },
    pause: () => {
      const waiting = [...pauses.values()];
      pauses.clear();
      for (const then of waiting) {
        then();
      }
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

// --------------------------------------------------------------------------------------------
// The un-anonymised view (story 2.3): a toggle over what the rows already hold, never a payload.
// --------------------------------------------------------------------------------------------

/** The real method behind `pair(1)`, both sides readable, as `--real-method` would answer it. */
const REAL: RealMethod = {
  findingId: 1,
  language: 'CSharp',
  name: 'GetOrAdd',
  reason: '',
  before: {
    reason: '', className: 'Totals', kind: 'method_declaration', startLine: 7, endLine: 15,
    source: 'public int GetOrAdd(string key, int value)\n{\n    return _items[key];\n}',
  },
  after: {
    reason: '', className: 'Totals', kind: 'method_declaration', startLine: 7, endLine: 18,
    source: 'public int GetOrAdd(string key, int value)\n{\n    lock (_items) { return _items[key]; }\n}',
  },
};

const fetched = (method: RealMethod = REAL): RealRead => ({ ok: true, method });

/** What the page asked the host for, in the order it asked. */
const asked = (page: Page): readonly Posted[] => page.posted.filter((m) => m.type === 'fetchReal');

/** The rows the page asked for, by id, in a fixed order. */
const askedIds = (page: Page): readonly number[] => asked(page).map((m) => m.id ?? -1).sort((a, b) => a - b);

/**
 * The host's answer for one request, exactly as `BugzReviewPanel.answerReal` posts it.
 *
 * <p>`keep` says whether the read REACHED the server: the panel caches those and not a failed
 * process, and the page holds its note on the same rule. Defaulted to true here because that is the
 * ordinary answer; the retry tests pass false explicitly.</p>
 */
function answer(page: Page, request: Posted, view: { shown: boolean; html: string; keep?: boolean }): void {
  page.host.push({
    type: 'real',
    id: request.id,
    generation: request.generation,
    shown: view.shown,
    keep: view.keep ?? true,
    html: view.html,
  });
}

test('the real-code toggle asks for every open row once, and paints nothing itself', () => {
  const page = run([pair(1), pair(2), pair(3)]);
  page.click(toggleFor(page, 1));
  page.click(toggleFor(page, 3));

  page.click(page.controls['realText']!);

  assert.deepEqual(askedIds(page), [1, 3],
    'exactly the open rows are asked for — the closed one would be a git read for nothing');
  assert.ok(asked(page).every((m) => typeof m.generation === 'string' && m.generation.length > 0),
    'every request carries the generation it can be told stale by');
  assert.deepEqual(page.posted.filter((m) => m.type === 'realText').at(-1), { type: 'realText', on: true },
    'the host is told, so the next paint draws the same view');
  assert.equal(page.controls['realText']!.getAttribute('aria-pressed'), 'true');
  // Nothing real has arrived, so the skeleton stays and the row says it is fetching.
  assert.equal(page.skeleton(1).hidden, false, 'the skeleton must not vanish before anything replaces it');
  assert.equal(page.real(1).hidden, false);
  assert.match(page.real(1).innerHTML, /Fetching the real method/u);
  assert.equal(page.real(2).hidden, true, 'a closed row shows nothing of the view');
});

test('a fetched method appears in the row that asked, and the skeleton steps aside', () => {
  const page = run([pair(1), pair(2)]);
  page.click(toggleFor(page, 1));
  page.click(page.controls['realText']!);
  const request = asked(page)[0]!;

  answer(page, request, realView(pair(1), fetched()));

  assert.equal(page.skeleton(1).hidden, true, 'the skeleton makes way for the real text');
  assert.equal(page.real(1).hidden, false);
  // Read as a person reads it: Shiki tokenises `_items` and `[key]` into separate spans, so a
  // regex over the raw markup cannot see the text.
  assert.match(readable(page.real(1).innerHTML), /_items\[key\]/u, 'the real names, not var_1');
  assert.match(readable(page.real(1).innerHTML), /Totals\.GetOrAdd/u, 'and the class the method sits in');
  assert.equal(page.skeleton(2).hidden, false, 'the other row is untouched');
});

test('turning the toggle off restores the skeleton in every open row, at once and without asking', () => {
  const page = run([pair(1), pair(2)]);
  page.click(toggleFor(page, 1));
  page.click(toggleFor(page, 2));
  page.click(page.controls['realText']!);
  for (const request of asked(page)) {
    answer(page, request, realView(pair(request.id!), fetched({ ...REAL, findingId: request.id! })));
  }
  assert.deepEqual([page.skeleton(1).hidden, page.skeleton(2).hidden], [true, true], 'the fixture must have both rows showing real text');
  const before = asked(page).length;

  page.click(page.controls['realText']!);

  assert.deepEqual([page.skeleton(1).hidden, page.skeleton(2).hidden], [false, false],
    'every open row shows its skeleton again');
  assert.deepEqual([page.real(1).hidden, page.real(2).hidden], [true, true]);
  assert.equal(asked(page).length, before, 'turning the view off costs no process');
  assert.equal(page.controls['realText']!.getAttribute('aria-pressed'), 'false');
  assert.deepEqual(page.posted.filter((m) => m.type === 'realText').at(-1), { type: 'realText', on: false });
});

test('a response to a request that is no longer wanted is discarded, not painted', () => {
  const page = run([pair(1)]);
  page.click(toggleFor(page, 1));
  page.click(page.controls['realText']!);
  const stale = asked(page)[0]!;
  page.click(page.controls['realText']!);
  page.click(page.controls['realText']!);
  const wanted = asked(page)[1]!;
  assert.notEqual(stale.generation, wanted.generation, 'the fixture must have asked twice with different generations');

  answer(page, stale, realView(pair(1), fetched()));

  assert.equal(page.skeleton(1).hidden, false, 'an answer to the OLD view must not land in the new one');
  assert.doesNotMatch(readable(page.real(1).innerHTML), /_items/u);

  answer(page, wanted, realView(pair(1), fetched()));

  assert.equal(page.skeleton(1).hidden, true, 'the answer to the request that is still wanted is applied');
});

test('a row collapsed while its method is on the way forgets it asked', () => {
  const page = run([pair(1)]);
  page.click(toggleFor(page, 1));
  page.click(page.controls['realText']!);
  const request = asked(page)[0]!;

  page.click(toggleFor(page, 1));
  answer(page, request, realView(pair(1), fetched()));

  assert.doesNotMatch(readable(page.real(1).innerHTML), /_items/u, 'a late answer is discarded, not stored for a row nobody is looking at');
  assert.equal(page.skeleton(1).hidden, false);
});

test('a response from another draw is discarded, even for the same row', () => {
  const earlier = run([pair(1)], { draw: 1 });
  earlier.click(toggleFor(earlier, 1));
  earlier.click(earlier.controls['realText']!);
  const fromBefore = asked(earlier)[0]!;
  const later = run([pair(1)], { draw: 2 });
  later.click(toggleFor(later, 1));
  later.click(later.controls['realText']!);
  const fromNow = asked(later)[0]!;
  assert.notEqual(fromBefore.generation, fromNow.generation, 'two draws must not compose the same generation');

  answer(later, fromBefore, realView(pair(1), fetched()));

  assert.equal(later.skeleton(1).hidden, false, 'the previous document’s answer must not be painted into this one');
});

test('a row opened while the view is on asks for its method', () => {
  const page = run([pair(1), pair(2)]);
  page.click(page.controls['realText']!);
  assert.deepEqual(asked(page), [], 'nothing is open, so nothing is asked for');

  page.click(toggleFor(page, 2));

  assert.deepEqual(asked(page).map((m) => m.id), [2]);
  assert.match(page.real(2).innerHTML, /Fetching/u);
});

test('expand all with the view on asks for every row, and collapse all forgets them all', () => {
  const page = run([pair(1), pair(2), pair(3)]);
  page.click(page.controls['realText']!);

  page.click(page.controls['expandAll']!);
  assert.deepEqual(askedIds(page), [1, 2, 3]);
  const requests = asked(page);

  page.click(page.controls['collapseAll']!);
  for (const request of requests) {
    answer(page, request, realView(pair(request.id!), fetched({ ...REAL, findingId: request.id! })));
  }

  assert.deepEqual([1, 2, 3].map((id) => page.skeleton(id).hidden), [false, false, false],
    'answers to rows that were collapsed since are discarded');
});

/**
 * The state survives a redraw: a page drawn with the view on and a cached method draws it.
 *
 * <p>This is the tab-switch case and the decision case in one — both replace the document, and the
 * panel hands back what it holds. The row must come up showing the real text without asking for
 * it again: 200 pairs redrawn after every decision must never cost 400 git reads.</p>
 */
test('a redraw with the view on and a cached method shows it without asking again', () => {
  const page = run([pair(1), pair(2)], {
    realText: true, expanded: new Set([1]), real: new Map([[1, fetched()]]),
  });

  assert.equal(page.skeleton(1).hidden, true, 'the cached real text is what the row shows');
  assert.equal(page.real(1).hidden, false);
  assert.match(readable(page.real(1).innerHTML), /_items\[key\]/u);
  assert.deepEqual(asked(page), [], 'a row whose method is already held is not asked for again');
  assert.equal(page.controls['realText']!.getAttribute('aria-pressed'), 'true');
});

test('a redraw with the view on and nothing cached asks for the open rows on load', () => {
  const page = run([pair(1), pair(2), pair(3)], { realText: true, expanded: new Set([1, 3]) });

  assert.deepEqual(askedIds(page), [1, 3]);
  assert.equal(page.skeleton(1).hidden, false, 'the skeleton stays until the real text arrives');
  assert.equal(page.real(2).hidden, true);
});

test('a redraw with the view off draws the skeleton whatever is cached', () => {
  const page = run([pair(1)], { realText: false, expanded: new Set([1]), real: new Map([[1, fetched()]]) });

  assert.equal(page.skeleton(1).hidden, false);
  assert.equal(page.real(1).hidden, true);
  assert.deepEqual(asked(page), []);
});

/**
 * A side that cannot be shown says WHY, and the skeleton stays.
 *
 * <p>Each reason is a different fact — a pruned commit, an overload set, a renamed method — and a
 * person acts on each differently; one word for all of them would send them to the wrong place.
 * With one side real the panes are shown; with neither, the sentence sits above the skeleton.</p>
 */
test('a side that cannot be shown says why, and one real side still shows', () => {
  const half = realView(pair(1), fetched({ ...REAL, after: { ...REAL.after, reason: 'symbol_ambiguous', source: '' } }));

  assert.equal(half.shown, true, 'the before side is real, so the panes are shown');
  assert.match(readable(half.html), /two functions are called GetOrAdd at bbbb222/u);
  assert.match(readable(half.html), /_items\[key\]/u, 'and the side that IS real is shown');

  const none = realView(pair(1), fetched({
    ...REAL,
    before: { ...REAL.before, reason: 'commit_unreachable', source: '' },
    after: { ...REAL.after, reason: 'symbol_gone', source: '' },
  }));

  assert.equal(none.shown, false, 'nothing real, so the skeleton stays');
  assert.match(readable(none.html), /commit aaaa111 is not in the repository any more/u);
  assert.match(readable(none.html), /no function called GetOrAdd is in commit bbbb222 any more/u);

  const page = run([pair(1)], { realText: true, expanded: new Set([1]), real: new Map([[1, fetched({ ...REAL, reason: 'pair_not_found' })]]) });
  assert.equal(page.skeleton(1).hidden, false, 'a whole-pair reason leaves the skeleton on screen');
  assert.equal(page.real(1).hidden, false, 'with the reason above it');
  assert.match(page.real(1).innerHTML, /not in the database any more/u);
});

test('a server too old for the view says to update it, not that the method could not be read', () => {
  const said = readable(realView(pair(1), { ok: false, tooOld: true, why: 'ignored' }).html);

  assert.match(said, /older than the un-anonymised view/u);
  assert.equal(said.includes(TOO_OLD_FOR_THE_REAL_METHOD), true, 'the reader’s sentence and the page’s are one');

  const failed = readable(realView(pair(1), { ok: false, tooOld: false, why: 'the server exited 74' }).html);
  assert.match(failed, /could not be read: the server exited 74/u);
});

test('the real text is escaped like the skeletons are', () => {
  const nasty = fetched({
    ...REAL,
    before: { ...REAL.before, source: 'var a = "</script><style>.pair{display:none}</style>";' },
    after: { ...REAL.after, source: 'var b = "<img src=x onerror=alert(1)>";', className: '<b>Totals</b>' },
  });
  const html = reviewPageHtml({ pairs: [pair(1)], nonce: 'test-nonce', expanded: new Set([1]), realText: true, real: new Map([[1, nasty]]) });
  const rows = html.slice(html.indexOf('<tbody>'), html.indexOf('</tbody>'));

  assert.ok(!rows.includes('</script>'), 'a real method could end the page’s own script early');
  assert.ok(!/<style[\s>]/iu.test(rows), 'a real method could hide the rows around it');
  assert.ok(!/<img[\s>]/iu.test(rows), 'a real method could add an element to the page');
  assert.ok(!/<b>Totals/u.test(rows), 'a class name is text, not markup');
  assert.ok(readable(rows).includes('<img src=x onerror=alert(1)>'), 'and the text is still all there');
});

/**
 * A decision made while the real text is showing carries ids and nothing else.
 *
 * <p>The extension's half of the byte-level guarantee: the page supplies decision IDs and the send
 * projects the stored pair, so nothing the view shows can reach the wire. The server's own test
 * holds the serialised upload byte-identical with the view on and off; this holds the message the
 * page posts to exactly the three fields it has always had.</p>
 */
test('a decision made while the real text is showing carries ids and nothing else', () => {
  const page = run([pair(1)], { realText: true, expanded: new Set([1]), real: new Map([[1, fetched()]]) });
  assert.equal(page.skeleton(1).hidden, true, 'the fixture must have real text on screen');

  page.click(page.boxes[0]!);
  page.click(page.controls['keep']!);

  const decided = page.posted.filter((m) => m.type === 'decide');
  assert.equal(decided.length, 1);
  assert.deepEqual(Object.keys(decided[0]!).sort(), ['ids', 'keep', 'type'],
    'a decision names pairs; it never carries what the page happened to be showing');
  assert.deepEqual(decided[0], { type: 'decide', keep: KEPT, ids: [1] });
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


test('opening many rows at once does not start a process for every one of them', () => {
  // Code round, codex. Expand all with the view ON called `wantReal` for every open row in one
  // loop, so 200 pairs meant 200 `coai-mcp` processes dispatched together, each re-reading two
  // commits out of git. The extension host and the machine both feel that.
  const many = [pair(1), pair(2), pair(3), pair(4), pair(5), pair(6), pair(7), pair(8)];
  const page = run(many);

  page.click(page.controls['realText']!);
  page.click(page.controls['expandAll']!);

  const inFlight = asked(page);
  assert.ok(inFlight.length > 0, 'it must still ask for something');
  assert.ok(inFlight.length <= 4,
    `eight open rows asked for ${inFlight.length} at once; a bounded queue is the point`);
});

test('and the queue drains: answering one starts the next', () => {
  // The companion. A cap that never released its slots would pass the test above and leave six
  // rows saying "Fetching the real method..." for ever.
  const many = [pair(1), pair(2), pair(3), pair(4), pair(5), pair(6), pair(7), pair(8)];
  const page = run(many);

  page.click(page.controls['realText']!);
  page.click(page.controls['expandAll']!);

  const first = asked(page);
  for (const one of first) {
    answer(page, one, { shown: true, html: '<pre>real</pre>' });
  }

  const after = asked(page);
  assert.ok(after.length > first.length,
    'answering the first batch must release its slots, or the rest are never asked for');
});

test('a read that never reached the server can be asked for again', () => {
  // Code round, codex. The panel deliberately does NOT cache a failed process — but the page
  // recorded the note anyway, so `wantReal` saw something held and never asked again. A transport
  // failure became permanent until an unrelated redraw.
  const page = run([pair(1)]);
  page.click(page.controls['realText']!);
  page.click(toggleFor(page, 1));

  const first = asked(page);
  assert.equal(first.length, 1);
  // `keep: false` is what the host says when the process itself failed.
  page.host.push({ type: 'real', id: first[0]!.id, generation: first[0]!.generation, shown: false, keep: false, html: '<p>could not be read</p>' });

  // The person closes the row and opens it again, which is the obvious way to retry.
  page.click(toggleFor(page, 1));
  page.click(toggleFor(page, 1));

  assert.equal(asked(page).length, 2, 'a failure that was never cached must be retryable');
});

// --------------------------------------------------------------------------------------------
// Reaching the code (story 3.1): the file at its revision, and the file as it is now — two
// actions, two guards, and the page paints neither answer itself.
// --------------------------------------------------------------------------------------------

/** What the page asked the host to open, in order. */
const opens = (page: Page, type: 'openAt' | 'openCurrent' | 'openTree'): readonly Posted[] =>
  page.posted.filter((m) => m.type === type);

test('an open row offers the file at its revision and the CURRENT file, and pressing each names the row', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1]) });
  assert.deepEqual(opens(page, 'openAt'), [], 'nothing is probed at paint — 200 rows must not cost 200 processes');

  page.click(page.opener(1, 'data-open-at'));
  page.click(page.opener(1, 'data-open-current'));

  assert.deepEqual(opens(page, 'openAt'), [{ type: 'openAt', id: 1 }]);
  assert.deepEqual(opens(page, 'openCurrent'), [{ type: 'openCurrent', id: 1 }]);
  assert.equal(page.showing(1), true, 'pressing an opener must not close the row it sits in');
  assert.equal(opens(page, 'openAt').length + opens(page, 'openCurrent').length, 2, 'one press, one message');
});

/**
 * The current file is the one that can mislead — it is where a fix would be made, and it may no
 * longer be the code the reviewers read — so it is labelled CURRENT and never presented as "the" file.
 */
/**
 * Story 3.2a's button, pressed rather than read.
 *
 * <p>The page paints no answer here either: it posts, and the host checks out, opens a window and
 * tells the row what to say. What this proves is the click PATH — the delegated listener, the real
 * attribute, the message — which is the half a rendered-markup assertion cannot see.</p>
 */
test('an open row offers checking the commit out, and pressing it names the row', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1]) });
  assert.deepEqual(opens(page, 'openTree'), [], 'a checkout costs minutes; nothing may start one at paint');

  page.click(page.opener(1, 'data-open-tree'));

  assert.deepEqual(opens(page, 'openTree'), [{ type: 'openTree', id: 1 }]);
  assert.equal(page.showing(1), true, 'pressing it must not close the row it sits in');
  assert.equal(opens(page, 'openAt').length, 0, 'and it is not the other action wearing a new name');
});

test('the checkout action says it costs a window and a minute, so the cheap actions stay the obvious ones', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });
  const said = readable(page.revision(1).innerHTML);

  assert.match(said, /Check out/u, 'the third action must be on the row at all');
  assert.match(said, /new window/u, 'a person must know the review page is not being replaced');
});

test('the current file is labelled CURRENT and says it may differ; the revision action names its commit', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });
  const said = readable(page.revision(1).innerHTML);

  assert.match(said, /CURRENT/u, 'the working-tree file must say it is the current one');
  assert.match(said, /may differ/u, 'and that it may not be what the reviewers read');
  assert.match(said, /Open at aaaa111/u, 'the revision action names the commit it opens, short');
  // Each BUTTON's own words: the revision one never says CURRENT, the current one never names the
  // commit — one label carrying both would be exactly the confusion the two buttons exist to end.
  const label = (kind: string): string => {
    const button = new RegExp(`<button[^>]*${kind}[^>]*>([\\s\\S]*?)</button>`, 'u').exec(page.revision(1).innerHTML);
    assert.ok(button !== null, `no ${kind} button was rendered`);

    return readable(button[1] ?? '');
  };
  assert.doesNotMatch(label('data-open-at'), /CURRENT/u);
  assert.doesNotMatch(label('data-open-current'), /aaaa111/u);
});

test('before anything has been asked, the revision action says it will check first', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });

  assert.match(readable(page.revision(1).innerHTML), /not checked yet/u,
    'an offer derived from nothing must say so — 55.7 % of recorded commits are orphaned');
});

/** What the host would post for these rows, from what it remembers — exactly as `tellRevisions` composes it. */
function revisions(memory: ReturnType<typeof emptyMemory>, rows: readonly ReviewPair[]): { readonly type: 'revisions'; readonly items: readonly { id: number; html: string }[] } {
  return { type: 'revisions', items: rows.map((row) => ({ id: row.findingId, html: revisionActions(row, stateOf(memory, row)) })) };
}

const fileAt = (row: ReviewPair, reason: string): FileAtRead =>
  ({ ok: true, file: { findingId: row.findingId, sha: row.headSha, path: row.file, reason, text: '' } });

/**
 * A checkout that is gone is learned ONCE and reaches every row of that repository — one process
 * per repository, not one per row — while the other repository's rows are untouched.
 */
test('a host answer replaces one row\'s actions, and a checkout that is gone takes every row of that repository with it', () => {
  const gone = [pair(1), pair(2)].map((row) => ({ ...row, repoPath: 'D:/gone' }));
  const other = { ...pair(3), repoPath: 'D:/other' };
  const page = run([...gone, other], { expanded: new Set([1, 2, 3]) });
  const learned = remember(emptyMemory(), gone[0]!, fileAt(gone[0]!, 'repo_path_missing'));

  page.host.push(revisions(learned, gone));

  for (const id of [1, 2]) {
    assert.doesNotMatch(page.revision(id).innerHTML, /data-open-at/u, `row ${id} still offers a press that can only fail`);
    assert.match(readable(page.revision(id).innerHTML), /D:\/gone is not a git repository any more/u);
    assert.match(page.revision(id).innerHTML, /data-open-current/u, 'the current file is still offered');
  }
  assert.match(page.revision(3).innerHTML, /data-open-at="3"/u, 'another repository\'s row is untouched');
  assert.deepEqual(opens(page, 'openAt'), [], 'and none of it cost the page a press');
});

test('a page drawn with what the panel remembers says the reasons without asking, and keeps the row open', () => {
  const pruned = remember(emptyMemory(), pair(1), fileAt(pair(1), 'commit_unreachable'));
  const page = run([pair(1), pair(2)], {
    expanded: new Set([1, 2]),
    revisions: new Map([[1, stateOf(pruned, pair(1))], [2, stateOf(pruned, pair(2))]]),
  });

  assert.doesNotMatch(page.revision(1).innerHTML, /data-open-at/u, 'a commit the repository no longer has must not be offered');
  assert.match(readable(page.revision(1).innerHTML), /commit aaaa111 is not in the repository any more/u);
  page.opener(2, 'data-open-at');
  assert.doesNotMatch(readable(page.revision(2).innerHTML), /not checked yet/u, 'the repository answered, so the sibling is a plain offer');
  assert.deepEqual(opens(page, 'openAt'), [], 'nothing is asked at paint');
  assert.equal(page.showing(1), true);
});

test('a file that was not at that path at that revision says which, and still offers the current file', () => {
  const moved = remember(emptyMemory(), pair(1), fileAt(pair(1), 'file_not_in_commit'));
  const page = run([pair(1)], { expanded: new Set([1]), revisions: new Map([[1, stateOf(moved, pair(1))]]) });
  const said = readable(page.revision(1).innerHTML);

  assert.match(said, /src\/Totals\.cs was not at this path at aaaa111/u);
  assert.match(said, /open the current file instead/u);
  page.opener(1, 'data-open-current');
  assert.doesNotMatch(page.revision(1).innerHTML, /data-open-at/u);
});

test('a refusal to open the current file is said on the row, beside the button that is still there', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });

  page.host.push({
    type: 'revisions',
    items: [{ id: 1, html: revisionActions(pair(1), { offered: true, note: '', currentNote: 'D:/repo is not a folder of this workspace, so nothing is opened from it' }) }],
  });

  assert.match(readable(page.revision(1).innerHTML), /not a folder of this workspace/u);
  assert.match(page.revision(1).innerHTML, /data-open-current="1"/u, 'a refused press is not a removed button');
});

test('a revision answer about a row that is not on the page lands nowhere and breaks nothing', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });

  assert.doesNotThrow(() => page.host.push({ type: 'revisions', items: [{ id: 99, html: '<b>x</b>' }] }));
  assert.doesNotMatch(page.revision(1).innerHTML, /<b>x<\/b>/u);
});

test('a path full of markup in the revision actions renders as text', () => {
  const nasty = { ...pair(1), file: '<img src=x onerror=alert(1)>.cs', repoPath: '<style>.pair{display:none}</style>' };
  const html = reviewPageHtml({ pairs: [nasty], nonce: 'test-nonce', expanded: new Set([1]) });
  const rows = html.slice(html.indexOf('<tbody>'), html.indexOf('</tbody>'));

  assert.ok(!/<img[\s>]/iu.test(rows), 'a recorded path could add an element to the page');
  assert.ok(!/<style[\s>]/iu.test(rows), 'a recorded checkout could restyle the page');
});

test('a read that DID reach the server is not asked for twice', () => {
  // The companion: retrying everything would spend a process per open on a pruned commit, whose
  // answer will not change.
  const page = run([pair(1)]);
  page.click(page.controls['realText']!);
  page.click(toggleFor(page, 1));

  const first = asked(page);
  page.host.push({ type: 'real', id: first[0]!.id, generation: first[0]!.generation, shown: false, keep: true, html: '<p>that commit is not in this checkout any more</p>' });

  page.click(toggleFor(page, 1));
  page.click(toggleFor(page, 1));

  assert.equal(asked(page).length, 1, 'a domain answer is held; only a failed process is retried');
});

// --------------------------------------------------------------------------------------------
// Who calls this (story 3.3): the control pressed, and the row it sits in left alone.
// --------------------------------------------------------------------------------------------

test('an open row offers to ask who calls its method, and pressing it names the row', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1]), calls: new Map([[1, callsBlockFor(1)]]) });

  assert.deepEqual(page.posted.filter((m) => m.type === 'calls'), [],
    'a cold language service takes seconds; nothing may ask at paint');

  page.click(page.opener(1, 'data-calls'));

  assert.deepEqual(page.posted.filter((m) => m.type === 'calls'), [{ type: 'calls', id: 1 }]);
});

/**
 * A reviewer asked for the control's propagation to be pinned, and this pins it — but it is worth
 * saying what it does NOT do. Removing the branch's `return` leaves this green, because the control
 * lives in the DETAIL row rather than inside the summary row's toggle button, so there is nothing
 * for the event to bubble into. Measured by taking the `return` out and watching the suite stay
 * green. The `return` stays because it is the house pattern and because the day the control moves
 * into the toggle is the day it matters; the test documents the contract rather than guarding it,
 * and saying so is better than claiming teeth it has not got.
 */
test('pressing it does not also open, close or otherwise disturb the row it sits in', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1]), calls: new Map([[1, callsBlockFor(1)]]) });
  const before = page.showing(1);

  page.click(page.opener(1, 'data-calls'));

  assert.equal(page.showing(1), before, 'the row must be exactly as it was');
  assert.deepEqual(page.posted.filter((m) => m.type === 'expand'), [],
    'and the row toggle must not have been told anything at all');
});

// --------------------------------------------------------------------------------------------
// Live patches, DRIVEN (round 2 of the code round, coderabbit).
// --------------------------------------------------------------------------------------------

test('a call answer is painted into its own row container, and no other', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1, 2]), calls: new Map([[1, callsBlockFor(1)], [2, callsBlockFor(2)]]) });
  const before = page.callBoxes.find((one) => one.key === '2')?.innerHTML;

  page.host.push({ type: 'calls', items: [{ id: 1, html: '<span class="why">2 methods call this</span>' }] });

  assert.match(page.callBoxes.find((one) => one.key === '1')?.innerHTML ?? '', /2 methods call this/u);
  assert.equal(page.callBoxes.find((one) => one.key === '2')?.innerHTML, before,
    'a patch names one row; the rest of the page is not redrawn');
});

test('an IDENTICAL call patch is skipped, so a focused button inside it survives', () => {
  // `.coderabbit.yaml` states the rule for this page: live patches must skip identical HTML. A
  // repeated or superseded completion posts the same block, and replacing innerHTML with the same
  // string still destroys the element the person was on. (Code round 2, coderabbit.)
  const page = run([pair(1)], { expanded: new Set([1]), calls: new Map([[1, callsBlockFor(1)]]) });
  const box = page.callBoxes.find((one) => one.key === '1');
  assert.ok(box !== undefined);

  page.host.push({ type: 'calls', items: [{ id: 1, html: '<span class="why">nothing calls this</span>' }] });
  const painted = box.writes;

  page.host.push({ type: 'calls', items: [{ id: 1, html: '<span class="why">nothing calls this</span>' }] });

  assert.equal(box.writes, painted, 'the same markup a second time must not touch the DOM');
  assert.match(box.innerHTML, /nothing calls this/u, 'and what is on screen is still the answer');
});

test('a revision answer is painted into its own row container, and no other', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([1, 2]) });
  const before = page.revision(2).innerHTML;

  page.host.push({ type: 'revisions', items: [{ id: 1, html: '<span class="why">the checkout is gone</span>' }] });

  assert.match(page.revision(1).innerHTML, /the checkout is gone/u);
  assert.equal(page.revision(2).innerHTML, before, 'a patch names one row; the rest of the page is not redrawn');
});

test('an IDENTICAL revision patch is skipped, so a focused button inside it survives', () => {
  // The rule `.coderabbit.yaml` states for this page, and the one its YOUNGER sibling `showCalls`
  // has obeyed since story 3.3's second round. RevisionPanel fans one answer out per REPOSITORY, so
  // several rows get the same markup and a redraw of unchanged content is the normal case here.
  const page = run([pair(1)], { expanded: new Set([1]) });
  const box = page.revision(1);

  page.host.push({ type: 'revisions', items: [{ id: 1, html: '<span class="why">the checkout is gone</span>' }] });
  const painted = box.writes;

  page.host.push({ type: 'revisions', items: [{ id: 1, html: '<span class="why">the checkout is gone</span>' }] });

  assert.equal(box.writes, painted, 'the same markup a second time must not touch the DOM');
  assert.match(box.innerHTML, /the checkout is gone/u, 'and what is on screen is still the answer');
});

// --------------------------------------------------------------------------------------------
// The live-patch channel: the container the page WRITES is the container a patch FINDS.
// --------------------------------------------------------------------------------------------

// --------------------------------------------------------------------------------------------
// A person's words about a pair (story 4.2 of PLAN_a_comment_crosses_the_machine_boundary.md).
// Every behaviour below is the script RUN; the sentence is read off the rendered element, which
// is a VALUE and inside the 2026-09-14 ruling's carve-out.
// --------------------------------------------------------------------------------------------

/** What the page posted because of something a person DID — its own `ready` announcement left out. */
const acted = (page: Page): readonly Posted[] => page.posted.filter((one) => one.type !== 'ready');

/** A pair the server has acknowledged, with or without its words having landed. */
const sent = (findingId: number, comment: string, commentLost = ''): ReviewPair =>
  ({ ...pair(findingId), comment, sentUtc: '2026-09-18T00:03:00.000Z', commentLost });

test('typing and then pausing posts the words once, for the pair they were typed on', () => {
  const page = run([pair(1), pair(2)], { expanded: new Set([2]) });

  page.type(page.comment(2), 'this one bit us');
  assert.deepEqual(acted(page).filter((one) => one.type === 'comment'), [],
    'nothing is WRITTEN on a keystroke — each write is a process');

  page.pause();
  assert.deepEqual(acted(page).filter((one) => one.type === 'comment'), [{ type: 'comment', id: 2, text: 'this one bit us' }]);
});

test('every keystroke hands the panel the words as a DRAFT, so closing the page cannot lose them', () => {
  // A draft is a message, not a write: it costs no process. What it buys is that the panel holds the
  // words from the first keystroke, and can write them itself if the page closes before a pause or
  // a blur ever comes — the window a code reviewer found open (codex, twice, round 1 of 4.2's code).
  const page = run([pair(1)], { expanded: new Set([1]) });

  page.type(page.comment(1), 'half');
  page.type(page.comment(1), 'half a thought');

  assert.deepEqual(acted(page), [
    { type: 'draft', id: 1, text: 'half' },
    { type: 'draft', id: 1, text: 'half a thought' },
  ]);
});

test('leaving the box posts what is in it, and the pause it replaced does not post again', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });
  const box = page.comment(1);

  page.type(box, 'half a thought');
  page.leave(box);
  page.pause();

  assert.deepEqual(acted(page).filter((one) => one.type === 'comment'), [{ type: 'comment', id: 1, text: 'half a thought' }],
    'the blur flushes the words, and a second post of the same words would be a second write');
});

test('a draft the panel holds is drawn back over the stored words, so a redraw loses nothing', () => {
  const page = run([{ ...pair(1), comment: 'stored' }], { comments: new Map([[1, 'typed since']]) });

  assert.equal(page.comment(1).value, 'typed since');
  assert.equal(page.counter(1).textContent, `${'typed since'.length} / ${COMMENT_MOST_CHARS}`);
});

test('the words a pair already carries are in its box, and the box can still change', () => {
  const page = run([{ ...pair(1), comment: 'stored words' }]);

  assert.equal(page.comment(1).value, 'stored words');
  assert.equal(page.comment(1).readOnly, false, 'nothing was sent, so nothing is fixed yet');
});

test('every unsent box says, beside it, that its words leave the machine in public', () => {
  const page = run([pair(1), pair(2), pair(3)]);

  for (const box of page.comments) {
    assert.equal(page.notice(Number(box.key)), LEAVES_THE_MACHINE,
      `pair ${box.key}'s box must say where its words go before a person chooses them`);
  }
  assert.equal(page.comments.length, 3, 'one box per pair, none forgotten');
});

test('a sent pair\'s box is read-only, says it was sent, and typing into it posts nothing', () => {
  const page = run([sent(1, 'what crossed')]);
  const box = page.comment(1);

  assert.equal(box.readOnly, true);
  assert.match(page.notice(1), /^Sent on 2026-09-18\. A change here will not follow it\.$/u);

  page.type(box, 'an edit after the send');
  page.leave(box);
  page.pause();
  assert.deepEqual(acted(page), [], 'words changed after the send would never cross, so none are posted');
});

test('a pair whose words did not land says so, with the server\'s reason, instead of "sent"', () => {
  const page = run([sent(1, 'mine', 'this pair already carries a comment, and the first one stays, so yours was not stored')]);

  assert.match(page.notice(1), /^Sent on 2026-09-18, but your comment was not stored: this pair already carries a comment/u);
});

test('markup in a comment is text: the box holds it, and no element is made of it', () => {
  const hostile = '</textarea><img src=x onerror=alert(1)>';
  const html = reviewPageHtml({ pairs: [{ ...pair(1), comment: hostile }], nonce: 'n' });
  const page = run([{ ...pair(1), comment: hostile }]);

  assert.equal(page.comment(1).value, hostile, 'the words come back whole, as the person wrote them');
  assert.ok(!html.includes('<img src=x'), 'and they did not become an element on the page');
});

test('pressing inside a box neither opens nor closes its row, and decides nothing', () => {
  const page = run([pair(1)], { expanded: new Set([1]) });

  page.click(page.comment(1));

  assert.equal(page.showing(1), true, 'the row stayed open');
  assert.deepEqual(acted(page), [], 'and no decision, expansion or comment was posted by a press');
});

test('the counter turns warning-coloured from nine hundred, before the box stops taking more', () => {
  const page = run([pair(1)]);
  const box = page.comment(1);

  page.type(box, 'x'.repeat(899));
  assert.equal(page.counter(1).className, 'count');
  page.type(box, 'x'.repeat(900));
  assert.equal(page.counter(1).className, 'count near');
  assert.equal(page.counter(1).textContent, `900 / ${COMMENT_MOST_CHARS}`);
});

test('a decision still posts exactly its three fields — the words travel from the panel, not the press', () => {
  const page = run([pair(1)], { comments: new Map([[1, 'typed']]) });

  page.click(page.boxes[0]!);
  page.click(page.controls['keep']!);

  const decided = page.posted.filter((one) => one.type === 'decide');
  assert.deepEqual(decided, [{ type: 'decide', keep: KEPT, ids: [1] }]);
});

test('a patch reaches the row the GENERATOR named, for both channels', () => {
  // The whole guarantee, RUN. The defect it replaces was silent: the generator and the page script
  // each spelled the container attribute out, so a rename made the patch write nowhere and the row
  // went on showing a stale answer with no error anywhere. Both sides come from `livePatch.ts` now.
  //
  // A first draft of this also asserted the two spellings matched by reading the generated text.
  // Two gate reviewers cited the operator's 2026-09-14 ruling against it and they were right: a
  // page is tested by RUNNING it, and this test proves the same thing by driving the real path —
  // the container found is the container emitted, or nothing changes. (Measured: hardcoding either
  // side turns seven tests red, this one among them.)
  const page = run([pair(1)], { expanded: new Set([1]) });

  page.host.push({ type: REVISIONS.message, items: [{ id: 1, html: '<b>reached the revision row</b>' }] });
  page.host.push({ type: CALLS.message, items: [{ id: 1, html: '<b>reached the calls row</b>' }] });

  assert.match(page.revision(1).innerHTML, /reached the revision row/u);
  assert.match(page.callBoxes.find((one) => one.key === '1')?.innerHTML ?? '', /reached the calls row/u);
});
