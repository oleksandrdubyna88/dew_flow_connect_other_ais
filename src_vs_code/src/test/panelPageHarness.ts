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

/** Just enough of a control for the script's `dataset`, `value` and caret reads. */
export class Control {
  readonly dataset: Record<string, string> = {};
  value = '';
  /** What an empty box says it stands for, as the page drew it — a DOM's `placeholder`. */
  placeholder = '';
  /** A checkbox's state, which the script reads instead of `value` (issue #485's switch). */
  checked = false;
  /** Whether the page rendered the control switched off — an `api` row against an older server (S1.2). */
  disabled = false;
  focused = false;
  selection: readonly [number, number] = [-1, -1];
  /**
   * A dropdown's choices as the page drew them — value and visible text, in document order — as a DOM's
   * `options` gives them. Empty for every other control. So a test can ask what a person is OFFERED, not
   * only what was selected (S3.8: the effort list is the module's and nothing else).
   */
  options: readonly { readonly value: string; readonly text: string }[] = [];
  private readonly handlers = new Map<string, (() => void)[]>();

  constructor(readonly tagName: string, readonly type: string) {}

  addEventListener(kind: string, handler: () => void): void {
    this.handlers.set(kind, [...(this.handlers.get(kind) ?? []), handler]);
  }

  fire(kind: string): void {
    for (const handler of this.handlers.get(kind) ?? []) {
      handler();
    }
  }

  focus(): void {
    this.focused = true;
  }

  setSelectionRange(start: number, end: number): void {
    this.selection = [start, end];
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
 * A dropdown holds the option the page marked `selected`, as a DOM gives it — so a test can see which
 * choice a page was DRAWN on (the follow-up to PR #561), rather than every select starting empty.
 */
function withChoice(control: Control, html: string, at: number): Control {
  if (control.tagName !== 'SELECT') {
    return control;
  }
  const body = html.slice(at, html.indexOf('</select>', at));
  control.value = /<option\b[^>]*\bvalue="([^"]*)"[^>]*\sselected(?=[\s>])/.exec(body)?.[1] ?? '';
  control.options = [...body.matchAll(/<option\b([^>]*)>([^<]*)<\/option>/g)]
    .map(([, attributes, text]) => ({ value: attribute(attributes!, 'value'), text: text!.trim() }));

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

export interface Page {
  readonly html: string;
  readonly controls: readonly Control[];
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

/** Render the page holding the fixture's section, parse its controls, and run its own script over them. */
export function runPanel(state: PanelState): Page {
  const html = pageHolding(state);
  const controls = controlsOf(html);
  const commands = commandsOf(html);
  const posted: Record<string, unknown>[] = [];
  const regions = regionsOf(html);
  // EVERY message listener: the Settings page adds its own (the tab it is told to show) after the shared
  // script's, and keeping only the last one would hand a live push to the listener that ignores it.
  const listeners: ((event: { data: unknown }) => void)[] = [];
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Control[] => selected(selector, controls, commands),
    querySelector: (): null => null,
    // The live regions answer, so the script's live push can be watched landing — widened when the
    // cadence line joined Active rounds, now Active gates (PR #556, CodeRabbit). Every other id is absent, as before.
    getElementById: (id: string): Region | null => regions.get(id) ?? null,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };

  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', pageScript(html));
  body(
    () => ({
      postMessage: (message: Record<string, unknown>): void => { posted.push(message); },
      getState: () => undefined,
      setState: () => undefined,
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
  );

  return {
    html,
    controls,
    commands,
    posted,
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
function selected(selector: string, controls: readonly Control[], commands: readonly Control[]): readonly Control[] {
  if (selector === '[data-setting]') {
    return controls;
  }

  return selector === '[data-command]' ? commands : [];
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
