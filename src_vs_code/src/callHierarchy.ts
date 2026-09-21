/**
 * Who calls a method, and what it calls — the decisions, as values.
 *
 * <p><b>Delegate, do not compute.</b> VS Code answers this through `prepareCallHierarchy`,
 * `provideIncomingCalls` and `provideOutgoingCalls`. References are a DIFFERENT question and are
 * never substituted for callers.</p>
 *
 * <p><b>Three facts were measured in a real editor before any of this was written</b>
 * (`research/module_tests.md`, story 3.3's gate), and each one is a rule here:</p>
 * <ul>
 *   <li>a language with no provider prepares NOTHING, so <i>unavailable</i> is distinguishable from
 *       <i>zero</i> at the first call;</li>
 *   <li>asking at column 0 of an indented method prepares the ENCLOSING CLASS — `Totals` where the
 *       method is `counted` — so the column is found from the line's text AND the prepared name is
 *       checked against the one the row records. Two guards, designed independently, and the
 *       measurement showed them agreeing;</li>
 *   <li>a file that no longer exists makes the call THROW, which is how <i>the file is gone</i> is
 *       told from <i>there is no provider</i> without guessing.</li>
 * </ul>
 *
 * <p><b>The numbers are about the CURRENT checkout, and every sentence says so.</b> A call hierarchy
 * is answered by the language server of the window that asks, over the folder that window has open —
 * and a review row is about `head_sha`, orphaned 55.7 % of the time. Story 2.1 set this precedent
 * with *Open CURRENT (may differ)*; this is the same honesty for the same reason. Asking the review
 * tree's own window is not possible: VS Code gives an extension no way to run a command in another
 * window, which is the gap story 3.2c exists for.</p>
 */

/** Where one end of a call is, as a person would go to it. */
export interface CallEnd {
  readonly name: string;
  readonly file: string;
  /** 0-based, as the editor counts. */
  readonly line: number;
  readonly character: number;
  /** The signature, when the provider offers one — what tells two overloads apart. */
  readonly detail: string;
}

/**
 * Why a method could not be asked about. Each is a different thing for a person to do next, which is
 * why they are not one word.
 */
export type Prepared =
  /** The symbol was found and is the one the row is about. */
  | 'ok'
  /** Something is at that line, and it is not this method. It moved, or it was renamed. */
  | 'moved'
  /** The file is not in the current checkout at all. */
  | 'gone'
  /** No language provider answered — nobody could be asked, which is NOT nobody calls it. */
  | 'no-provider'
  /** The provider was asked and failed, or took too long. Worth asking again. */
  | 'failed';

/** One direction's answer. The two are independent: a provider can do one and fail the other. */
export interface Side {
  readonly asked: boolean;
  readonly failed: boolean;
  readonly ends: readonly CallEnd[];
}

/** Everything one press learned about one row. */
export interface Calls {
  readonly findingId: number;
  /**
   * The attempt this belongs to.
   *
   * <p>Not the draw generation, and the difference is the defect a reviewer found: a request can time
   * out, the person can press again within the SAME generation, and the first provider promise can
   * still resolve and overwrite the second answer. Every press mints a new attempt, and only the
   * attempt a row is currently waiting for may be applied.</p>
   */
  readonly attempt: string;
  readonly prepared: Prepared;
  readonly incoming: Side;
  readonly outgoing: Side;
}

/** Nothing asked for yet. */
export const NO_SIDE: Side = { asked: false, failed: false, ends: [] };

/**
 * The column a symbol starts at on its line, or -1 when it is not on that line at all.
 *
 * <p>Measured: asking at column 0 of `    public counted()` prepares `Totals`, the enclosing class,
 * and would have counted a class's callers under a method's name. So the column is found rather than
 * assumed — and when the name is not on the line, the row has moved and nothing is asked.</p>
 *
 * <p>The match is on a WORD boundary: `counted` must not be found inside `recounted`.</p>
 */
export function columnOf(lineText: string, symbolName: string): number {
  if (symbolName.length === 0) {
    return -1;
  }

  let from = 0;
  for (;;) {
    const at = lineText.indexOf(symbolName, from);
    if (at < 0) {
      return -1;
    }
    if (!wordish(lineText[at - 1]) && !wordish(lineText[at + symbolName.length])) {
      return at;
    }
    from = at + 1;
  }
}

function wordish(one: string | undefined): boolean {
  return one !== undefined && /[A-Za-z0-9_$]/u.test(one);
}

/** What a prepared item must be for its callers to be this row's callers. */
export interface PreparedItem {
  readonly name: string;
  readonly detail: string;
}

/**
 * WHICH prepared symbol is the one the row is about, as an index — or -1.
 *
 * <p><b>A name is not enough</b>, and a reviewer was right to say so: two overloads share one. So an
 * AMBIGUOUS preparation — more than one item carrying the row's name — is refused rather than
 * guessed at, because nothing in a review row can tell two overloads apart. One match is the only
 * answer that counts.</p>
 *
 * <p><b>And it returns WHICH, not whether.</b> The first draft answered a boolean and the caller then
 * asked the provider about `items[0]` — so a provider returning `[Totals, counted]` passed the check
 * on `counted` and had `Totals`'s callers counted under the method's name. Three reviewers found it
 * independently. The index is the fix, and it is the same defect the column measurement found one
 * level down.</p>
 */
export function theRightSymbol(items: readonly PreparedItem[], symbolName: string): number {
  const matching = items
    .map((one, at) => (one.name === symbolName ? at : -1))
    .filter((at) => at >= 0);

  return matching.length === 1 ? matching[0] ?? -1 : -1;
}

/** The sentence a row shows instead of a number, and it never prints the state's own word. */
export function preparedSentence(prepared: Prepared): string {
  return SAID[prepared] ?? 'this method could not be looked up';
}

const SAID: Readonly<Record<Prepared, string>> = {
  ok: '',
  moved: 'that line holds something else in the current checkout, so nothing was counted — the method moved or was renamed',
  gone: 'that file is not in the current checkout any more',
  'no-provider': 'no language support is installed for this file, so nobody could be asked — which is not the same as nobody calling it',
  failed: 'the language support did not answer in time — nothing was learned, so it is worth asking again',
};

/**
 * How a direction reads: a count, or why there is none.
 *
 * <p><b>Zero is a real answer and says so plainly.</b> Everything that is NOT an answer says
 * something else, because the whole point of the measurement was that the two are different.</p>
 */
export function sideSentence(side: Side, what: 'calls this' | 'is called by this'): string {
  if (!side.asked) {
    return '';
  }
  if (side.failed) {
    return `the language support could not say what ${what}`;
  }

  if (side.ends.length === 0) {
    return `nothing ${what}, in the current checkout`;
  }
  const methods = side.ends.length === 1 ? 'method' : 'methods';

  return `${side.ends.length} ${methods} ${what}, in the current checkout`;
}

/**
 * Distinct METHODS, never call sites.
 *
 * <p>The API is already shaped this way — one `CallHierarchyIncomingCall` per calling method, with
 * its `fromRanges` being the sites — so this is a guard against a future change rather than work the
 * count needs today, and it costs one pass over a list that is rarely longer than a screen.</p>
 */
export function distinct(ends: readonly CallEnd[]): readonly CallEnd[] {
  const seen = new Set<string>();

  return ends.filter((end) => {
    // WHERE as well as what: `A.run()` and `B.run()` in one file share a name and often a
    // signature, and keying without the position collapsed them into one caller. (Code round, two
    // reviewers.) The selection start is the provider's own stable identity for a symbol.
    const key = `${end.file}\u0000${end.line}:${end.character}\u0000${end.name}\u0000${end.detail}`;
    if (seen.has(key)) {
      return false;
    }
    seen.add(key);

    return true;
  });
}

/**
 * Whether an answer may be applied to a row.
 *
 * <p>Both halves matter and a reviewer found the second: the row must be the one that asked, AND the
 * attempt must be the one it is still waiting for. A generation alone lets a timed-out request
 * overwrite the retry a person made inside the same draw.</p>
 */
export function stillWanted(answer: Calls, waitingFor: ReadonlyMap<number, string>): boolean {
  return waitingFor.get(answer.findingId) === answer.attempt;
}
