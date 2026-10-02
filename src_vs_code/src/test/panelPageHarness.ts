import assert from 'node:assert/strict';

import { panelHtml, type PanelFocus, type PanelState, settingsHtml, settingsSections } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { LIVE_REGION_IDS } from '../panelSurface';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { camel } from './rolesPageHarness';

/**
 * The panel's pages, RUN — their own script over their own markup, for a test of any section's controls,
 * on the sidebar or in the Settings tab.
 *
 * <p>Extracted from `consultantSectionScript.test.ts` when the gate section gained controls of its own
 * (issue #117): a second copy of this harness beside the first is the duplication `reuse-first.md`
 * names. `.agents/PROJECT.md` refuses a new behavioural assertion over page source text, and this is
 * the shape it points at — the controls are PARSED out of the page the panel really renders, so
 * deleting an attribute the write path routes on is a test that goes red rather than a string that
 * still matches.</p>
 */

/** An event as a page's listener meets it: its type, a key for a keyboard event, where focus went, and whether it was stopped. */
export class PageEvent {
  defaultPrevented = false;

  constructor(readonly type: string, readonly key = '', readonly relatedTarget: Control | null = null) {}

  preventDefault(): void {
    this.defaultPrevented = true;
  }
}

/**
 * One `<option>` of a dropdown, as a DOM holds it: a value, its visible text, and the select it is IN — `null` once
 * it has been removed, which is how a page that detaches non-matches leaves it (the list's search box, Epic 2).
 */
export class PageOption {
  parentNode: Control | null = null;

  constructor(readonly value: string, readonly text: string) {}
}

/** The node a control sits in, as far as a page uses it: somewhere to insert a node BEFORE the control. */
export interface PageParent {
  insertBefore: (node: Control, before: Control) => void;
}

/** A parent that refuses: a control nobody placed in a running page has nowhere to insert anything. */
const NO_PARENT: PageParent = {
  insertBefore: () => { throw new Error('this control is not in a running page, so nothing can be inserted beside it'); },
};

/**
 * Just enough of a control for the script's `dataset`, `value` and caret reads — and, since the list search box
 * (todo/PLAN_model_search_and_busy_marks.md, Epic 2), for a select's options to be moved, removed and re-added.
 *
 * <p>Stricter than a DOM wherever the two can differ, never more permissive (`generated-code-tests.md` §3): a select
 * refuses a value none of its options carries (a DOM selects nothing and answers `''`), `removeChild` of a node that is
 * not a child throws (a DOM throws `NotFoundError`), and `appendChild` MOVES a node rather than copying it.</p>
 */
export class Control {
  readonly dataset: Record<string, string> = {};
  /** What an empty box says it stands for, as the page drew it — a DOM's `placeholder`. */
  placeholder = '';
  /** A checkbox's state, which the script reads instead of `value` (issue #485's switch). */
  checked = false;
  /** Whether the page rendered the control switched off — an `api` row against an older server (S1.2). */
  disabled = false;
  focused = false;
  selection: readonly [number, number] = [-1, -1];
  className = '';
  /** The node the control sits in: what a page calls `insertBefore` on. Set when the control joins a running page. */
  parentNode: PageParent = NO_PARENT;
  /**
   * A dropdown's choices as the page drew them — value and visible text, in document order — as a DOM's
   * `options` gives them. Empty for every other control. So a test can ask what a person is OFFERED, not
   * only what was selected (S3.8: the effort list is the module's and nothing else). LIVE: a page that moves or
   * removes options changes this list, exactly as it changes a DOM's.
   */
  options: PageOption[] = [];
  private current = '';
  private readonly attributes = new Map<string, string>();
  private readonly handlers = new Map<string, ((event: PageEvent) => void)[]>();

  constructor(readonly tagName: string, public type: string) {}

  get value(): string {
    return this.current;
  }

  /** A select takes only a value one of its options carries; anything else selects nothing, as a DOM does. */
  set value(next: string) {
    const refused = this.tagName === 'SELECT' && this.options.length > 0 && !this.options.some((one) => one.value === next);
    this.current = refused ? '' : next;
  }

  addEventListener(kind: string, handler: (event: PageEvent) => void): void {
    this.handlers.set(kind, [...(this.handlers.get(kind) ?? []), handler]);
  }

  fire(kind: string, event: PageEvent = new PageEvent(kind)): PageEvent {
    for (const handler of this.handlers.get(kind) ?? []) {
      handler(event);
    }

    return event;
  }

  /** What a page's `dispatchEvent` does: run this control's listeners for that event's type. */
  dispatchEvent(event: PageEvent): boolean {
    this.fire(event.type, event);

    return !event.defaultPrevented;
  }

  focus(): void {
    this.focused = true;
  }

  setSelectionRange(start: number, end: number): void {
    this.selection = [start, end];
  }

  setAttribute(name: string, value: string): void {
    this.attributes.set(name, value);
  }

  getAttribute(name: string): string | null {
    return this.attributes.get(name) ?? null;
  }

  /** A select's option moved to its end — taken out of wherever it was first, never copied. */
  appendChild(option: PageOption): PageOption {
    assert.equal(this.tagName, 'SELECT', 'only a select holds options in this harness');
    if (option.parentNode !== null) {
      option.parentNode.removeChild(option);
    }
    this.options = [...this.options, option];
    option.parentNode = this;

    return option;
  }

  removeChild(option: PageOption): PageOption {
    if (!this.options.includes(option)) {
      throw new Error('NotFoundError: the node to be removed is not a child of this node');
    }
    this.options = this.options.filter((one) => one !== option);
    option.parentNode = null;
    // Removing the CHOSEN option re-runs a DOM's selectedness: a single-row select then holds its first remaining
    // option, or nothing. Keeping the old value here would let a page that moves options lose its choice and pass.
    if (option.value === this.current) {
      this.current = this.options[0]?.value ?? '';
    }

    return option;
  }
}

/**
 * One `data-setting` tag of the page, as the script will meet it — every `data-*` attribute under the
 * name `dataset` gives it, hyphenated ones included (`data-command-model` is `commandModel`), as a DOM
 * does.
 */
export function controlFrom(tag: string, attributes: string): Control {
  const control = new Control(tag.toUpperCase(), attribute(attributes, 'type'));
  control.value = attribute(attributes, 'value');
  control.placeholder = attribute(attributes, 'placeholder');
  control.disabled = /\sdisabled(?=[\s>]|$)/.test(attributes);
  // What the page DREW, read like `disabled` — a box drawn ticked starts ticked. Without it every box began
  // unticked here, and a test could not tell a page drawn from the stored value from one that was not.
  control.checked = /\schecked(?=[\s>]|$)/.test(attributes);
  for (const [, name, value] of attributes.matchAll(/data-([a-zA-Z-]+)="([^"]*)"/g)) {
    control.dataset[camel(name!)] = value!;
  }

  return control;
}

/**
 * One attribute by its whole name, as a DOM reads it. A word boundary alone would also match the tail of a
 * hyphenated name — `data-placeholder` read as `placeholder` — which makes the fake more permissive than
 * the real thing; the name must start the attribute list or follow whitespace.
 */
function attribute(attributes: string, name: string): string {
  return new RegExp(String.raw`(?:^|\s)${name}="([^"]*)"`).exec(attributes)?.[1] ?? '';
}

/** Every control the PAGE carries, in document order — never a hand-made list beside it. */
function controlsOf(html: string): readonly Control[] {
  return [...html.matchAll(/<(input|select|textarea)\b([^>]*)>/g)]
    .filter(([, , attributes]) => attributes!.includes('data-setting="'))
    .map((match) => withChoice(controlFrom(match[1]!, match[2]!), html, match.index));
}

/**
 * Every prompt picker the page carries — a `select` routed on `data-prompt`, not on a setting name. Kept apart from
 * {@link controlsOf}: a `[data-setting]` query must not answer it, as a DOM's would not.
 */
function promptsOf(html: string): readonly Control[] {
  return [...html.matchAll(/<select\b([^>]*)>/g)]
    .filter(([, attributes]) => attributes!.includes('data-prompt="'))
    .map((match) => withChoice(controlFrom('select', match[1]!), html, match.index));
}

/**
 * A dropdown holds the option the page marked `selected`, as a DOM gives it — so a test can see which
 * choice a page was DRAWN on (the follow-up to PR #561), rather than every select starting empty.
 */
function withChoice(control: Control, html: string, at: number): Control {
  if (control.tagName !== 'SELECT') {
    return control;
  }
  const body = html.slice(at, html.indexOf('</select>', at));
  for (const [, attributes, text] of body.matchAll(/<option\b([^>]*)>([^<]*)<\/option>/g)) {
    control.appendChild(new PageOption(attribute(attributes!, 'value'), text!.trim()));
  }
  control.value = /<option\b[^>]*\bvalue="([^"]*)"[^>]*\sselected(?=[\s>])/.exec(body)?.[1] ?? '';

  return control;
}

/**
 * Every `data-command` button the PAGE carries, in document order — what the script's `bindCommands`
 * binds a click to. A button is not a `data-setting` control, so it is not in `controlsOf`.
 */
function commandsOf(html: string): readonly Control[] {
  return [...html.matchAll(/<button\b([^>]*)>/g)]
    .filter(([, attributes]) => attributes!.includes('data-command="'))
    .map(([, attributes]) => controlFrom('button', attributes!));
}

/** A panel state with nothing configured, one section open, and whatever a test changes laid over it. */
export function panelState(openSection: string, overrides: Partial<PanelState> = {}, focus?: PanelFocus): PanelState {
  return {
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    agyModels: [],
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    questions: [],
    sessions: [],
    openSections: [openSection],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
    latestServerVersion: '',
    ...overrides,
    ...(focus === undefined ? {} : { focus }),
  };
}

/** The last `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel has no script to run');

  return html.slice(start, end);
}

/** Where the page inserted a node before one of its controls — the search box before its select. */
export interface Inserted {
  readonly node: Control;
  readonly before: Control;
}

export interface Page {
  readonly html: string;
  readonly controls: readonly Control[];
  /** Every prompt picker (`data-prompt`) — a dropdown, but not a setting. */
  readonly prompts: readonly Control[];
  /** Every node the page inserted beside a control, in the order it did it. */
  readonly inserted: () => readonly Inserted[];
  /** What the page last saved with `vscode.setState` — what a replaced document of the same webview will be handed. */
  readonly saved: () => unknown;
  /** Every `data-command` button, bound by the page's own script — `fire('click')` is a person clicking it. */
  readonly commands: readonly Control[];
  readonly posted: readonly Record<string, unknown>[];
  /** What a live region holds NOW — its rendered content until a delivered message replaces it. */
  readonly region: (id: string) => string;
  /** Hand the page's own message listener what the host posts, as `webview.postMessage` does. */
  readonly deliver: (data: Record<string, unknown>) => void;
  /** How many times the page REPLACED a live region's content — an identical push must replace nothing. */
  readonly regionWrites: (id: string) => number;
}

/** The regions the host's live push replaces, by id — read from the declaration, so a new region cannot be missed here. */
const LIVE_REGIONS: readonly string[] = LIVE_REGION_IDS.map((id) => `live-${id}`);

/** A live region as the script meets it: content it can read and replace, and no controls of its own. */
class Region {
  /** How many times the page assigned its content. */
  writes = 0;

  constructor(private content: string) {}

  get innerHTML(): string {
    return this.content;
  }

  set innerHTML(value: string) {
    this.content = value;
    this.writes += 1;
  }

  querySelectorAll(): readonly Control[] {
    return [];
  }
}

/**
 * A region's rendered content: what sits between `<div id="…">` and ITS closing tag, nested `div`s
 * counted — so a region holding cards of its own is read whole rather than cut at the first `</div>`.
 */
function regionContent(html: string, id: string): string | undefined {
  const open = `<div id="${id}">`;
  const start = html.indexOf(open);
  if (start < 0) {
    return undefined;
  }
  const tags = /<div\b|<\/div>/g;
  tags.lastIndex = start + open.length;
  let depth = 1;
  for (let tag = tags.exec(html); tag !== null; tag = tags.exec(html)) {
    depth += tag[0] === '</div>' ? -1 : 1;
    if (depth === 0) {
      return html.slice(start + open.length, tag.index);
    }
  }

  return undefined;
}

function regionsOf(html: string): Map<string, Region> {
  return new Map(LIVE_REGIONS.flatMap((id): [string, Region][] => {
    const content = regionContent(html, id);
    return content === undefined ? [] : [[id, new Region(content)]];
  }));
}

/**
 * The page that holds the section a fixture opens: the Settings tab, opened on that section, when the
 * section moved there (`research/PLAN_settings_page.md`) — the sidebar otherwise.
 */
function pageHolding(state: PanelState): string {
  const opened = state.openSections[0] ?? '';

  return settingsSections().some((section) => section.id === opened)
    ? settingsHtml(state, 'test-nonce', opened)
    : panelHtml(state, 'test-nonce');
}

/** What a run starts from beyond the panel state: the webview's own saved state, as `vscode.getState()` answers it. */
export interface RunOptions {
  /** What a previous document of the same webview saved with `setState` — `undefined` for a first load. */
  readonly saved?: unknown;
}

/** The tags a page may create here. Anything else refuses, so a page that builds what this harness cannot see fails loudly. */
const CREATABLE: readonly string[] = ['input'];

/** What the fake document's `createElement` answers: a control for a tag this harness models, a refusal for any other. */
export function createdElement(tag: string): Control {
  assert.ok(CREATABLE.includes(tag), `the page created a <${tag}>, which this harness does not model`);

  return new Control(tag.toUpperCase(), '');
}

/** Render the page holding the fixture's section, parse its controls, and run its own script over them. */
export function runPanel(state: PanelState, options: RunOptions = {}): Page {
  const html = pageHolding(state);
  const controls = controlsOf(html);
  const prompts = promptsOf(html);
  const commands = commandsOf(html);
  const posted: Record<string, unknown>[] = [];
  const regions = regionsOf(html);
  const inserted: Inserted[] = [];
  let saved = options.saved;
  const parent: PageParent = { insertBefore: (node, before) => { inserted.push({ node, before }); node.parentNode = parent; } };
  for (const control of [...controls, ...prompts, ...commands]) {
    control.parentNode = parent;
  }
  // EVERY message listener: the Settings page adds its own (the tab it is told to show) after the shared
  // script's, and keeping only the last one would hand a live push to the listener that ignores it.
  const listeners: ((event: { data: unknown }) => void)[] = [];
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Control[] => selected(selector, controls, commands, prompts),
    querySelector: (): null => null,
    // The live regions answer, so the script's live push can be watched landing — widened when the
    // cadence line joined Active rounds, now Active gates (PR #556, CodeRabbit). Every other id is absent, as before.
    getElementById: (id: string): Region | null => regions.get(id) ?? null,
    createElement: createdElement,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };

  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', 'Event', pageScript(html));
  body(
    () => ({
      postMessage: (message: Record<string, unknown>): void => { posted.push(message); },
      getState: (): unknown => saved,
      // Kept as given, as a webview keeps it — a copy, so a page cannot change what it saved by mutating it after.
      setState: (next: unknown): void => { saved = structuredClone(next); },
    }),
    fakeDocument,
    {
      addEventListener: (kind: string, handler: (event: { data: unknown }) => void): void => {
        if (kind === 'message') {
          listeners.push(handler);
        }
      },
    },
    (): number => 0,
    (): void => undefined,
    PageEvent,
  );

  return {
    html,
    controls,
    prompts,
    commands,
    posted,
    inserted: () => inserted.map((one) => one),
    saved: () => saved,
    region: (id) => regions.get(id)?.innerHTML ?? '',
    regionWrites: (id) => regions.get(id)?.writes ?? 0,
    deliver: (data) => {
      assert.ok(listeners.length > 0, 'the panel registers no message listener, so a live push drives nothing');
      for (const listener of listeners) {
        listener({ data });
      }
    },
  };
}

/**
 * What the fake document answers for a selector: the setting controls, the command buttons, and nothing
 * for any other selector — stricter than a DOM, never more permissive (`generated-code-tests.md` §3).
 */
function selected(
  selector: string, controls: readonly Control[], commands: readonly Control[], prompts: readonly Control[],
): readonly Control[] {
  switch (selector) {
    case '[data-setting]':
      return controls;
    case '[data-command]':
      return commands;
    case '[data-prompt]':
      return prompts;
    case 'select':
      // Every dropdown the page drew, a setting's and a prompt picker's alike — as `querySelectorAll('select')` does.
      return [...controls.filter((one) => one.tagName === 'SELECT'), ...prompts];
    default:
      return [];
  }
}

/** The button for one command and id, clicked as a person clicks it — what the page then posted is in `posted`. */
export function click(page: Page, command: string, id: string): void {
  const button = page.commands.find((one) => one.dataset['command'] === command && one.dataset['id'] === id);
  assert.ok(button !== undefined, `the page has no ${command} button for ${id}`);
  button.fire('click');
}

/** Whatever the script last sent as a setting write. */
export function lastWrite(page: Page): Record<string, unknown> {
  const writes = page.posted.filter((one) => one['type'] === 'setting');
  assert.ok(writes.length > 0, 'the control was changed and the page wrote nothing');

  return writes.at(-1)!;
}
