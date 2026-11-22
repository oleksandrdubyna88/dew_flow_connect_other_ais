/**
 * The cold-start marks the old code names (v2 E2 S2; the recorder is `shell/measure.ts`).
 *
 * <p>The sidebar's `ready` acknowledgement and the first state handed to its webview are moments only the old code knows. The shell's marks are
 * written by `shell/measure.ts`, which the old code must not import (`src/` does not reach into `shell/`, and the entry's firewall forbids the
 * other direction), so the legacy module installs the shell's recorder here and the old code calls {@link measureMark}. With no recorder — the unit
 * tests, any caller outside the shell — a mark is nothing. This file imports nothing, so a unit test can load it.</p>
 */

/**
 * The moments the old code names, in the order the old activation and the first sidebar paint reach them (v2 E2 S2, step marks).
 *
 * <p>`oldActivate*` are the main phases of `src/extension.ts` `activate()`; `sidebarResolve` is the sidebar's `resolveWebviewView`; `render*` are the phases
 * of the FIRST sidebar render (`PanelProvider.renderNow`: each awaited read of the state). `firstPaint` is the sidebar's first html, handed to the webview from what is
 * known, before the slow reads (`src/firstPaint.ts`); `firstData` is the first state with its data, which ends M3; `ready` is the page's
 * acknowledgement. The names are the rows of the per-step table the measurement prints, so a name is a stable word, not a sentence.</p>
 */
export const OLD_MARKS = [
  'oldActivateStart',
  'oldNotifications',
  'oldStorageSide',
  'oldPathWatch',
  'oldBindings',
  'oldWatchersBuilt',
  'oldRoundsLog',
  'oldPanelBuilt',
  'oldWatchersStarted',
  'oldFeaturesCache',
  'oldSettingsSync',
  'oldMirrorStarted',
  'oldMigrationsKicked',
  'oldChatStore',
  'oldHousekeeping',
  'oldCommandsRegistered',
  'oldActivateEnd',
  'sidebarResolve',
  'renderStart',
  'renderWrites',
  'firstPaint',
  'renderCliModels',
  'renderPublished',
  'renderSessions',
  'renderFeatures',
  'renderNewPageReads',
  'renderHostKind',
  'renderUsage',
  'renderStorage',
  'renderCliStatus',
  'renderCardPrices',
  'renderSnippet',
  'renderPrompts',
  'renderPlaces',
  'renderLocalEngines',
  'renderBugz',
  'renderDiscovery',
  'firstData',
  'ready',
] as const;

/** A moment the old code names. */
export type OldMark = typeof OLD_MARKS[number];

let recorder: ((name: OldMark) => void) | undefined;

/** Installs the shell's recorder; `undefined` removes it. */
export function recordMarksWith(next: typeof recorder): void {
  recorder = next;
}

/** One cold-start mark: a timestamp pushed onto a list, and nothing else. */
export function measureMark(name: OldMark): void {
  recorder?.(name);
}
