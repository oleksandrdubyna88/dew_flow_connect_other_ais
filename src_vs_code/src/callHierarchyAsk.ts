import {
  CallEnd, Calls, columnOf, distinct, methodOf, NO_SIDE, Prepared, PreparedItem, Side, theRightSymbol,
} from './callHierarchy';

/**
 * Asking the editor who calls a method — the one place that touches a language provider.
 *
 * <p>Everything decidable is in `callHierarchy.ts`, where it is a unit test; this is what remains
 * once the decisions are made, and it takes its world as a parameter so the whole flow runs without
 * an editor.</p>
 *
 * <p><b>The two directions are independent.</b> One preparation, then incoming and outgoing asked
 * separately, each with its own failure. A provider that can answer one and not the other must still
 * answer the one — a reviewer caught the first draft making outgoing conditional on incoming, which
 * would have lost half of an explicitly promised feature to an unrelated timeout.</p>
 *
 * <p><b>There is no process to kill and no token to pass.</b> A language server belongs to VS Code,
 * is shared with every other extension and with the editor's own features, and an extension has no
 * handle, no pid and no API for it. Nor do the three commands take a `CancellationToken`:
 * `vscode.prepareCallHierarchy` takes a URI and a position, the two direction commands take an item,
 * and that is all. What exists is a bounded WAIT after which we stop listening and say `failed` —
 * never zero — and a row whose control is pressed again supersedes its own request rather than
 * accumulating a second. An earlier draft of this block said a token was passed; it was not, and a
 * comment that says so is worse than none.</p>
 */

/** What a row knows about the method it is about. */
export interface AskedAbout {
  readonly findingId: number;
  readonly attempt: string;
  readonly file: string;
  /** 1-based, as a finding records it; 0 when none was recorded. */
  readonly line: number;
  readonly symbolName: string;
}

/**
 * One symbol a preparation found, WITH the handle that goes back to the provider.
 *
 * <p>One value rather than two arrays indexed in parallel, because a parallel index is a contract
 * nothing enforces: an adapter that filters or reorders one and not the other would pair a name with
 * another symbol's handle, and the failure would look like a correct answer about the wrong method —
 * this story's own worst case, reached by a refactor rather than by a bug. (Code round 2, codex.)
 * The first draft carried a single handle and asked about `items[0]`; the second carried two arrays;
 * this one cannot be got wrong.</p>
 */
export interface PreparedSymbol {
  readonly item: PreparedItem;
  readonly handle: unknown;
}

/** What a preparation found. Empty means no provider answered. */
export type Preparation = readonly PreparedSymbol[];

/** Everything this reaches the editor through. */
export interface Editor {
  /** The text of one line, or undefined when the file is not in the checkout. */
  readonly lineText: (file: string, line: number) => Promise<string | undefined>;

  /** Prepare at a position. Empty items means no provider answered. */
  readonly prepare: (file: string, line: number, character: number) => Promise<Preparation>;

  /** Who calls the prepared symbol. */
  readonly incoming: (handle: unknown) => Promise<readonly CallEnd[]>;

  /** What the prepared symbol calls. */
  readonly outgoing: (handle: unknown) => Promise<readonly CallEnd[]>;
}

/** Long enough for a cold language service, short enough that a row is not stuck. */
export const BUDGET_MS = 20_000;

/**
 * One press: find the symbol, prove it is the row's, then ask both directions.
 *
 * <p>Nothing is asked until the symbol is proved, because a count about the wrong symbol is the
 * failure this story is most afraid of — and the measurement showed exactly how it happens.</p>
 */
export async function askCalls(
  editor: Editor,
  about: AskedAbout,
  budgetMs: number = BUDGET_MS,
): Promise<Calls> {
  const of = methodOf(about.file, about.line, about.symbolName);
  const prepared = await preparedAt(editor, about, budgetMs);
  if (prepared.why !== 'ok') {
    return {
      findingId: about.findingId, about: of, attempt: about.attempt,
      prepared: prepared.why, incoming: NO_SIDE, outgoing: NO_SIDE,
    };
  }

  // Both directions, independently, and neither can take the other down with it.
  const [incoming, outgoing] = await Promise.all([
    sideOf(() => editor.incoming(prepared.handle), budgetMs),
    sideOf(() => editor.outgoing(prepared.handle), budgetMs),
  ]);

  return { findingId: about.findingId, about: of, attempt: about.attempt, prepared: 'ok', incoming, outgoing };
}

/** The symbol, or the reason there is not one to ask about. */
async function preparedAt(
  editor: Editor,
  about: AskedAbout,
  budgetMs: number,
): Promise<{ readonly why: Prepared; readonly handle: unknown }> {
  if (about.line <= 0 || about.symbolName.length === 0) {
    return { why: 'moved', handle: undefined };
  }

  const text = await within(() => editor.lineText(about.file, about.line - 1), budgetMs, undefined);
  if (text === undefined) {
    // A file that is not in the checkout: measured, the editor THROWS rather than answering empty,
    // and the adapter turns that into an absent line. Told apart from "no provider" on purpose.
    return { why: 'gone', handle: undefined };
  }

  const column = columnOf(text, about.symbolName);
  if (column < 0) {
    return { why: 'moved', handle: undefined };
  }

  return await askedFor(editor, about, column, budgetMs);
}

async function askedFor(
  editor: Editor,
  about: AskedAbout,
  column: number,
  budgetMs: number,
): Promise<{ readonly why: Prepared; readonly handle: unknown }> {
  const prepared = await within(
    () => editor.prepare(about.file, about.line - 1, column), budgetMs, undefined);
  if (prepared === undefined) {
    return { why: 'failed', handle: undefined };
  }

  if (prepared.length === 0) {
    return { why: 'no-provider', handle: undefined };
  }

  const which = theRightSymbol(prepared.map((one) => one.item), about.symbolName);
  const found = prepared[which];

  return found === undefined
    ? { why: 'moved', handle: undefined }
    : { why: 'ok', handle: found.handle };
}

/** One direction, with its own failure and its own budget. */
async function sideOf(ask: () => Promise<readonly CallEnd[]>, budgetMs: number): Promise<Side> {
  const ends = await within(ask, budgetMs, undefined);

  return ends === undefined
    ? { asked: true, failed: true, ends: [] }
    : { asked: true, failed: false, ends: distinct(ends) };
}

/**
 * A bounded wait, where a rejection and a timeout are the same thing to the caller: we stopped
 * listening and learned nothing.
 */
async function within<T>(doing: () => Promise<T>, budgetMs: number, orElse: undefined): Promise<T | undefined> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([
      doing(),
      new Promise<undefined>((resolve) => {
        timer = setTimeout(() => resolve(orElse), budgetMs);
      }),
    ]);
  } catch {
    return orElse;
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}
