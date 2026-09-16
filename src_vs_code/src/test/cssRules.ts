import assert from 'node:assert/strict';

/**
 * A stylesheet a page ships, as rules — and enough of a cascade to ask which one wins.
 *
 * <p>Extracted rather than written a third time. Two test files already parse CSS
 * (`chatPage.test.ts`'s `rules`/`ruleFor`, `vendorClassIsNotACard.test.ts`'s `rulesFor`) and neither
 * is exported; a code round called a third copy Blocking under the reuse rule and was right to. This
 * module is the shared unit those two should move onto — that migration is proposed, not done here,
 * because rewriting neighbouring tests uninvited turns a one-rule change into a diff nobody asked to
 * review.</p>
 *
 * <h2>It never guesses</h2>
 *
 * <p>{@link couldMatch} answers `true`, `false` or **`undefined`**, and `undefined` means *this
 * selector is written in a form I do not model*. A matcher that quietly matched what it could not
 * parse — returning the first element for an unsupported selector — is a failure this family has
 * already paid for: page tests bound to the wrong node and passed. A caller must treat `undefined`
 * as "my verdict is not trustworthy here", which is what the callers below assert about.</p>
 *
 * <p>{@link stylesheet} refuses the same way. It is a flat-rule parser, and rather than trusting that
 * the sheet is flat it proves it: every byte it did not consume must be whitespace, so a nested block
 * or a brace inside a quoted value makes the parse fail loudly instead of silently returning rules
 * that have slipped out of alignment.</p>
 */

/** One rule of a flat stylesheet. */
export interface Rule {
  readonly selector: string;
  readonly body: string;
  /** Source order, which is what decides a specificity tie. */
  readonly at: number;
}

/** An element, as much of one as a selector can ask about. */
export interface Element {
  readonly tag: string;
  readonly classes: readonly string[];
  readonly attrs: Readonly<Record<string, string>>;
}

/** CSS specificity, most significant first. */
export type Specificity = readonly [number, number, number];

/**
 * A page's `<style>` block, parsed into rules.
 *
 * @param html the whole document the page ships
 */
export function stylesheet(html: string): Rule[] {
  const open = html.indexOf('<style>');
  const close = html.indexOf('</style>', open);
  assert.ok(open >= 0 && close > open, 'the page ships no stylesheet');
  const css = html.slice(open + '<style>'.length, close).replace(/\/\*[\s\S]*?\*\//g, '');

  const rules: Rule[] = [];
  read(css, rules, '');
  assert.ok(rules.length > 0, 'the stylesheet parsed into no rules at all');

  return rules;
}

/** At-rules whose body holds ORDINARY RULES, which therefore still compete in the cascade. */
const DESCEND = /^@(media|supports|container|layer|scope)\b/;

/** At-rules whose body is not rules at all — frames, a font, a counter. Skipped whole. */
const OPAQUE = /^@(keyframes|-\w+-keyframes|font-face|counter-style|property|page|font-feature-values)\b/;

/**
 * One level of a stylesheet, by brace DEPTH rather than by a regex over `[^{}]`.
 *
 * <p>It started as that regex, and a reviewer was right that it could not be trusted: a nested block
 * leaves text behind, and text left behind means every rule after it has slid out of alignment while
 * still looking exactly like a rule. So this walks, and it asserts that it consumed everything —
 * which is what makes a construct it does not understand a loud failure rather than a silent
 * misreading. Whatever is skipped is named in the failure.</p>
 */
function read(css: string, into: Rule[], within: string): void {
  let at = 0;
  let consumed = 0;
  const skipped: string[] = [];
  while (at < css.length) {
    const opens = css.indexOf('{', at);
    if (opens < 0) {
      break;
    }
    let depth = 1;
    let scan = opens + 1;
    while (scan < css.length && depth > 0) {
      if (css[scan] === '{') { depth += 1; }
      else if (css[scan] === '}') { depth -= 1; }
      scan += 1;
    }
    assert.ok(depth === 0, `a block opened at ${opens} and never closed, so this stylesheet cannot be read`);
    const prelude = css.slice(consumed === at ? at : consumed, opens).trim().replace(/\s+/g, ' ');
    const body = css.slice(opens + 1, scan - 1);
    if (prelude.startsWith('@')) {
      if (DESCEND.test(prelude)) {
        read(body, into, prelude);
      } else if (!OPAQUE.test(prelude)) {
        skipped.push(prelude);
      }
    } else {
      into.push({ selector: prelude, body: body.trim(), at: into.length });
    }
    at = scan;
    consumed = scan;
  }
  const tail = css.slice(consumed).trim();
  if (tail.length > 0) {
    skipped.push(tail);
  }
  assert.deepEqual(
    skipped, [],
    `${within.length === 0 ? 'the stylesheet' : within} holds something this parser does not understand, `
    + 'so every verdict over it is unsound',
  );
}

/** (ids, classes + attributes + pseudo-classes, elements + pseudo-elements). */
export function specificity(selector: string): Specificity {
  const count = (pattern: RegExp): number => (selector.match(pattern) ?? []).length;

  return [
    count(/#[\w-]+/g),
    count(/\.[\w-]+/g) + count(/\[[^\]]*\]/g) + count(/(?<!:):[a-z-]+(?:\([^)]*\))?/g),
    count(/(?:^|[\s>+~])[a-z][\w-]*/g) + count(/::[a-z-]+/g),
  ];
}

/** Positive when `a` outranks `b`; zero is a tie, which source order then breaks. */
export function outranks(a: Specificity, b: Specificity): number {
  return (a[0] - b[0]) || (a[1] - b[1]) || (a[2] - b[2]);
}

/** Every compound this matcher models. Anything else is an honest `undefined`. */
const MODELLED =
  /^(?:[a-z][\w-]*)?(?:\.[\w-]+|\[[\w-]+(?:="[^"]*")?\]|:(?:hover|focus|active|focus-within|focus-visible))+$|^[a-z][\w-]*$/;

/** Does one compound selector describe this element? `undefined` when it cannot be read. */
export function compoundMatches(compound: string, element: Element): boolean | undefined {
  // A PSEUDO-ELEMENT IS NOT THIS ELEMENT. `::after` addresses a generated box of its own, so a rule
  // ending in one never paints the element it hangs off and never competes with a rule that does.
  // That is a definite no rather than an "I cannot read this": answering `undefined` would put every
  // ::after rule into the unreadable pile a caller is told to treat as untrustworthy.
  if (compound.includes('::')) {
    return false;
  }
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

  // A PSEUDO-CLASS IS A STATE THE ELEMENT CAN BE IN, and is therefore a live competitor for the
  // cascade rather than a reason to stop looking — a mouse is still sitting on the button it has
  // just pressed. Narrowing this to "only if the element is modelled as hovered" would exclude the
  // one class of rule the ranking below exists to catch.
  return true;
}

/** One branch of a selector — no commas — against this element. `undefined` when unreadable. */
function branchMatches(
  branch: string,
  element: Element,
  ancestors: readonly Element[],
): boolean | undefined {
  // THE SUBJECT DECIDES FIRST. A combinator only ever adds a constraint, so a branch whose last
  // compound cannot be this element is a definite no however it is joined. Asking about the
  // combinator first put `.sec-phrases > summary` — a rule for a disclosure heading, which can never
  // be a button — into the unreadable pile, and the first test to use this failed on its own guard
  // rather than on the thing it was written to find.
  const compounds = branch.trim().split(/[\s>+~]+/).filter(Boolean);
  const last = compounds.length === 0
    ? undefined
    : compoundMatches(compounds[compounds.length - 1] ?? '', element);
  if (last !== true) {
    return last;
  }
  // It could be the subject, and how it is joined to its ancestors is beyond this matcher.
  if (/[>+~]/.test(branch)) {
    return undefined;
  }
  let left = [...ancestors];
  let unreadable = false;
  const reached = compounds.slice(0, -1).every((compound) => {
    const found = left.findIndex((up) => compoundMatches(compound, up) === true);
    if (found < 0) {
      unreadable = left.some((up) => compoundMatches(compound, up) === undefined);

      return false;
    }
    left = left.slice(found + 1);

    return true;
  });

  return reached ? true : (unreadable ? undefined : false);
}

/** Could this selector paint `element` sitting inside `ancestors`? `undefined` when unreadable. */
export function couldMatch(
  selector: string,
  element: Element,
  ancestors: readonly Element[],
): boolean | undefined {
  let unreadable = false;
  for (const branch of selector.split(',')) {
    const answer = branchMatches(branch, element, ancestors);
    if (answer === true) {
      return true;
    }
    unreadable = unreadable || answer === undefined;
  }

  return unreadable ? undefined : false;
}

/**
 * How strongly this selector paints THIS element — the specificity of the branch that reaches it.
 *
 * <p><b>Per branch, never over the whole list.</b> CSS ranks each branch of a grouped selector on its
 * own, and counting the group as one cost this its first honest verdict: `.msg:hover .copy,
 * .msg:focus-within .copy, .msg .copy:focus` counts six classes and three pseudo-classes together,
 * which outranks everything, while the branch that actually reaches the element is worth (0,3,0) and
 * loses to the rule under test. A test asserted it was being beaten by a rule that cannot beat it.</p>
 *
 * <p>The strongest matching branch wins, which is what the cascade does when two branches of one
 * rule both reach an element.</p>
 */
export function rankFor(selector: string, element: Element, ancestors: readonly Element[]): Specificity {
  let best: Specificity = [0, 0, 0];
  for (const branch of selector.split(',')) {
    if (branchMatches(branch, element, ancestors) === true && outranks(specificity(branch), best) > 0) {
      best = specificity(branch);
    }
  }

  return best;
}

/**
 * Does a rule decide a colour at all? One that does not cannot win or lose that argument.
 *
 * <p>`background-` longhands are in, and that was a code-round finding rather than foresight: a rule
 * setting `background-color` alone restores a fill just as completely as `background` does, and a
 * check that looked only for the shorthand would have ranked it out of the contest.</p>
 *
 * <p>`opacity` is in for the same reason one step further out: a control held at `.55` shows its
 * colour at `.55`, so a rule that dims it decides how it looks as surely as one that recolours it.
 * The chat tab's copy controls are dimmed until their message is hovered, which is exactly the rule
 * an acknowledgement has to win.</p>
 */
export function paints(rule: Rule): boolean {
  return /(^|;)\s*(color|background(-[a-z]+)?|opacity)\s*:/.test(rule.body);
}

/**
 * The rules that could paint `element`, and the ones written in a form {@link couldMatch} cannot
 * read — which a caller must assert about rather than ignore.
 */
export function painters(
  sheet: readonly Rule[],
  element: Element,
  ancestors: readonly Element[],
): { readonly matching: Rule[]; readonly unreadable: Rule[] } {
  const colouring = sheet.filter(paints);

  return {
    matching: colouring.filter((rule) => couldMatch(rule.selector, element, ancestors) === true),
    unreadable: colouring.filter((rule) => couldMatch(rule.selector, element, ancestors) === undefined),
  };
}

/**
 * Of the rules that match, the ones that beat `rule` — by specificity, or by coming later on a tie.
 *
 * <p>Ranked by {@link rankFor}, so a grouped selector is judged on the branch that actually reaches
 * this element rather than on the whole list added together.</p>
 */
export function beating(
  rule: Rule,
  matching: readonly Rule[],
  element: Element,
  ancestors: readonly Element[],
): Rule[] {
  const rank = rankFor(rule.selector, element, ancestors);

  return matching.filter((other) => {
    if (other === rule) {
      return false;
    }
    const against = outranks(rankFor(other.selector, element, ancestors), rank);

    return against > 0 || (against === 0 && other.at > rule.at);
  });
}
