import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { PanelState, REPAINT_HOLD_MS, panelHtml, staticKey, withholdsRepaint } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { ChatSettings } from '../chatSettings';

/**
 * What is typed into *What to ask about the selection* stays typed.
 *
 * <p>2026-09-09: the operator typed `поясни` into the sidebar's prompt box and it came back holding
 * what it held before. `coai.chatPrompt` is what every chat turn opens with, so a prompt that does
 * not save is a feature that cannot be configured.</p>
 *
 * <p><b>The measurement, because the draft plan's premise was wrong.</b> It said the obvious fix —
 * save on `input` — makes things worse, since every save is a `config.update`, every update fires
 * `onDidChangeConfiguration`, and the listener repaints, so the box would lose focus after every
 * character. It does not: `render()` only assigns `webview.html` when `staticKey` moves, and
 * `staticKey` reads eleven fields of which `chat` is not one. A chat setting cannot repaint the
 * panel. That is asserted below, so the day somebody adds `chat` to the key this design fails loudly
 * rather than silently.</p>
 *
 * <p>What DOES kill the draft is that a full repaint can land at any moment from five causes that
 * are nobody's doing — a local-engine probe, the server's own version, the published release, the
 * Team-server catalog, and any other setting written from any window — while the typed text exists
 * only in the DOM, because the `[data-setting]` listener posted on `change`, which a textarea fires
 * at BLUR. Hence two halves: the value is durable as it is typed, and a repaint does not rebuild the
 * page under a focused control.</p>
 *
 * <p>These tests RUN the page's own script. The code round on the plan refused a text scan of it —
 * a script can contain an `input` listener, a debounce and a focus message while posting the wrong
 * key, a stale value or the wrong order, and a scan would call that green. So the script is executed
 * against a fake document and the messages it posts are read.</p>
 */

const chat: ChatSettings = { prompt: 'Explain', language: 'en', autoSend: 'keyboard', model: '' };

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['chat'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  chat,
  ...over,
});

// ---------------------------------------------------------------------------------------------
// A document just real enough to run the page's script against.
// ---------------------------------------------------------------------------------------------

interface FakeEvent {
  readonly relatedTarget?: FakeElement | null;
}

class FakeElement {
  readonly listeners = new Map<string, ((event: FakeEvent) => void)[]>();
  focused = false;
  caret: readonly [number, number] = [0, 0];

  constructor(
    readonly tagName: string,
    readonly dataset: Record<string, string | undefined>,
    public value = '',
    readonly type = '',
  ) {}

  addEventListener(kind: string, handler: (event: FakeEvent) => void): void {
    this.listeners.set(kind, [...(this.listeners.get(kind) ?? []), handler]);
  }

  fire(kind: string, event: FakeEvent = {}): void {
    for (const handler of this.listeners.get(kind) ?? []) {
      handler(event);
    }
  }

  focus(): void {
    this.focused = true;
  }

  setSelectionRange(start: number, end: number): void {
    this.caret = [start, end];
  }
}

interface Page {
  readonly posted: readonly Record<string, unknown>[];
  readonly elements: readonly FakeElement[];
  fire(index: number, kind: string, event?: FakeEvent): void;
  document(kind: string, hidden?: boolean): void;
  tick(): void;
}

/** The `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.indexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel page has no script to run');

  return html.slice(start, end);
}

/** Run the panel's script over a handful of controls and collect everything it posts. */
function run(elements: readonly FakeElement[], over: Partial<PanelState> = {}): Page {
  const posted: Record<string, unknown>[] = [];
  const documentListeners = new Map<string, ((event: FakeEvent) => void)[]>();
  const timers = new Map<number, () => void>();
  let nextTimer = 1;
  let hidden = false;

  const query = (selector: string): FakeElement[] => {
    if (selector === '[data-setting]') {
      return [...elements];
    }
    const named = /^\[data-setting="([^"]*)"\]$/.exec(selector);
    if (named !== null) {
      return elements.filter((el) => el.dataset.setting === named[1]);
    }

    return [];
  };
  const fakeDocument = {
    querySelectorAll: query,
    querySelector: (selector: string): FakeElement | null => query(selector)[0] ?? null,
    getElementById: (): null => null,
    addEventListener: (kind: string, handler: (event: FakeEvent) => void): void => {
      documentListeners.set(kind, [...(documentListeners.get(kind) ?? []), handler]);
    },
    get visibilityState(): string {
      return hidden ? 'hidden' : 'visible';
    },
  };
  const fakeWindow = {
    addEventListener: (kind: string, handler: (event: FakeEvent) => void): void => {
      documentListeners.set(kind, [...(documentListeners.get(kind) ?? []), handler]);
    },
  };
  const later = (fn: () => void): number => {
    timers.set(nextTimer, fn);

    return nextTimer++;
  };
  const cancel = (id: number): void => {
    timers.delete(id);
  };

  const script = pageScript(panelHtml(state(over), 'test-nonce'));
  // eslint-disable-next-line no-new-func -- the shipped script is the thing under test; a scan of
  // its text is what the code round refused.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', script);
  body(
    () => ({ postMessage: (message: Record<string, unknown>) => { posted.push(message); } }),
    fakeDocument,
    fakeWindow,
    later,
    cancel,
  );

  return {
    posted,
    elements,
    fire: (index, kind, event) => { elements[index].fire(kind, event); },
    document: (kind, hide = false) => {
      hidden = hide;
      for (const handler of documentListeners.get(kind) ?? []) {
        handler({});
      }
    },
    tick: () => {
      const due = [...timers.values()];
      timers.clear();
      for (const fn of due) {
        fn();
      }
    },
  };
}

const promptBox = (): FakeElement => new FakeElement('TEXTAREA', { setting: 'chatPrompt' });
const languagePicker = (): FakeElement => new FakeElement('SELECT', { setting: 'chatLanguage' }, 'en');
const settings = (page: Page): Record<string, unknown>[] =>
  page.posted.filter((message) => message.type === 'setting');
const focusMessages = (page: Page): Record<string, unknown>[] =>
  page.posted.filter((message) => message.type === 'focus');

// ---------------------------------------------------------------------------------------------

test('what is typed into the prompt box is saved without waiting for a blur', () => {
  const box = promptBox();
  const page = run([box, languagePicker()]);

  box.value = 'по';
  page.fire(0, 'input');
  box.value = 'поясни';
  page.fire(0, 'input');
  page.tick();

  assert.deepEqual(settings(page), [{
    type: 'setting', key: 'chatPrompt', value: 'поясни', vendor: undefined, role: undefined,
  }], 'one save, carrying the last thing typed');
});

test('a dropdown still saves when it is chosen, never as it is typed', () => {
  const picker = languagePicker();
  const page = run([promptBox(), picker]);

  picker.value = 'ru';
  page.fire(1, 'input');
  page.tick();

  assert.deepEqual(settings(page), [], 'a half-chosen dropdown was written to settings');

  page.fire(1, 'change');

  assert.equal(settings(page).length, 1, 'choosing a language saved nothing');
  assert.equal(settings(page)[0].key, 'chatLanguage');
  assert.equal(settings(page)[0].value, 'ru');
});

test('the page says when a control has focus, so nothing rebuilds it underneath', () => {
  const box = promptBox();
  const page = run([box, languagePicker()]);

  page.fire(0, 'focusin');

  assert.deepEqual(focusMessages(page), [{ type: 'focus', setting: 'chatPrompt', editing: true }]);

  page.fire(0, 'focusout', { relatedTarget: null });

  assert.deepEqual(focusMessages(page)[1], { type: 'focus', setting: 'chatPrompt', editing: false });
});

test('moving between two controls is not a moment to rebuild the page', () => {
  const box = promptBox();
  const picker = languagePicker();
  const page = run([box, picker]);

  page.fire(0, 'focusin');
  page.fire(0, 'focusout', { relatedTarget: picker });
  page.fire(1, 'focusin');

  assert.deepEqual(
    focusMessages(page).filter((message) => message.editing === false), [],
    'the page was released for a repaint while focus was still inside it',
  );
  // Paired with the transition actually being seen, so this cannot pass by reporting nothing at all.
  assert.deepEqual(focusMessages(page).map((message) => message.setting), ['chatPrompt', 'chatLanguage']);
});

test('leaving the box writes what was typed, before it says the box is free', () => {
  const box = promptBox();
  const page = run([box, languagePicker()]);

  page.fire(0, 'focusin');
  box.value = 'поясни';
  page.fire(0, 'input');
  page.fire(0, 'focusout', { relatedTarget: null });

  const order = page.posted.map((message) => `${String(message.type)}:${String(message.value ?? message.editing)}`);
  assert.deepEqual(order, ['focus:true', 'setting:поясни', 'focus:false'],
    'the value must be written before the panel is told it may repaint');
});

test('a panel that is being hidden writes what was typed into it', () => {
  const box = promptBox();
  const page = run([box, languagePicker()]);

  box.value = 'поясни';
  page.fire(0, 'input');
  page.document('visibilitychange', true);

  assert.deepEqual(settings(page).map((message) => message.value), ['поясни']);
});

test('a paint that could not be withheld any longer puts the caret back', () => {
  const box = promptBox();
  box.value = 'поясни';
  const page = run([box, languagePicker()], { focus: 'chatPrompt' });

  assert.equal(page.elements[0].focused, true, 'the control the paint landed under was not refocused');
  assert.deepEqual(page.elements[0].caret, [6, 6], 'the caret did not return to the end of what was typed');
});

test('an ordinary paint takes nobody’s focus', () => {
  const page = run([promptBox(), languagePicker()]);

  assert.equal(page.elements[0].focused, false, 'a repaint stole focus into the sidebar');
});

test('a focus name that is not a setting name never reaches the page', () => {
  const hostile = panelHtml(state({ focus: '</script><script>alert(1)' }), 'test-nonce');

  assert.ok(!hostile.includes('alert(1)'), 'a value echoed from the webview was written into its own script');
  assert.match(hostile, /const focusOn = '';/, 'the name was escaped rather than refused');
});

test('a chat setting cannot repaint the panel — the measurement this design rests on', () => {
  // The draft plan said saving on `input` would repaint after every character. It cannot: the paint
  // decision is `staticKey`, and `chat` is not one of the eleven fields it reads. Asserted so that
  // adding `chat` to the key fails HERE, with this sentence, rather than by making the box flicker.
  const typed: ChatSettings = { ...chat, prompt: 'поясни' };

  assert.equal(staticKey(state({ chat: typed })), staticKey(state()),
    'a chat setting now moves the repaint key, and saving as you type would rebuild the page per keystroke');
});

test('a repaint waits for a focused control, and not forever', () => {
  const now = 1_000_000;

  assert.equal(withholdsRepaint(0, now), false, 'nothing is focused, so nothing may be withheld');
  assert.equal(withholdsRepaint(now - 1_000, now), true, 'a paint landed on a control being edited');
  assert.equal(withholdsRepaint(now - REPAINT_HOLD_MS, now), false, 'the hold has no bound');
  assert.equal(withholdsRepaint(now - REPAINT_HOLD_MS - 1, now), false, 'the hold outlived its cap');
});

test('the host never renders from a configuration it has not finished writing', () => {
  // Structural: `panelProvider.ts` imports `vscode`, so no unit test here can instantiate it — the
  // idiom of `theLogRefusesToOpen.test.ts`. These three are orderings and lifetimes inside one
  // class, which is exactly what a unit test could not see even if it could reach them.
  const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');

  const awaits = source.indexOf('await this.queued;');
  const paints = source.indexOf('const key = staticKey(state);');
  assert.ok(awaits > 0 && paints > awaits, 'render must await the write queue before it decides what to paint');

  assert.match(source, /if \(key !== this\.paintedKey && !withholdsRepaint\(this\.editingSince, Date\.now\(\)\)\)/,
    'the paint no longer consults whether a control is being edited');
  assert.match(source, /this\.queued = this\.queued\.then\(work, work\)\.catch\(/,
    'a rejected write would poison the queue and freeze every later one');
  assert.match(source, /this\.enqueue\(\(\) => this\.write\(/,
    'settings are written straight off the message again, so two of them can race');
});
