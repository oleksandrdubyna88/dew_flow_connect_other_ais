import { bySideOf, familyLabel, type ExecutableBySide, type PathFamily, type StoredExecutable } from './pathFamily';

/**
 * A kept copy of every catalog row's per-side CLI path, and the one-click way back when a save drops it
 * (todo/PLAN_paths_per_side.md, E3 as built: the plan-gate's Major finding and the risk consultant's findings a and b).
 *
 * <p><b>Why it exists.</b> A released extension (v0.67.0, v0.65.0 — measured by `scripts/live-paths-per-side-compat.mjs`)
 * rebuilds every row from the fields it knows when it saves `coai.vendors`, so ANY vendor save it makes removes
 * `executablePathBySide` from every row: the side that did not save loses its own CLI path without a word. The newer side
 * cannot stop that, but it can SAY it and offer the value back.</p>
 *
 * <p><b>What it never does</b> (the consultant's advice, verified): it never restores on its own — a missing member looks the
 * same whether an older save dropped it, a person removed it by hand, or Settings Sync brought another machine's list — and
 * it never recreates a row. It says once, and **Restore** writes only after a click, re-reading the row at that moment.</p>
 *
 * <ul>
 *   <li><b>Where:</b> `globalState`, which the Windows and the WSL window of one profile share (`sidesSeen.ts`, measured) and
 *       Settings Sync never carries (no `setKeysForSync`) — so another machine's copy can never be offered here.</li>
 *   <li><b>One key per layer and row</b> — `coai.cliPathKept@<layer>@<identity>` — so two windows never read-modify-write one
 *       value (the `sidesSeen.ts` pattern), and a copy of the user layer is never offered for a row of a side's own
 *       settings.</li>
 *   <li><b>Identity is id + runtime + model</b>, not the id alone: `freeVendorId` hands a removed row's id to the next row
 *       added, so an id alone would offer one CLI's path to another. A row whose runtime or model changed is treated as
 *       another row — it never inherits the copy, and the old copy is dropped when no row carries that identity.</li>
 *   <li>The copy holds the member as it was stored — `mirrored` included, and an explicitly EMPTY value kept as empty (the PATH
 *       lookup), never as absent.</li>
 * </ul>
 *
 * <p>Pure and `vscode`-free: the store is structural (a `vscode.Memento` is one), the settings and the voice are handed in —
 * `cliPathKeepHost.ts` is the wiring.</p>
 */

/** Every kept copy lives under this prefix, then its layer, `@`, and the row's identity. */
export const CLI_PATH_KEPT_PREFIX = 'coai.cliPathKept@';

/** The part of a `vscode.Memento` the keeper uses — `context.globalState` is one. */
export interface KeptStore {
  keys(): readonly string[];
  get(key: string): unknown;
  /** `undefined` deletes the key, as `Memento.update` does. */
  update(key: string, value: unknown): PromiseLike<void>;
}

/** A catalog row as the keeper sees it: who it is, and its CLI path. */
export interface KeptRow extends StoredExecutable {
  readonly id: string;
  readonly runtime: string;
  readonly model: string;
}

/** A row whose per-side member is gone while a copy of it is kept — what the notice offers back. */
export interface DroppedValue {
  /** The store key of the copy — also the notice's subject, so two rows are two notices. */
  readonly key: string;
  readonly id: string;
  readonly runtime: string;
  readonly model: string;
  /** The copy, as it was kept. */
  readonly kept: ExecutableBySide;
  /** The legacy value the row held when this was found — a restore after it changed is refused. */
  readonly legacy: string;
}

/** The store key of a row's copy in a layer. */
export function keptKey(layer: string, row: Pick<KeptRow, 'id' | 'runtime' | 'model'>): string {
  return `${layerPrefix(layer)}${JSON.stringify([row.id, row.runtime, row.model])}`;
}

function layerPrefix(layer: string): string {
  return `${CLI_PATH_KEPT_PREFIX}${layer}@`;
}

/** A kept copy as the store holds it, or nothing — whatever else was stored reads as nothing, never as an error. */
export function keptCopy(stored: unknown): ExecutableBySide | undefined {
  return typeof stored === 'object' && stored !== null ? bySideOf((stored as { bySide?: unknown }).bySide) : undefined;
}

/** What one look at the rows decides: the copies to write or delete, and the rows whose member is gone. */
export interface KeepPlan {
  /** `[key, value]` — `undefined` deletes. */
  readonly writes: readonly (readonly [string, unknown])[];
  readonly dropped: readonly DroppedValue[];
}

/**
 * One look at a layer's rows: a row that carries its member has it copied (written only when it differs from the copy); a
 * row without it whose copy exists is DROPPED; a copy no row of the layer carries any more is deleted — a removed row, or
 * one whose runtime or model changed, never inherits it.
 */
export function keepPlan(store: Pick<KeptStore, 'keys' | 'get'>, layer: string, rows: readonly KeptRow[]): KeepPlan {
  const looks = rows.map((row) => lookAt(store, layer, row));
  const carried = new Set(looks.map((look) => look.key));
  const stale = store.keys().filter((key) => key.startsWith(layerPrefix(layer)) && !carried.has(key));

  return {
    writes: [...looks.flatMap((look) => look.write), ...stale.map((key) => [key, undefined] as const)],
    dropped: looks.flatMap((look) => look.dropped),
  };
}

interface Look {
  readonly key: string;
  readonly write: readonly (readonly [string, unknown])[];
  readonly dropped: readonly DroppedValue[];
}

function lookAt(store: Pick<KeptStore, 'get'>, layer: string, row: KeptRow): Look {
  const key = keptKey(layer, row);
  const kept = keptCopy(store.get(key));
  const member = row.executablePathBySide;
  if (member !== undefined) {
    return { key, write: sameMembers(kept, member) ? [] : [[key, { bySide: member }]], dropped: [] };
  }

  return { key, write: [], dropped: kept === undefined ? [] : [droppedOf(key, row, kept)] };
}

function droppedOf(key: string, row: KeptRow, kept: ExecutableBySide): DroppedValue {
  return { key, id: row.id, runtime: row.runtime, model: row.model, kept, legacy: row.executablePath.trim() };
}

/** Whether two per-side members hold the same values — an empty one is a value, an absent one is not. */
function sameMembers(one: ExecutableBySide | undefined, other: ExecutableBySide): boolean {
  return one !== undefined && JSON.stringify(ordered(one)) === JSON.stringify(ordered(other));
}

function ordered(member: ExecutableBySide): readonly (string | undefined)[] {
  return [member.windows, member.posix, member.mirrored];
}

/**
 * The rows after **Restore**, decided on the rows as they are AT THE CLICK — or the reason nothing is written. The copy goes
 * back into the one row it was taken from, exactly as kept, and the legacy `executablePath` is left as it is: a later save
 * by an extension that does not know the member is then still DETECTED (legacy ≠ the kept `mirrored`) and still wins for the
 * family it is spelled for, so restoring the Windows value after a WSL save of P′ leaves the WSL side running P′ and the
 * legacy value P′ (a save of the Windows field would have mirrored the Windows value into it — never this).
 *
 * <p>Refused: a row that is gone (never recreated), a row that carries a member again (a later save — never overwritten), a
 * row whose legacy value changed since the offer (a later edit), and a copy that changed since the offer.</p>
 *
 * @param keptNow the copy as the store holds it at the click
 */
export function restoredRows<R extends KeptRow>(rows: readonly R[], offer: DroppedValue, keptNow: ExecutableBySide | undefined): R[] | string {
  const row = rows.find((one) => sameIdentity(one, offer));
  const refusal = row === undefined ? 'the model is no longer in the list' : staleness(row, offer, keptNow);

  return refusal.length > 0 ? refusal : rows.map((one) => (one === row ? { ...one, executablePathBySide: offer.kept } : one));
}

function sameIdentity(row: KeptRow, offer: DroppedValue): boolean {
  return row.id === offer.id && row.runtime === offer.runtime && row.model === offer.model;
}

/**
 * The notice's words: "a previously saved value", never who removed it — a missing member looks the same whether an older
 * extension's save dropped it, a person removed it, or another machine's settings arrived — and every kept value named,
 * an empty one as empty (the PATH lookup).
 */
export function droppedNoticeText(dropped: DroppedValue): { readonly title: string; readonly cure: string } {
  const values = FAMILIES.flatMap((family) => keptValueOf(dropped.kept, family)).join('; ');

  return {
    title: `The CLI path of the model "${dropped.id}" no longer holds a previously saved value — ${values}.`,
    cure: 'Restore puts it back and leaves the path your settings hold now as it is; nothing changes unless you press it. '
      + 'Setting the CLI path on Settings › Models replaces it.',
  };
}

const FAMILIES: readonly PathFamily[] = ['windows', 'posix'];

function keptValueOf(kept: ExecutableBySide, family: PathFamily): readonly string[] {
  const value = kept[family];

  return value === undefined ? [] : [`${familyLabel(family)}: ${value.length === 0 ? 'empty (looked up on PATH)' : value}`];
}

/** Why a restore at the click would overwrite something newer than the offer — empty when it would not. */
function staleness(row: KeptRow, offer: DroppedValue, keptNow: ExecutableBySide | undefined): string {
  const checks: readonly (readonly [boolean, string])[] = [
    [row.executablePathBySide !== undefined, 'a CLI path was saved for it since'],
    [row.executablePath.trim() !== offer.legacy, 'its CLI path was changed since'],
    [!sameMembers(keptNow, offer.kept), 'the saved value changed since'],
  ];

  return checks.find(([stale]) => stale)?.[1] ?? '';
}
