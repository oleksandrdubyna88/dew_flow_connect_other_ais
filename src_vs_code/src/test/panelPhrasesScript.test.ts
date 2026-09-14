import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml, type PanelState } from '../panelView';
import { phrasesFrom } from '../phrases';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import type { Phrase } from '../phrases';

/**
 * The panel's own script, RUN — because a scan of its text cannot tell a program from a string.
 *
 * <p><b>This file exists because the alternative failed, immediately and completely.</b> The Copied
 * acknowledgement was first tested by matching the assembled page for `message?.type === 'copied'`
 * and for the word `Copied`. Both matched. The script they matched contained `/["\]/g` — an
 * unterminated character class — so the WHOLE panel script was a syntax error and every control in
 * the sidebar was dead. Three reviewers found it in the code round; no test could, because none of
 * them ran anything. `common/generated-code-tests.md` requires the artefact to be executed, and this
 * is that.</p>
 *
 * <p>The context is explicit and small — no ambient environment, no filesystem, no network — and the
 * handler under test is synchronous.</p>
 */

const PHRASES = phrasesFrom([
  { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy' },
  { id: 'odd"id', name: 'Awkward', text: 'an id with a quote in it' },
]);

const state = (phrases: readonly Phrase[] = PHRASES): PanelState => ({
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
  openSections: ['phrases'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  phrases,
});

/** Just enough of a button for the script's `dataset` and `textContent` reads. */
class Button {
  readonly dataset: Record<string, string>;
  textContent: string;

  constructor(id: string, label: string) {
    this.dataset = { command: 'copyPhrase', id };
    this.textContent = label;
  }
}

/** The last `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel has no script to run');

  return html.slice(start, end);
}

interface Page {
  readonly buttons: readonly Button[];
  say(message: unknown): void;
  tick(): void;
}

/**
 * Run the panel's script against a synthetic document and hand back what it can touch.
 *
 * <p>Timers are collected rather than fired, so the test decides when the second elapses.</p>
 */
function run(buttons: readonly Button[], phrases: readonly Phrase[] = PHRASES): Page {
  const later: (() => void)[] = [];
  const listeners = new Map<string, ((event: { data: unknown }) => void)[]>();
  const add = (kind: string, handler: (event: { data: unknown }) => void): void => {
    listeners.set(kind, [...(listeners.get(kind) ?? []), handler]);
  };
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Button[] =>
      (selector.includes('copyPhrase') ? buttons : []),
    querySelector: (): null => null,
    getElementById: (): null => null,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };
  const script = pageScript(panelHtml(state(phrases), 'test-nonce'));
  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test. A scan of
  // its text once passed over a syntax error that made every control in the panel dead.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', script);
  body(
    () => ({ postMessage: () => undefined, getState: () => undefined, setState: () => undefined }),
    fakeDocument,
    { addEventListener: add },
    (fn: () => void): number => { later.push(fn); return later.length; },
    (): void => undefined,
  );

  return {
    buttons,
    say: (message) => {
      for (const handler of listeners.get('message') ?? []) {
        handler({ data: message });
      }
    },
    tick: () => {
      const due = [...later];
      later.length = 0;
      for (const fn of due) {
        fn();
      }
    },
  };
}

test('the panel script is a program, not a string that looks like one', () => {
  // The whole point of this file. `new Function` throws on a syntax error, which is what the page
  // would do on every load — and what the text-matching tests could not see.
  assert.doesNotThrow(() => run([]), 'the panel script does not parse, so every control in the sidebar is dead');
});

test('a copy the host confirmed says so on the button that was pressed', () => {
  const ship = new Button('a', 'Ship it');
  const other = new Button('b', 'Check');
  const page = run([ship, other]);

  page.say({ type: 'copied', id: 'a' });

  assert.equal(ship.textContent, 'Copied', 'the button the person pressed never confirmed the copy');
  assert.equal(other.textContent, 'Check', 'a different button was changed');
});

test('the label comes back, so the button is a button again', () => {
  const ship = new Button('a', 'Ship it');
  const page = run([ship]);

  page.say({ type: 'copied', id: 'a' });
  page.tick();

  assert.equal(ship.textContent, 'Ship it', 'the button says Copied for ever');
});

test('an id with a quote in it is still found — no selector is built from it', () => {
  // The first version interpolated the id into a CSS selector and stripped quotes to make that safe,
  // which meant an id containing one could never be matched again. Four reviewers, one root cause.
  const awkward = new Button('odd"id', 'Awkward');
  const page = run([awkward]);

  page.say({ type: 'copied', id: 'odd"id' });

  assert.equal(awkward.textContent, 'Copied', 'a phrase whose id holds a quote can never be acknowledged');
});

test('a message about a phrase that is not on screen changes nothing', () => {
  const ship = new Button('a', 'Ship it');
  const page = run([ship]);

  page.say({ type: 'copied', id: 'gone' });

  assert.equal(ship.textContent, 'Ship it', 'a message naming another phrase changed this button');
});

test('anything that is not a copied message is left to the rest of the script', () => {
  const ship = new Button('a', 'Ship it');
  const page = run([ship]);

  for (const junk of [undefined, null, 'a string', { type: 'live' }, { type: 'copied' }]) {
    page.say(junk);
  }

  assert.equal(ship.textContent, 'Ship it', 'a message that names no phrase changed a button anyway');
});
