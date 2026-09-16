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
 * that rule has to win.</p>
 *
 * <p>The selector matcher below answers true, false or <b>undefined</b>, and never guesses: a
 * construct it does not model is undefined, and a separate assertion proves the undefined set holds
 * nothing that could paint this button. A matcher that quietly matched what it could not parse is a
 * failure this family has already paid for.</p>
 */

interface Rule {
  readonly selector: string;
  readonly body: string;
  /** Source order, which is what decides a specificity tie. */
  readonly at: number;
}

/** An element, as much of one as a selector can ask about. */
interface Element {
  readonly tag: string;
  readonly classes: readonly string[];
  readonly attrs: Readonly<Record<string, string>>;
}

/** The panel's stylesheet, parsed. Comments are dropped; there are no at-rules to nest. */
function stylesheet(html: string): Rule[] {
  const open = html.indexOf('<style>');
  const close = html.indexOf('</style>', open);
  assert.ok(open >= 0 && close > open, 'the panel ships no stylesheet');
  const css = html.slice(open + '<style>'.length, close).replace(/\/\*[\s\S]*?\*\//g, '');
  assert.ok(!css.includes('@media') && !css.includes('@keyframes'), 'this parser does not nest');

  const rules: Rule[] = [];
  const pattern = /([^{}]+)\{([^{}]*)\}/g;
  for (let found = pattern.exec(css); found !== null; found = pattern.exec(css)) {
    rules.push({
      selector: (found[1] ?? '').trim().replace(/\s+/g, ' '),
      body: (found[2] ?? '').trim(),
      at: rules.length,
    });
  }
  assert.ok(rules.length > 20, 'the stylesheet did not parse into rules');

  return rules;
}

/** CSS specificity as (ids, classes+attributes+pseudo-classes, elements+pseudo-elements). */
function specificity(selector: string): readonly [number, number, number] {
  const count = (pattern: RegExp): number => (selector.match(pattern) ?? []).length;

  return [
    count(/#[\w-]+/g),
    count(/\.[\w-]+/g) + count(/\[[^\]]*\]/g) + count(/(?<!:):[a-z-]+(?:\([^)]*\))?/g),
    count(/(?:^|[\s>+~])[a-z][\w-]*/g) + count(/::[a-z-]+/g),
  ];
}

/** Bigger wins; zero is a tie, which source order then breaks. */
function outranks(a: readonly [number, number, number], b: readonly [number, number, number]): number {
  return (a[0] - b[0]) || (a[1] - b[1]) || (a[2] - b[2]);
}

/** Everything this matcher models. Anything else is an honest undefined. */
const MODELLED =
  /^(?:[a-z][\w-]*)?(?:\.[\w-]+|\[[\w-]+(?:="[^"]*")?\]|:(?:hover|focus|active|focus-within))+$|^[a-z][\w-]*$/;

/** Does one compound selector describe this element? undefined when it cannot be read. */
function compoundMatches(compound: string, element: Element): boolean | undefined {
  if (!MODELLED.test(compound)) {
    return undefined;
  }
  const tag = compound.match(/^[a-z][\w-]*/)?.[0];
  if (tag !== undefined && tag !== element.tag) {
    return false;
  }
  for (const [, name] of compound.matchAll(/\.([\w-]+)/g)) {
    if (!element.classes.includes(name ?? '')) {
      return false;
    }
  }
  for (const [, name, , value] of compound.matchAll(/\[([\w-]+)(="([^"]*)")?\]/g)) {
    const held = element.attrs[name ?? ''];
    if (held === undefined || (value !== undefined && held !== value)) {
      return false;
    }
  }

  // A pseudo-class is a state the element CAN be in — a mouse sits on the button it has just
  // pressed — so it is a live competitor for the cascade rather than a reason to stop looking.
  return true;
}

/** Could this selector paint `element` sitting inside `ancestors`? undefined when unreadable. */
function couldMatch(
  selector: string,
  element: Element,
  ancestors: readonly Element[],
): boolean | undefined {
  let unreadable = false;
  for (const branch of selector.split(',')) {
    // THE SUBJECT DECIDES FIRST. A combinator only ever adds a constraint, so a branch whose last
    // compound cannot be this element is a definite no however it is joined. Asking about the
    // combinator first put `.sec-phrases > summary` — a rule for a disclosure heading, which can
    // never be a button — into the unreadable pile, and the test then failed on its own guard
    // instead of on the missing rule it was written to find.
    const compounds = branch.trim().split(/[\s>+~]+/).filter(Boolean);
    const last = compounds.length === 0
      ? undefined
      : compoundMatches(compounds[compounds.length - 1] ?? '', element);
    if (last === undefined) {
      unreadable = true;
      continue;
    }
    if (!last) {
      continue;
    }
    // It could be the subject, and how it is joined to its ancestors is beyond this matcher.
    if (/[>+~]/.test(branch)) {
      unreadable = true;
      continue;
    }
    let left = [...ancestors];
    const reached = compounds.slice(0, -1).every((compound) => {
      const found = left.findIndex((up) => compoundMatches(compound, up) === true);
      if (found < 0) {
        unreadable = unreadable || left.some((up) => compoundMatches(compound, up) === undefined);

        return false;
      }
      left = left.slice(found + 1);

      return true;
    });
    if (reached) {
      return true;
    }
  }

  return unreadable ? undefined : false;
}

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

/** Does a rule set a colour at all? One that does not cannot win or lose this argument. */
function paints(rule: Rule): boolean {
  return /(^|;)\s*(color|background)\s*:/.test(rule.body);
}

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
  const inside: readonly Element[] = [{ tag: 'div', classes: ['phrases'], attrs: {} }];

  // (c) The rule, found by MATCHING it against that element — never by searching the text for it.
  const sheet = stylesheet(html);
  const unreadable = sheet.filter(
    (rule) => paints(rule)
      && couldMatch(rule.selector, button, inside) === undefined
      && /\brun\b|\bphrases\b|data-said|\bbutton\b/.test(rule.selector),
  );
  assert.deepEqual(
    unreadable.map((rule) => rule.selector),
    [],
    'a rule that could paint this button is written in a form this test cannot read, so the verdict below is not trustworthy',
  );

  const matching = sheet.filter(
    (rule) => paints(rule) && couldMatch(rule.selector, button, inside) === true,
  );
  const green = matching.filter((rule) => rule.body.includes('var(--vscode-charts-green)'));
  assert.deepEqual(
    green.map((rule) => rule.selector),
    ['.phrases .run[data-said="1"]'],
    'nothing in the panel stylesheet paints the copied state green, so Copied arrives in the same colour as the phrase name',
  );

  // (d) And it wins the cascade it is in — including the hover a mouse is still sitting in.
  const rule = green[0]!;
  const rank = specificity(rule.selector);
  const beaten = matching.filter((other) => {
    if (other === rule) {
      return false;
    }
    const against = outranks(specificity(other.selector), rank);

    return against > 0 || (against === 0 && other.at > rule.at);
  });
  assert.deepEqual(
    beaten.map((other) => other.selector),
    [],
    'another rule outranks the acknowledgement and paints this button instead',
  );

  // And it goes away with the mark, so the button is a button again.
  page.tick();
  assert.equal(ship.dataset.said, undefined, 'the green state never lifts');
});

test('the acknowledgement takes its green from the theme, never a hex of ours', () => {
  const rule = stylesheet(panelHtml(state(), 'n0nce'))
    .find((one) => one.selector === '.phrases .run[data-said="1"]');
  assert.ok(rule, 'the acknowledgement rule is gone');
  assert.ok(!/#[0-9a-f]{3,8}\b/i.test(rule.body), `a colour of ours instead of the theme: ${rule.body}`);
});
