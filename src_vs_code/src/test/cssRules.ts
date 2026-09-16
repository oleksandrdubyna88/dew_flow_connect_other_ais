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
  const pattern = /([^{}]+)\{([^{}]*)\}/g;
  let consumed = 0;
  const skipped: string[] = [];
  for (let found = pattern.exec(css); found !== null; found = pattern.exec(css)) {
    const from = pattern.lastIndex - found[0].length;
    if (from > consumed) {
      skipped.push(css.slice(consumed, from));
    }
    consumed = pattern.lastIndex;
    rules.push({
      selector: (found[1] ?? '').trim().replace(/\s+/g, ' '),
      body: (found[2] ?? '').trim(),
      at: rules.length,
    });
  }
  skipped.push(css.slice(consumed));

  // THE PROOF THAT THIS FLAT PARSER WAS ALLOWED TO BE FLAT. An at-rule, a nested selector or a brace
  // inside a quoted value leaves text behind, and text left behind means the rules that follow it
  // have slid out of alignment — every one of them still looks like a rule, which is why this cannot
  // be left to inspection.
  const left = skipped.map((text) => text.trim()).filter((text) => text.length > 0);
  assert.deepEqual(left, [], 'the stylesheet did not parse as flat rules, so every verdict over it is unsound');
  assert.ok(rules.length > 0, 'the stylesheet parsed into no rules at all');

  return rules;
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

/** Could this selector paint `element` sitting inside `ancestors`? `undefined` when unreadable. */
export function couldMatch(
  selector: string,
  element: Element,
  ancestors: readonly Element[],
): boolean | undefined {
  let unreadable = false;
  for (const branch of selector.split(',')) {
    // THE SUBJECT DECIDES FIRST. A combinator only ever adds a constraint, so a branch whose last
    // compound cannot be this element is a definite no however it is joined. Asking about the
    // combinator first put `.sec-phrases > summary` — a rule for a disclosure heading, which can
    // never be a button — into the unreadable pile, and the first run of the test that uses this
    // failed on its own guard rather than on the thing it was written to find.
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

/**
 * Does a rule decide a colour at all? One that does not cannot win or lose that argument.
 *
 * <p>`background-` longhands are in, and that was a code-round finding rather than foresight: a rule
 * setting `background-color` alone restores a fill just as completely as `background` does, and a
 * check that looked only for the shorthand would have ranked it out of the contest.</p>
 */
export function paints(rule: Rule): boolean {
  return /(^|;)\s*(color|background(-[a-z]+)?)\s*:/.test(rule.body);
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

/** Of the rules that match, the ones that beat `rule` — by specificity, or by coming later on a tie. */
export function beating(rule: Rule, matching: readonly Rule[]): Rule[] {
  const rank = specificity(rule.selector);

  return matching.filter((other) => {
    if (other === rule) {
      return false;
    }
    const against = outranks(specificity(other.selector), rank);

    return against > 0 || (against === 0 && other.at > rule.at);
  });
}
