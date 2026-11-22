/**
 * The cold-start marks the old code names (v2 E2 S2; the recorder is `shell/measure.ts`).
 *
 * <p>The sidebar's `ready` acknowledgement and the first state handed to its webview are moments only the old code knows. The shell's marks are
 * written by `shell/measure.ts`, which the old code must not import (`src/` does not reach into `shell/`, and the entry's firewall forbids the
 * other direction), so the legacy module installs the shell's recorder here and the old code calls {@link measureMark}. With no recorder — the unit
 * tests, any caller outside the shell — a mark is nothing. This file imports nothing, so a unit test can load it.</p>
 */

/** A moment the old code names. */
export type OldMark = 'ready' | 'firstData';

let recorder: ((name: OldMark) => void) | undefined;

/** Installs the shell's recorder; `undefined` removes it. */
export function recordMarksWith(next: typeof recorder): void {
  recorder = next;
}

/** One cold-start mark: a timestamp pushed onto a list, and nothing else. */
export function measureMark(name: OldMark): void {
  recorder?.(name);
}
