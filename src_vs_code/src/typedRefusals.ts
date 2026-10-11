import { escapeHtml } from './escapeHtml';
import { stableId } from './pathMark';

/**
 * Text typed into the OTHER side's box and refused — shown BESIDE the box, never as a toast (todo/PLAN_paths_per_side.md
 * E4.1 and E4.2): the box keeps what was typed, the reason is under it, and the box is `aria-invalid` and
 * `aria-describedby` the reason, so a person corrects the text where they typed it.
 *
 * <p><b>Why the host keeps it.</b> Whether a typed path is admitted can ask THIS host's disk (on Windows `/work` is
 * `C:\work` on the system drive, and one that exists is this side's), which the page's script cannot; so the host decides,
 * keeps the refusal in memory for its window, and repaints. A refusal is forgotten when the same box is saved, when the
 * window is reloaded, or — being only feedback, never a status — when another refusal for the same box replaces it.
 * Replaced, never changed in place.</p>
 */

/** What was typed into one box, and why it was not saved. */
export interface TypedRefusal {
  readonly typed: string;
  readonly reason: string;
}

/** Every box's refusal, by the box's key ({@link cliBoxKey}, {@link rootBoxKey}). */
export type TypedRefusals = Readonly<Record<string, TypedRefusal>>;

export const NO_REFUSALS: TypedRefusals = {};

/** The refusals with this box's replaced. */
export function withRefusal(refusals: TypedRefusals, box: string, refusal: TypedRefusal): TypedRefusals {
  return { ...refusals, [box]: refusal };
}

/** The refusals without this box's — what a save of the box leaves. */
export function withoutRefusal(refusals: TypedRefusals, box: string): TypedRefusals {
  return Object.fromEntries(Object.entries(refusals).filter(([key]) => key !== box));
}

/** The key of a row's CLI-path box of one family. */
export function cliBoxKey(vendor: string, family: string): string {
  return `cli|${vendor}|${family}`;
}

/** The key of a list's typed box (`qconsultRoots`), by the group it types into. */
export function rootBoxKey(list: string, group: string): string {
  return `root|${list}|${group}`;
}

/** The id of the sentence a refused box points at — stable for one box, so two renders agree. */
export function refusalId(box: string): string {
  return stableId('typed-refusal', box);
}

/** What a refused box adds to its `aria-describedby` and `aria-invalid` — nothing when it was not refused. */
export function refusalAttributes(refusal: TypedRefusal | undefined, box: string): { readonly describedBy: string; readonly invalid: string } {
  return refusal === undefined ? { describedBy: '', invalid: '' } : { describedBy: ` ${refusalId(box)}`, invalid: ' aria-invalid="true"' };
}

/** The sentence under a refused box, or nothing. */
export function refusalNote(refusal: TypedRefusal | undefined, box: string): string {
  return refusal === undefined
    ? ''
    : `<div class="hint stale typed-refusal" id="${refusalId(box)}" role="alert">${escapeHtml(refusal.reason)}</div>`;
}
