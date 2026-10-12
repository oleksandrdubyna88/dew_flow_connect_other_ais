import type { WatchedDir } from './escalationDirs';
import { escapeHtml } from './escapeHtml';
import type { OtherSideDataDirectory } from './otherSideValues';
import { familyLabel } from './pathFamily';
import { NOTHING_ASKED, groupedPaths } from './pathGroups';
import { pathGroupsHtml } from './pathGroupsView';
import { otherSideFieldset, type PathSides } from './pathSides';

/**
 * Setup › MCP server once both sides are known (research/PLAN_paths_per_side.md E4.3): the watched data folders in the
 * groups of `pathGroups.ts`, and the OTHER side's data directory, read-only (decided with the operator, D2).
 *
 * <p><b>The watched folders</b> are grouped by their SPELLING alone (`NOTHING_ASKED`) — the rule `escalationDirs.ts`
 * already reads them by: a folder spelled for the other OS is that side's and skipped here. There is no editor for the list
 * on this page (it lives in `settings.json`, as before E4), so the groups only say whose each folder is.</p>
 *
 * <p><b>The other side's data directory</b> is each other-family side's own choice, read from its overlay through the side
 * registry (`otherSideValues.ts`): text, in a fieldset with its legend and caption — no input, nothing to save — and
 * *Change it from a window on that side.* Never `stat`ed from here.</p>
 */

/** The setting the watched folders are, as `data-list` names their groups. */
const WATCHED_LIST = 'alsoWatchDataDirectories';

/** The watched folders in their groups — each line drawn by the place's own drawer, wrapped so a test can name it. */
export function watchedGroupsHtml(dirs: readonly WatchedDir[], sides: PathSides, line: (dir: WatchedDir) => string): string {
  const byAsked = new Map(dirs.map((dir) => [dir.asked, dir] as const));
  const grouped = groupedPaths(dirs.map((dir) => dir.asked), sides.here === 'windows', NOTHING_ASKED);

  return pathGroupsHtml(WATCHED_LIST, grouped, sides, (asked) => watchedEntry(asked, byAsked.get(asked), line));
}

function watchedEntry(asked: string, dir: WatchedDir | undefined, line: (dir: WatchedDir) => string): string {
  return dir === undefined ? '' : `<div class="watched" data-watched="${escapeHtml(asked)}">${line(dir)}</div>`;
}

/** The other side's data directory, read-only — nothing when one field is drawn. */
export function otherSideDataHtml(sides: PathSides | undefined, rows: readonly OtherSideDataDirectory[]): string {
  if (sides === undefined) {
    return '';
  }
  const listed = rows.length === 0
    ? `<div class="hint">No ${escapeHtml(familyLabel(sides.other))} window has been seen yet, so there is no choice of its to show.</div>`
    : rows.map(dataLine).join('\n');

  return otherSideFieldset(sides, 'data-dir-other-side-caption', `${listed}\n<div class="hint">Change it from a window on that side.</div>`, ' data-other-side-data');
}

/** One side's choice: its name, and the directory as text — or that it chose none there. */
function dataLine(row: OtherSideDataDirectory): string {
  return `<div class="status other-side-data" data-side="${escapeHtml(row.side)}"><b>${escapeHtml(row.label)}</b>: ${chosen(row)}</div>`;
}

function chosen(row: OtherSideDataDirectory): string {
  if (row.directory.length === 0) {
    return 'none chosen there — that side uses its environment, the shared setting or its default folder';
  }

  return `<code>${escapeHtml(row.directory)}</code>${row.dataSide.length === 0 ? '' : ` (side folder <code>${escapeHtml(row.dataSide)}</code>)`}`;
}
