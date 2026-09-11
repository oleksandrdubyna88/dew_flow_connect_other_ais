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

/**
 * <p><b>The example is historical, and the machinery is not.</b> *What to ask about the selection* is
 * a PICKER now — step 5 of the presets plan — so the textarea this defect was found in no longer
 * exists. The tests below keep its name because they run the page's own script against a synthetic
 * document, and the script is generic over `[data-setting]`: the same hazard is live for every
 * free-text control the panel still renders (`executablePath`, `credsKey`). Re-pointing the fixture
 * at one of those would rename the case without changing a single thing it proves.</p>
 */
const chat: ChatSettings = {
  prompt: 'Explain',
  promptChoice: '',
  prompts: [],
  models: [],
  language: 'en',
  autoSend: 'keyboard',
  model: '',
  modelName: '',
};

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
  selectionStart = 0;
  selectionEnd = 0;

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
    this.selectionStart = start;
    this.selectionEnd = end;
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
/** Two controls that share a setting name and are told apart only by their role. */
const roundsFor = (role: string): FakeElement => new FakeElement('INPUT', { setting: 'rounds', role }, '2', 'number');
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

  assert.deepEqual(focusMessages(page),
    [{ type: 'focus', id: 'chatPrompt||', editing: true, start: 0, end: 0 }]);

  page.fire(0, 'focusout', { relatedTarget: null });

  assert.deepEqual(focusMessages(page)[1],
    { type: 'focus', id: 'chatPrompt||', editing: false, start: 0, end: 0 });
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
  assert.deepEqual(focusMessages(page).map((message) => message.id), ['chatPrompt||', 'chatLanguage||']);
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
  const page = run([box, languagePicker()], { focus: { id: 'chatPrompt||', start: 2, end: 2 } });

  assert.equal(page.elements[0].focused, true, 'the control the paint landed under was not refocused');
  assert.deepEqual(page.elements[0].caret, [2, 2], 'the caret did not come back where it was');
});

test('the caret is put back inside a value the rebuilt page is shorter than', () => {
  const box = promptBox();
  box.value = 'по';
  const page = run([box], { focus: { id: 'chatPrompt||', start: 40, end: 90 } });

  assert.deepEqual(page.elements[0].caret, [2, 2], 'a range past the end of the box was asked for');
});

test('two controls sharing a setting name are told apart', () => {
  // `rounds` is carried by one control per role. A focus anchor that were the setting NAME alone
  // would refocus whichever of them the document holds first — which is never the one being edited
  // unless it happens to be Architecture.
  const architecture = roundsFor('Architecture');
  const security = roundsFor('SecurityReliability');
  run([architecture, security], { focus: { id: 'rounds||SecurityReliability', start: 1, end: 1 } });

  assert.equal(security.focused, true, 'the control that was being edited was not the one refocused');
  assert.equal(architecture.focused, false, 'a namesake control took the focus');
});

test('an ordinary paint takes nobody’s focus', () => {
  const page = run([promptBox(), languagePicker()]);

  assert.equal(page.elements[0].focused, false, 'a repaint stole focus into the sidebar');
});

test('a focus name that is not a setting name never reaches the page', () => {
  const hostile = panelHtml(
    state({ focus: { id: '</script><script>alert(1)', start: 0, end: 0 } }), 'test-nonce');

  assert.ok(!hostile.includes('alert(1)'), 'a value echoed from the webview was written into its own script');
  assert.match(hostile, /const focusOn = null;/, 'the id was escaped rather than refused');

  // And a caret that is not two ordered whole numbers is coerced rather than carried.
  const silly = panelHtml(
    state({ focus: { id: 'chatPrompt||', start: -5, end: Number.NaN } }), 'test-nonce');

  assert.match(silly, /const focusOn = \{"id":"chatPrompt\|\|","start":0,"end":0\};/);
});

test('a chat setting repaints the panel now, because there is no longer a box to rebuild under', () => {
  // THE GUARANTEE CHANGED, and the reason is that its premise did. While the section held a
  // textarea, `chat` had to be outside `staticKey` or saving the prompt as it was typed would have
  // rebuilt the page under a focused control per keystroke — that was measured, and this test was
  // the tripwire on it. The prompt is a PICKER now (step 5 of the presets plan), so every control in
  // the section is a `<select>`, which posts `change` with its dropdown already shut. And the
  // exclusion had acquired a cost: the provider and model selects are a PAIR, so choosing a provider
  // has to re-fill the models beside it — which a section the paint decision cannot see never does.
  const chosen: ChatSettings = { ...chat, model: 'antigravity' };

  assert.notEqual(staticKey(state({ chat: chosen })), staticKey(state()),
    'choosing a provider does not move the repaint key, so the model select beside it never re-fills');
});

test('the free-text hazard the old rule guarded is gone with the box, not merely overruled', () => {
  // The rule above is only safe while the section has nothing a person types INTO. This asserts
  // that, so re-introducing a textarea here fails with this sentence rather than by flickering.
  const body = panelHtml(state(), 'n0nce');
  const section = body.slice(body.indexOf('data-section="chat"'), body.indexOf('data-section="prompts"'));

  assert.doesNotMatch(section, /<textarea|<input type="(text|url|number)"/,
    'a free-text control is back in a section that now repaints on every one of its own settings');
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

  const awaits = source.indexOf('const seen = this.queued;');
  const paints = source.indexOf('const key = staticKey(state);');
  assert.ok(awaits > 0 && paints > awaits, 'render must await the write queue before it decides what to paint');

  assert.match(source, /if \(key !== this\.paintedKey && !withholdsRepaint\(this\.editingSince, Date\.now\(\)\)\)/,
    'the paint no longer consults whether a control is being edited');
  assert.match(source, /this\.queued = this\.queued\.then\(work, work\)\.catch\(/,
    'a rejected write would poison the queue and freeze every later one');
  assert.match(source, /this\.enqueue\(\(\) => this\.write\(/,
    'settings are written straight off the message again, so two of them can race');
});

test('leaving a box the pause already saved does not write it a second time', () => {
  const box = promptBox();
  const page = run([box, languagePicker()]);

  box.value = 'поясни';
  page.fire(0, 'input');
  page.tick();
  page.fire(0, 'change');
  page.fire(0, 'focusout', { relatedTarget: null });

  assert.deepEqual(settings(page).map((message) => message.value), ['поясни'],
    'the same value was written twice — `change` compares with the value at focus, not the one sent');
});

test('the hold is not renewed by moving between controls, and disposal forgets it', () => {
  const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');

  assert.match(source, /if \(this\.editingSince === 0\) \{\s*\n\s*this\.editingSince = Date\.now\(\);/,
    'every focus restarts the clock again, so tabbing between controls withholds a paint forever');
  assert.match(source, /onDidDispose\(\(\) => \{[\s\S]{0,120}this\.forgetEditing\(\);/,
    'a sidebar closed mid-sentence leaves the host believing a control is still being edited');
  assert.match(source, /const seen = this\.queued;[\s\S]{0,200}if \(seen !== this\.queued\)|if \(seen === this\.queued\)/,
    'render awaits one snapshot of the queue, so a write appended while it waited is read too late');
});

test('a chosen dropdown RELEASES the repaint it is holding, so its neighbour can re-fill', () => {
  // The plan gate (gemini, Major, twice from two vendors): the hold is 30 seconds and `focusin` on
  // ANY [data-setting] control starts it — a `<select>` included. So choosing a provider saved the
  // setting and then sat on the repaint until focus left the dropdown or half a minute passed, while
  // the model select beside it kept the previous provider's models. The hold exists to protect text
  // that lives only in the DOM; a dropdown has none — its value is already saved when `change` fires.
  const picker = languagePicker();
  const page = run([picker]);

  page.fire(0, 'focusin');
  picker.value = 'ru';
  page.fire(0, 'change');

  const released = focusMessages(page).filter((message) => message.editing === false);

  assert.equal(released.length, 1, 'choosing in a dropdown did not release the repaint hold');
  assert.equal(released[0]!.id, 'chatLanguage||');
});

test('a textarea keeps its hold through a change, because its words are still being written', () => {
  // The other half of the same rule, asserted so the fix cannot be widened into the case it was
  // written to protect: a textarea fires `change` at BLUR, and `focusout` releases it there already.
  const box = promptBox();
  const page = run([box]);

  page.fire(0, 'focusin');
  box.value = 'поясни';
  page.fire(0, 'change');

  assert.deepEqual(focusMessages(page).filter((message) => message.editing === false), [],
    'a textarea released its hold on change, before the blur that flushes it');
});
