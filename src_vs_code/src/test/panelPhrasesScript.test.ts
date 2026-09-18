import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml, type PanelState } from '../panelView';
import { phrasesFrom } from '../phrases';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import type { Phrase } from '../phrases';
import { beating, painters, stylesheet, type Element } from './cssRules';

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

/**
 * The acknowledgement's COLOUR.
 *
 * <p>Issue #322: the button says *Copied* in the same colour and weight as its own name, so the one
 * signal that the clipboard write resolved is the one nobody notices.</p>
 *
 * <p><b>Why this is not a substring assertion.</b> `.agents/PROJECT.md` and `.coderabbit.yaml` refuse
 * a new behavioural assertion over page source text (operator ruling, 2026-09-14), and the plan round
 * for this change found the specific hole: a rule reading `.never[data-said="1"] { color:
 * var(--vscode-charts-green) }` satisfies every string match anybody would write and matches no
 * button. So four things are pinned TO EACH OTHER — the attribute the script is observed writing, the
 * element the page is observed rendering, the rule the stylesheet is parsed into, and the cascade
 * that rule has to win. The parsing and the ranking live in `cssRules.ts`, which exists because the
 * code round called a third private CSS parser Blocking under the reuse rule.</p>
 */

/** The phrase button the page actually renders, read out of the markup rather than assumed. */
function renderedPhraseButton(html: string): Element {
  const row = html.match(/<div class="phrases">([\s\S]*?)<\/div>/);
  assert.ok(row, 'the phrases section renders no button row for the rule to key on');
  const tag = (row[1] ?? '').match(/<button[^>]*data-command="copyPhrase"[^>]*>/);
  assert.ok(tag, 'the phrase row holds no copy button');
  const classes = (tag[0].match(/class="([^"]*)"/)?.[1] ?? '').split(/\s+/).filter(Boolean);
  const attrs: Record<string, string> = {};
  for (const [, name, value] of tag[0].matchAll(/([\w-]+)="([^"]*)"/g)) {
    attrs[name ?? ''] = value ?? '';
  }
  assert.ok(classes.includes('run'), `the copy button lost its run class: ${tag[0]}`);

  return { tag: 'button', classes, attrs };
}

/** Where a phrase button sits. The rule's first hop has to describe this. */
const IN_THE_ROW: readonly Element[] = [{ tag: 'div', classes: ['phrases'], attrs: {} }];

test('the word Copied is painted green by a rule that matches the button the script marks', () => {
  const html = panelHtml(state(), 'n0nce');

  // (a) The attribute the rule keys on — OBSERVED, by running the panel's own script.
  const ship = new Button('a', 'Ship it');
  const page = run([ship]);
  page.say({ type: 'copied', id: 'a' });
  assert.equal(ship.dataset.said, '1', 'the script no longer marks the button a rule could key on');
  assert.equal(ship.textContent, 'Copied', 'the acknowledgement is not on the button any more');

  // (b) The element the page renders, read out of the markup, wearing the mark from (a).
  const drawn = renderedPhraseButton(html);
  const button: Element = { ...drawn, attrs: { ...drawn.attrs, 'data-said': ship.dataset.said ?? '' } };

  // (c) The rule, found by MATCHING parsed selectors against that element — never by searching text.
  const { matching, unreadable } = painters(stylesheet(html), button, IN_THE_ROW);
  assert.deepEqual(
    unreadable.filter((rule) => /\brun\b|\bphrases\b|data-said|\bbutton\b/.test(rule.selector))
      .map((rule) => rule.selector),
    [],
    'a rule that could paint this button is written in a form this test cannot read, so the verdict below is not trustworthy',
  );

  const green = matching.filter((rule) => rule.body.includes('var(--vscode-charts-green)'));
  assert.deepEqual(
    green.map((rule) => rule.selector),
    ['.phrases .run[data-said="1"]'],
    'nothing in the panel stylesheet paints the copied state green, so Copied arrives in the same colour as the phrase name',
  );

  // (d) And it wins the cascade it is in — including the hover a mouse is still sitting in, and a
  //     later rule that restores a fill through `background-color` rather than the shorthand.
  assert.deepEqual(
    beating(green[0]!, matching, button, IN_THE_ROW).map((other) => other.selector),
    [],
    'another rule outranks the acknowledgement and paints this button instead',
  );

  // And it goes away with the mark, so the button is a button again.
  page.tick();
  assert.equal(ship.dataset.said, undefined, 'the green state never lifts');
});

test('the acknowledgement stands on the panel ground, in the theme green, with no colour of ours', () => {
  const rule = stylesheet(panelHtml(state(), 'n0nce'))
    .find((one) => one.selector === '.phrases .run[data-said="1"]');
  assert.ok(rule, 'the acknowledgement rule is gone');
  assert.ok(!/#[0-9a-f]{3,8}\b/i.test(rule.body), `a colour of ours instead of the theme: ${rule.body}`);
  // The panel is a SIDEBAR view whose body is transparent, so the ground behind this button is
  // sideBar.background — #181818 against the editor's #1F1F1F in the default dark theme. Naming a
  // background of its own painted a lighter rectangle on that ground; a code round caught it.
  assert.match(
    rule.body, /background:\s*transparent/,
    'the copied state names a background instead of standing on the panel ground, so it is a patch of another shade',
  );
});

// ---------------------------------------------------------------------------------------------
// The mark a section wears while a probe is out (issue #301, the code round asked for this)

test('the looking mark is one rule in the panel stylesheet, and it turns', () => {
  // A STRUCTURAL assertion, which is the kind this page allows: the stylesheet is parsed as data and
  // the rule is looked up, rather than the page's text being searched for a string.
  const rules = stylesheet(panelHtml(state(), 'n0nce'));
  const looking = rules.filter((one) => one.selector === '.looking');

  assert.equal(looking.length, 1,
    'the panel dims the loser when a class is defined twice, and a spinner is the worst thing to lose');
  assert.match(looking[0]!.body, /animation:\s*looking-turn/, 'the ring does not turn');
  assert.match(looking[0]!.body, /border-radius:\s*50%/, 'a square is not a ring');
  assert.ok(!/#[0-9a-f]{3,8}\b/i.test(looking[0]!.body),
    `a colour of ours instead of the theme: ${looking[0]!.body}`);
});

test('the stylesheet is still one parsable whole, at-rules and all', () => {
  // `stylesheet` asserts every byte was consumed, so this fails rather than silently skipping if the
  // spinner's nested @keyframes-inside-@media confuses the parser — which is the shape that carries
  // the reduced-motion preference without defining .looking a second time.
  const rules = stylesheet(panelHtml(state(), 'n0nce'));

  assert.ok(rules.length > 50, `the stylesheet parsed to ${rules.length} rules, which is not this page`);
  assert.equal(rules.filter((one) => one.selector.includes('@')).length, 0, 'an at-rule leaked out as a selector');
});
