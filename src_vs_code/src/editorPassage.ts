/**
 * What a passage IS when it comes from an ordinary editor rather than from somebody else's webview.
 *
 * <p><b>This door needs none of `selectionCapture.ts`.</b> That file synthesises a keystroke through
 * PowerShell, borrows the clipboard and gives it back, and it exists for one measured reason: Claude
 * Code's panel offers no way to read what is selected inside it. An editor does. So the passage
 * here is read synchronously, with no clipboard to lose, no 1.7 seconds to wait, and no keystroke
 * that can land in the wrong window — and the menu-versus-keyboard split disappears with it, since
 * both doors read the same live selection rather than something copied at an unknown time.</p>
 *
 * <p>Pure: the caller narrows a `vscode.TextEditor` into the two strings this needs, which is the
 * same split `sessionKey.ts` keeps with the tab list.</p>
 */

/** An editor, narrowed to what the decision needs. */
export interface EditorText {
  /** Everything in the document. */
  readonly whole: string;
  /** What is selected, or empty when the selection is. */
  readonly selected: string;
  /** For the sentence a refusal says, and for the size a whole file is measured by. */
  readonly name: string;
}

/** What to send, or the sentence saying why nothing will be. Never both. */
export type EditorPassage =
  | { readonly ok: true; readonly text: string; readonly whole: boolean }
  | { readonly ok: false; readonly refusal: string };

/**
 * How large a document may be before the whole of it is sent without asking.
 *
 * <p>A selection is never questioned however large — that one was deliberate. An EMPTY selection is
 * the accident this bound exists for: the chord pressed to focus a window, in a minified bundle or
 * a log, becomes a paid turn nobody meant. Two vendors raised it independently on the plan round,
 * and they were right that a notification shown after the send is too late to be a choice.</p>
 *
 * <p>20 000 characters is a few hundred lines of prose — under it a whole file is an ordinary
 * question, over it is a decision worth one click.</p>
 */
export const WHOLE_FILE_LIMIT = 20_000;

/**
 * The passage for this editor: the selection, or the whole document when nothing is selected.
 *
 * <p>Whitespace-only counts as nothing selected — a stray drag is not a choice of passage.</p>
 */
export function passageFromEditor(editor: EditorText | undefined): EditorPassage {
  if (editor === undefined) {
    return { ok: false, refusal: 'Open a file and put the cursor in it — there is nothing to chat about.' };
  }
  if (editor.selected.trim().length > 0) {
    return { ok: true, text: editor.selected, whole: false };
  }
  if (editor.whole.trim().length === 0) {
    return { ok: false, refusal: `${editor.name} is empty — there is nothing to chat about.` };
  }

  return { ok: true, text: editor.whole, whole: true };
}

/**
 * Whether sending this whole document should be asked about first, and the sentence to ask with.
 *
 * <p>Separate from the passage itself so the question is a pure decision too: the host shows a
 * modal, it does not decide whether there should be one.</p>
 */
export function confirmWholeFile(editor: EditorText, limit = WHOLE_FILE_LIMIT): string {
  if (editor.whole.length <= limit) {
    return '';
  }
  // CHARACTERS, because that is what was counted. A KB computed from a UTF-16 length is wrong for
  // every file that is not ASCII, and this number is shown to a person as a reason to decide.
  // (gemini, the code round.)
  const thousands = Math.round(editor.whole.length / 1000);

  return `Nothing is selected, so this would send the whole of ${editor.name}`
    + ` — about ${thousands} thousand characters. Send all of it?`;
}
