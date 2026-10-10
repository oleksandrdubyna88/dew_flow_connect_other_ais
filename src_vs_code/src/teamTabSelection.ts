import { UsageWant } from './teamUsageCache';

/**
 * What the Team server tab is looking at, held by the extension host (todo/PLAN_team_usage_by_person.md, story 1.3).
 *
 * <p>Host-side because the host decides what to ASK: each company answer makes the server parse its whole ledger, so
 * company figures are asked only for the server the tab is showing, over its window, and only while the tab is the
 * selected one. What the page does with an answer — the search, the sort, an opened card — stays on the page.</p>
 *
 * <p>Pure functions over an immutable record, with no `vscode`.</p>
 */

/** The server's own windows — trailing and UTC, unlike the spending tab's Today, which starts at local midnight. */
export const TEAM_WINDOWS = [
  { id: 'today', label: 'Today' },
  { id: 'week', label: 'Week' },
  { id: 'month', label: 'Month' },
  { id: 'year', label: 'Year' },
] as const;

export type TeamWindow = (typeof TEAM_WINDOWS)[number]['id'];

/** Who spent what this week is the question an admin opens the tab with; Today is one click away. */
export const DEFAULT_TEAM_WINDOW: TeamWindow = 'week';

export interface TeamSelection {
  /** Whether the Team server tab is the selected one on the page. */
  readonly shown: boolean;
  /** The server it shows, or empty for "the first admin server". */
  readonly server: string;
  /** The window chosen per server. */
  readonly windows: Readonly<Record<string, TeamWindow>>;
}

export const NO_SELECTION: TeamSelection = { shown: false, server: '', windows: {} };

function isTeamWindow(value: string): value is TeamWindow {
  return TEAM_WINDOWS.some((one) => one.id === value);
}

export function windowOf(selection: TeamSelection, serverId: string): TeamWindow {
  return selection.windows[serverId] ?? DEFAULT_TEAM_WINDOW;
}

/**
 * A window chip pressed: its id is `<serverId>|<window>`, so the press names the server it was made on — a press
 * that arrives after the person switched servers changes the server it was meant for, not the one now shown.
 * Anything else is ignored rather than guessed at.
 */
export function withWindow(selection: TeamSelection, id: string): TeamSelection {
  const cut = id.lastIndexOf('|');
  const serverId = id.slice(0, cut);
  const window = id.slice(cut + 1);
  if (cut <= 0 || !isTeamWindow(window)) {
    return selection;
  }

  return { ...selection, windows: { ...selection.windows, [serverId]: window } };
}

export function withServer(selection: TeamSelection, serverId: string): TeamSelection {
  return serverId.length === 0 ? selection : { ...selection, server: serverId };
}

export function withShown(selection: TeamSelection, shown: boolean): TeamSelection {
  return { ...selection, shown };
}

/** The server the tab shows: the chosen one while it is still an admin server here, otherwise the first that is. */
export function selectedServer(selection: TeamSelection, adminIds: readonly string[]): string {
  return adminIds.includes(selection.server) ? selection.server : (adminIds[0] ?? '');
}

/**
 * What one server's figures are wanted for: always this account's own over the spending tab's window, and the
 * company's over the tab's window only while the tab is showing that server.
 */
export function usageWants(
  selection: TeamSelection,
  serverId: string,
  meWindow: string,
  adminIds: readonly string[],
): readonly UsageWant[] {
  const mine: UsageWant = { scope: 'me', window: meWindow };
  const showing = selection.shown && selectedServer(selection, adminIds) === serverId;

  return showing ? [mine, { scope: 'company', window: windowOf(selection, serverId) }] : [mine];
}
