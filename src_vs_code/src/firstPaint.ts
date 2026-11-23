import { SNIPPET_VERSION } from './claudeSnippet';
import type { PanelState } from './panelView';
import type { BugCorpus } from './roundsDb';

/**
 * The sidebar's first paint (v2 E2): the page is handed to the webview from what is in memory, and its slow cards are filled later.
 *
 * <p>The cold-start measurement (M2, `research/RESULTS_v2_start.md`) ends at the sidebar's `ready`, and `ready` can only come once the webview has
 * a document. The old first render awaited every read of the whole state — the published version over the network, each CLI's models and version,
 * the card prices, the corpus, the local engines — BEFORE the first `webview.html` was written, and the sidebar draws almost none of them
 * (`PANEL_SECTIONS`: notifications, gates, consultations, phrases, Bugz). This file is the half that has no `vscode` in it, so a test can
 * run the page it makes, and run the order it is made in.</p>
 *
 * <p>Two stages for the sidebar, then the old render as it was:</p>
 * <ol>
 *   <li><b>first</b> — the state from what is already known: the settings, the rows, the sessions (a read of local files), the live regions' own
 *       caches. The Bugz card says it is loading. Nothing here spawns, asks the network or probes.</li>
 *   <li><b>fill</b> — what only the sidebar's own cards need and is slow: the corpus, the local engines, the server's feature list. The sidebar is painted again when they land.</li>
 * </ol>
 */

/** What the sidebar's page is drawn from. Every field is a thing the sidebar's sections read; nothing else is asked for. */
export type SidebarReads = Pick<PanelState, 'settings' | 'vendors' | 'catalogRows' | 'sessions' | 'openSections'
| 'notifications' | 'phrases' | 'localEngines' | 'rankByRuntime' | 'hostKind'> & {
  /** The corpus as the server read it; `undefined` while that read has not landed — the card then says it is loading. */
  readonly bugz: BugCorpus | undefined;
};

/**
 * The state the sidebar's page is made from, as a {@link PanelState}.
 *
 * <p>The fields the sidebar never draws — the Settings page's models, versions, prices, usage and statuses — are neutral values here, and that is
 * safe ONLY because this state is painted on the sidebar alone, never on the Settings page (the provider paints it with `paintEach([sidebar])`).
 * A test holds the other half: the sidebar's page is byte for byte the same whatever those fields say.</p>
 */
export function sidebarState(reads: SidebarReads): PanelState {
  const { bugz, ...rest } = reads;

  return {
    ...rest,
    bugz,
    bugzLoading: bugz === undefined,
    codexModels: [],
    agyModels: [],
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    cardPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
    latestServerVersion: '',
  };
}

/** What the sidebar's own slow cards wait for, once it has them. */
export type SidebarSlow = Pick<PanelState, 'localEngines' | 'rankByRuntime'> & { readonly bugz: BugCorpus };

/** The first state with its slow cards filled: the corpus read, the engines probed, the lines and the ranking rule known. */
export function filledSidebarState(first: PanelState, slow: SidebarSlow): PanelState {
  return { ...first, ...slow, bugzLoading: false };
}

/**
 * What a sidebar paint is made of, in order.
 *
 * @typeParam S what a stage hands to its paint
 */
export interface SidebarStages<S> {
  /** The state from what is known now. It must not wait for anything slow: the paint after it is what the person sees first. */
  readonly first: () => Promise<S>;
  readonly paintFirst: (state: S) => void;
  /** The slow reads of the sidebar's own cards. It may take as long as it takes; nothing is waiting for the sidebar's first paint on it. */
  readonly fill: (first: S) => Promise<S>;
  readonly paintFill: (state: S) => void;
}

/**
 * Paints the sidebar from what is known, THEN reads what is slow and paints again.
 *
 * <p>The order is the whole function: the first paint is written before `fill` is even called, so a slow read — one that never answers — cannot
 * hold the document back. A stage that has nothing to paint (the view went away) says so by its paint doing nothing.</p>
 */
export async function paintSidebarInStages<S>(stages: SidebarStages<S>): Promise<S> {
  const first = await stages.first();
  stages.paintFirst(first);
  const filled = await stages.fill(first);
  stages.paintFill(filled);

  return filled;
}
