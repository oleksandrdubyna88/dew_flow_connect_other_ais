import { Calls, preparedSentence, Side, sideSentence } from './callHierarchy';
import { escapeHtml } from './webviewHtml';

/**
 * What a row says about who calls its method — as markup, and nothing else.
 *
 * <p>Pure, and free of `node:` and `vscode` like every module the page bundle is built from. What a
 * row SAYS is decided in `callHierarchy.ts`; this only renders it.</p>
 *
 * <p><b>Every count is labelled *in the current checkout*, in the sentence itself.</b> Not in a
 * tooltip and not once at the top: a call hierarchy is answered by the language server of the window
 * that asks, over the folder that window has open, and a review row is about a commit that is
 * orphaned 55.7 % of the time. A number without that clause is the "convincing and wrong" this
 * story's plan named, and the row already shows which commit it is about beside it.</p>
 *
 * <p><b>A path is posted by INDEX, never as a path.</b> The page holds no file names for this: it
 * posts which row, which direction and which entry, and the panel — which has the answer — opens it.
 * A page that carried paths would be a page that could be asked to open one.</p>
 */

/**
 * How many ends a row lists before it stops and says how many more there are.
 *
 * <p>Enough to be useful, few enough that one `innerHTML` assignment cannot freeze a webview. The
 * count in the sentence above is never truncated.</p>
 */
const MOST_SHOWN = 50;

/** What the row is showing right now. */
export type CallsState =
  | { readonly phase: 'unasked' }
  | { readonly phase: 'asking' }
  | { readonly phase: 'answered'; readonly calls: Calls };

/** The control and, once there is one, the answer. */
export function callsBlock(findingId: number, state: CallsState): string {
  const id = escapeHtml(String(findingId));
  if (state.phase === 'unasked') {
    return `<div class="calls">${control(id)}</div>`;
  }
  if (state.phase === 'asking') {
    return `<div class="calls"><span class="why">asking the language support…</span></div>`;
  }

  return `<div class="calls">${control(id)}${answer(id, state.calls)}</div>`;
}

/**
 * The control says what it will ask about BEFORE it is pressed.
 *
 * <p>A reviewer asked for the mismatch to be visible before the press rather than only in the answer,
 * and this is how: the control never claims to be about the reviewed commit, and the row shows that
 * commit beside it. Reading the workspace's own HEAD to compare was the alternative and would have
 * meant parsing `.git/HEAD` and `packed-refs` by hand — re-implementing git to say something the
 * label can say plainly.</p>
 */
function control(id: string): string {
  return `<button type="button" class="quiet" data-calls="${id}" `
    + `title="Ask the language support who calls this method and what it calls, IN THE CURRENT CHECKOUT. `
    + `The row is about the commit the reviewers read, which may be a different one. The first ask in a file takes a moment.">`
    + `Who calls this?</button>`;
}

function answer(id: string, calls: Calls): string {
  if (calls.prepared !== 'ok') {
    return why(preparedSentence(calls.prepared));
  }

  return side(id, 'in', calls.incoming, 'calls this') + side(id, 'out', calls.outgoing, 'is called by this');
}

function side(id: string, which: 'in' | 'out', one: Side, what: 'calls this' | 'is called by this'): string {
  const said = why(sideSentence(one, what));
  if (one.failed || one.ends.length === 0) {
    return said;
  }

  // Bounded. The COUNT is the answer and the list is how a person reaches a few of them; a method
  // with ten thousand callers would otherwise become ten thousand buttons in one `innerHTML`
  // assignment. (Code round, codex.) The sentence above already says how many there are.
  const shown = one.ends.slice(0, MOST_SHOWN);
  const rows = shown
    .map((end, at) => `<li><button type="button" class="quiet" data-open-call="${id}:${which}:${at}">`
      + `${escapeHtml(end.name)}</button> <span class="why">${escapeHtml(end.file)}</span></li>`)
    .join('');
  const rest = one.ends.length > shown.length
    ? `<li><span class="why">and ${one.ends.length - shown.length} more</span></li>`
    : '';

  return `${said}<ul class="calls-list">${rows}${rest}</ul>`;
}

/** A sentence beside the control, quiet, so the control stays what a person reads first. */
function why(said: string): string {
  return said.length > 0 ? `<span class="why">${escapeHtml(said)}</span>` : '';
}

/** Which entry a press named: the row, the direction, and the position in that list. */
export function calledOut(attribute: string): { id: number; which: 'in' | 'out'; at: number } | undefined {
  const parts = attribute.split(':');
  if (parts.length !== 3 || (parts[1] !== 'in' && parts[1] !== 'out')) {
    return undefined;
  }
  const id = Number(parts[0]);
  const at = Number(parts[2]);

  return Number.isInteger(id) && id >= 0 && Number.isInteger(at) && at >= 0
    ? { id, which: parts[1], at }
    : undefined;
}
