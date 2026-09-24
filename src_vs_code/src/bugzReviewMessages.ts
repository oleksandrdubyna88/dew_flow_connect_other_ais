/**
 * What the bugs review page may post to its panel, and the one place raw webview data becomes it.
 *
 * <p>Moved out of `bugzReviewPanel.ts` unchanged when issue #487 added `choose` and that file stood at
 * the `max-lines` cap — and moved to a module with no `vscode` in it, so the guard is RUN by a test
 * rather than read (the precedent is `roundsLogMessages.ts`).</p>
 */

/**
 * What the page can post, as a UNION with required fields per kind rather than a bag of optionals.
 *
 * <p>A bag compiles whatever is missing. A malformed `zoom` became a zero-delta write, a `decide`
 * with no ids became a decision about nothing, and the page and this side could drift apart without
 * a type error anywhere — which is the argument a code reviewer made, and it only gets worse as
 * epics 2 to 4 add actions. The shapes below are what the page actually sends; {@link asReviewMessage}
 * is the one place raw webview data becomes one of them.</p>
 *
 * <p>`ready` is deliberately absent. The page announces itself and this side has nothing to do
 * about it, so it belongs with the messages that are DROPPED rather than with the ones that are
 * handled — a member here would be a case the switch has to answer for and never receives.</p>
 */
export type ReviewMessage =
  | { readonly type: 'decide'; readonly ids: readonly number[]; readonly keep: number }
  | { readonly type: 'expand'; readonly ids: readonly number[]; readonly open: boolean }
  | { readonly type: 'expandAll'; readonly ids: readonly number[]; readonly open: boolean }
  | { readonly type: 'zoom' | 'tone'; readonly delta: number }
  /** A filter strip was pressed: which strip, and the key of the tab in it. */
  | { readonly type: 'tab'; readonly strip: string; readonly key: string }
  /** The un-anonymised view was switched; held so the next paint draws the same view. */
  | { readonly type: 'realText'; readonly on: boolean }
  /** An open row wants its real method; the generation is echoed back so a late answer can be told stale. */
  | { readonly type: 'fetchReal'; readonly id: number; readonly generation: string }
  /** A row's file was asked for — at the commit the reviewers read, or as it is now. */
  | { readonly type: 'openAt' | 'openCurrent' | 'openTree' | 'calls'; readonly id: number }
  /** A row's *CoAI: choose* was pressed: its pair goes into a chat, unsent (issue #487). */
  | { readonly type: 'choose'; readonly id: number }
  | { readonly type: 'openCall'; readonly at: string }
  /**
   * A person's words about one pair: a `draft` on every keystroke (held, never written), a `comment`
   * on a pause or when the box was left (held AND written).
   */
  | { readonly type: 'comment' | 'draft'; readonly id: number; readonly text: string };

/** Whole numbers only, junk dropped — an id is a row this database has or it is nothing. */
const numbers = (raw: unknown): readonly number[] =>
  (Array.isArray(raw) ? raw : []).map(Number).filter(Number.isFinite);

/** A message as the page posted it, before it is shown to be anything. */
type Raw = Readonly<Record<string, unknown>>;

/**
 * A WHOLE, non-negative number, or nothing — an id that is anything else names no row. `Number.isFinite`
 * alone let `1.5` and `-1` through to a server call that could only refuse them. (Code round, codex.)
 */
const wholeId = (value: unknown): boolean => Number.isInteger(Number(value)) && Number(value) >= 0;

/** The kinds that carry one row id and nothing else — the same rule for every one of them. */
const byId = (type: 'openAt' | 'openCurrent' | 'openTree' | 'calls' | 'choose') =>
  (m: Raw): ReviewMessage | undefined => (wholeId(m['id']) ? { type, id: Number(m['id']) } : undefined);

/**
 * A person's words about one row. The text must BE text: `String(undefined)` would write the word
 * "undefined" as somebody's comment. The rule on WHAT text is `--pairs-decide`'s.
 */
const words = (type: 'comment' | 'draft') =>
  (m: Raw): ReviewMessage | undefined =>
    (wholeId(m['id']) && typeof m['text'] === 'string' ? { type, id: Number(m['id']), text: m['text'] } : undefined);

/** A zoom or tone step. `NaN` reaching `clampScale` would silently RESET somebody's zoom, so it is dropped. */
const step = (type: 'zoom' | 'tone') =>
  (m: Raw): ReviewMessage | undefined =>
    (Number.isFinite(Number(m['delta'])) ? { type, delta: Number(m['delta']) } : undefined);

/**
 * One parser per kind the page sends. A `Map`, not a plain object: `{"type":"__proto__"}` finds
 * `Object.prototype` on a plain lookup table, and calling it raises a `TypeError` rather than being
 * ignored — a `Map` answers every key it was not given with `undefined`. It replaced a switch of 29
 * branches when the guard moved here (issue #487) and CI refused to carry that switch's suppression to a
 * new file; `ready` is absent on purpose, as in the union.
 */
const PARSERS: ReadonlyMap<string, (m: Raw) => ReviewMessage | undefined> = new Map<string, (m: Raw) => ReviewMessage | undefined>([
  ['decide', (m) => ({ type: 'decide', ids: numbers(m['ids']), keep: Number(m['keep']) })],
  ['expand', (m) => ({ type: 'expand', ids: numbers([m['id']]), open: m['open'] === true })],
  ['expandAll', (m) => ({ type: 'expandAll', ids: numbers(m['ids']), open: m['open'] === true })],
  ['tab', (m) => ({ type: 'tab', strip: String(m['strip']), key: String(m['key']) })],
  ['realText', (m) => ({ type: 'realText', on: m['on'] === true })],
  // A generation that is not a string could never match what the page holds: junk, dropped.
  ['fetchReal', (m) => (wholeId(m['id']) && typeof m['generation'] === 'string'
    ? { type: 'fetchReal', id: Number(m['id']), generation: m['generation'] }
    : undefined)],
  ['openCall', (m) => (typeof m['at'] === 'string' ? { type: 'openCall', at: m['at'] } : undefined)],
  ['comment', words('comment')],
  ['draft', words('draft')],
  ['openAt', byId('openAt')],
  ['openCurrent', byId('openCurrent')],
  ['openTree', byId('openTree')],
  ['calls', byId('calls')],
  ['choose', byId('choose')],
  ['zoom', step('zoom')],
  ['tone', step('tone')],
]);

/**
 * Raw webview data, turned into one of the shapes above or into nothing at all.
 *
 * <p>Three things this has to survive, all of which a reviewer named and none of which the page
 * sends today — which is the point, because what arrives here is only the page's while nothing has
 * gone wrong. `null` and `undefined` throw on the first field read; `__proto__` is answered by the
 * `Map` above; a junk `delta` is dropped by {@link step}.</p>
 */
export function asReviewMessage(raw: unknown): ReviewMessage | undefined {
  const m: Raw = typeof raw === 'object' && raw !== null ? raw as Raw : {};
  // `String` of anything that is not one of the kinds is a key the Map was never given.
  const parse = PARSERS.get(String(m['type']));

  return parse === undefined ? undefined : parse(m);
}
