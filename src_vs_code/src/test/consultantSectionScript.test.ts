import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CUSTOM_ENDPOINT } from '../consultSettings';
import { panelHtml, type PanelFocus, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * The Consultant section's controls, RUN — the page's own script over the page's own markup.
 *
 * <p><b>Why this file exists rather than four more substring assertions.</b> A section is assembled
 * as a template literal, so a scan of that text sees everything it was supposed to contain: a box
 * wired to the wrong caller, an attribute the write path routes on that never reaches the control,
 * and a caret restored into somebody else's row all read as PRESENT. `.agents/PROJECT.md` refuses a
 * new behavioural assertion over page source text for exactly that reason, and this is the shape it
 * names — `panelPhrasesScript.test.ts`, whose own first tests stayed green over an unterminated
 * character class that made every control in the sidebar dead.</p>
 *
 * <p>The controls under test are NOT fixtures. They are parsed out of the page the section really
 * renders, so deleting `data-caller` from the endpoint box, or the box itself, is a test that goes
 * red rather than a string that still matches. Ask of each assertion what it would SEE if the
 * behaviour were deleted; every one below answers "nothing at this caller".</p>
 */

/** Just enough of a control for the script's `dataset`, `value` and caret reads. */
class Control {
  readonly dataset: Record<string, string> = {};
  value = '';
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

/** One `data-setting` tag of the page, as the script will meet it. */
function controlFrom(tag: string, attributes: string): Control {
  const control = new Control(tag.toUpperCase(), attribute(attributes, 'type'));
  control.value = attribute(attributes, 'value');
  for (const [, name, value] of attributes.matchAll(/data-([a-zA-Z]+)="([^"]*)"/g)) {
    control.dataset[name!] = value!;
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

const state = (focus?: PanelFocus): PanelState => ({
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
  openSections: ['consultant'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  ...(focus === undefined ? {} : { focus }),
});

/** The last `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel has no script to run');

  return html.slice(start, end);
}

interface Page {
  readonly controls: readonly Control[];
  readonly posted: readonly Record<string, unknown>[];
}

/** Render the panel, parse its controls, and run its own script over them. */
function run(focus?: PanelFocus): Page {
  const html = panelHtml(state(focus), 'test-nonce');
  const controls = controlsOf(html);
  const posted: Record<string, unknown>[] = [];
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Control[] =>
      (selector === '[data-setting]' ? controls : []),
    querySelector: (): null => null,
    getElementById: (): null => null,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };
  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', pageScript(html));
  body(
    () => ({
      postMessage: (message: Record<string, unknown>): void => { posted.push(message); },
      getState: () => undefined,
      setState: () => undefined,
    }),
    fakeDocument,
    { addEventListener: () => undefined },
    (): number => 0,
    (): void => undefined,
  );

  return { controls, posted };
}

/** The one control of that setting in that caller's row — and a failure that names the row. */
function control(page: Page, setting: string, caller: string): Control {
  const found = page.controls.filter((one) => one.dataset['setting'] === setting && one.dataset['caller'] === caller);
  assert.equal(found.length, 1,
    `the ${caller} row has no ${setting} control of its own — ${found.length} matched`);

  return found[0]!;
}

/** Whatever the script last sent as a setting write. */
function lastWrite(page: Page): Record<string, unknown> {
  const writes = page.posted.filter((one) => one['type'] === 'setting');
  assert.ok(writes.length > 0, 'the control was changed and the page wrote nothing');

  return writes.at(-1)!;
}

test('the vendor a caller is pointed at is written for THAT caller', () => {
  const page = run();
  const picker = control(page, 'consultVendor', 'gemini');

  picker.value = 'claude';
  picker.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultVendor', value: 'claude', vendor: undefined, role: undefined, caller: 'gemini',
  }, 'four rows share one setting name, so a write with no caller lands in whichever row the document holds first');
});

test('the consultant’s OWN endpoint is written for the caller whose row holds it', () => {
  const page = run();
  const endpoint = control(page, 'consultBaseUrl', 'gemini');

  endpoint.value = 'https://api.deepseek.com/v1';
  endpoint.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultBaseUrl', value: 'https://api.deepseek.com/v1',
    vendor: undefined, role: undefined, caller: 'gemini',
  }, 'the write path has accepted consultBaseUrl since story A2 and nothing in the section could send it');
});

test('the consultant’s OWN CLI path is written for the caller whose row holds it', () => {
  const page = run();
  const path = control(page, 'consultExecutablePath', 'codex');

  path.value = 'D:/tools/claude.cmd';
  path.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultExecutablePath', value: 'D:/tools/claude.cmd',
    vendor: undefined, role: undefined, caller: 'codex',
  }, 'a consultant that looks up its CLI on PATH cannot be pointed at one until this box exists');
});

test('choosing the custom endpoint asks the HOST, for THAT caller, and writes no setting at all', () => {
  // An id keys the vault entry and the usage ledger, so the one thing that must not happen is the
  // sentinel reaching disk: `{ vendor: '' }` is an entry a person can see in their settings file and
  // cannot choose in the panel. The picker is a `<select>`, so the only way to ask for something
  // that is not a value is to post a COMMAND — the shape `__other__` already uses for a model.
  const page = run();
  const picker = control(page, 'consultVendor', 'codex');
  const before = picker.value;

  picker.value = CUSTOM_ENDPOINT;
  picker.fire('change');

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'setting'), [],
    'the sentinel must never be stored — it is a request for a name, not a vendor');
  assert.deepEqual(page.posted.filter((one) => one['type'] === 'command'),
    [{ type: 'command', command: 'customConsultant', id: 'codex' }],
    'four rows share this control, so a command with no caller configures whichever the document holds first');
  assert.equal(picker.value, before,
    'and the select goes back: nothing is written, so no repaint comes, and it would sit on an option that is not a vendor');
});

test('a repaint puts the caret back in the row that was being edited, not the first row sharing its name', () => {
  // The shipped map points three callers at a codex consultant, so three rows carry an endpoint box
  // under one setting name. Without the caller in the focus id the script refocuses the first of
  // them — silently, mid-edit, which is the failure `FOCUS_ID`'s fourth segment exists to prevent.
  const page = run({ id: 'consultBaseUrl||\u007Cother', start: 2, end: 2 });

  assert.deepEqual(
    page.controls.filter((one) => one.focused).map((one) => one.dataset['caller']),
    ['other'],
    'the caret came back into somebody else’s consultant row',
  );
});
