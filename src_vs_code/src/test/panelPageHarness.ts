import assert from 'node:assert/strict';

import { panelHtml, type PanelFocus, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { camel } from './rolesPageHarness';

/**
 * The sidebar panel, RUN — its own script over its own markup, for a test of any section's controls.
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
  /** A checkbox's state, which the script reads instead of `value` (issue #485's switch). */
  checked = false;
  /** Whether the page rendered the control switched off — an `api` row against an older server (S1.2). */
  disabled = false;
  focused = false;
  selection: readonly [number, number] = [-1, -1];
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
  control.disabled = /\sdisabled(?=[\s>]|$)/.test(attributes);
  for (const [, name, value] of attributes.matchAll(/data-([a-zA-Z-]+)="([^"]*)"/g)) {
    control.dataset[camel(name!)] = value!;
  }

  return control;
}

function attribute(attributes: string, name: string): string {
  return new RegExp(`\\b${name}="([^"]*)"`).exec(attributes)?.[1] ?? '';
}

/** Every control the PAGE carries, in document order — never a hand-made list beside it. */
function controlsOf(html: string): readonly Control[] {
  return [...html.matchAll(/<(input|select|textarea)\b([^>]*)>/g)]
    .filter(([, , attributes]) => attributes!.includes('data-setting="'))
    .map(([, tag, attributes]) => controlFrom(tag!, attributes!));
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
  readonly posted: readonly Record<string, unknown>[];
  /** What a live region holds NOW — its rendered content until a delivered message replaces it. */
  readonly region: (id: string) => string;
  /** Hand the page's own message listener what the host posts, as `webview.postMessage` does. */
  readonly deliver: (data: Record<string, unknown>) => void;
}

/** The regions the host's live push replaces, by id. */
const LIVE_REGIONS: readonly string[] = ['live-questions', 'live-rounds', 'live-consultations', 'live-notifications'];

/** A live region as the script meets it: content it can read and replace, and no controls of its own. */
class Region {
  constructor(public innerHTML: string) {}

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

/** Render the panel, parse its controls, and run its own script over them. */
export function runPanel(state: PanelState): Page {
  const html = panelHtml(state, 'test-nonce');
  const controls = controlsOf(html);
  const posted: Record<string, unknown>[] = [];
  const regions = regionsOf(html);
  let listener: ((event: { data: unknown }) => void) | undefined;
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Control[] =>
      (selector === '[data-setting]' ? controls : []),
    querySelector: (): null => null,
    // The live regions answer, so the script's live push can be watched landing — widened when the
    // cadence line joined Active rounds (PR #556, CodeRabbit). Every other id is absent, as before.
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
          listener = handler;
        }
      },
    },
    (): number => 0,
    (): void => undefined,
  );

  return {
    html,
    controls,
    posted,
    region: (id) => regions.get(id)?.innerHTML ?? '',
    deliver: (data) => {
      assert.ok(listener !== undefined, 'the panel registers no message listener, so a live push drives nothing');
      listener({ data });
    },
  };
}

/** Whatever the script last sent as a setting write. */
export function lastWrite(page: Page): Record<string, unknown> {
  const writes = page.posted.filter((one) => one['type'] === 'setting');
  assert.ok(writes.length > 0, 'the control was changed and the page wrote nothing');

  return writes.at(-1)!;
}
