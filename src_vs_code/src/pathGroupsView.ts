import { escapeHtml } from './escapeHtml';
import type { GroupedPaths, PathGroup } from './pathGroups';
import { otherSideFieldset, thisSideCaption, type PathSides } from './pathSides';

/**
 * A list of folders a Windows window and a WSL window share, drawn in its three groups once both sides are known
 * (research/PLAN_paths_per_side.md E4.2, E4.3; the groups are `pathGroups.ts`'s): THIS side's under *This side — Windows*,
 * the OTHER side's in a `fieldset` with its legend and caption, and *Both sides / unclear* — every entry in exactly one,
 * nothing hidden. The question consultant's folders and the watched data folders draw through this one builder, each
 * with its own line per entry and what it adds to a group (Add a folder…, the typed box).
 */

/** What a place adds inside a group, after its entries — already escaped. */
export interface GroupExtras {
  readonly thisSide?: string;
  readonly otherSide?: string;
}

/** The id of the other side's caption for a list, unescaped — the controls in that group are described by it. */
export function groupCaptionId(list: string): string {
  return `${list}-other-side-caption`;
}

/**
 * The three groups.
 *
 * @param list the setting the entries belong to (`qconsultRoots`, `alsoWatchDataDirectories`) — `data-list` on each group
 * @param draw one entry's line, escaped
 */
export function pathGroupsHtml(list: string, grouped: GroupedPaths, sides: PathSides, draw: (entry: string) => string, extras: GroupExtras = {}): string {
  const lines = (group: PathGroup): string => linesOf(grouped[group], draw);

  return `<div class="path-group"${groupAttributes(list, 'thisSide')}><h4 class="group-title">${escapeHtml(thisSideCaption(sides))}</h4>${lines('thisSide')}${extras.thisSide ?? ''}</div>\n`
    + `${otherSideFieldset(sides, groupCaptionId(list), `${lines('otherSide')}${extras.otherSide ?? ''}`, groupAttributes(list, 'otherSide'))}\n`
    + `<div class="path-group"${groupAttributes(list, 'unclear')}><h4 class="group-title">Both sides / unclear</h4>`
    + `<div class="hint">${escapeHtml(UNCLEAR)}</div>${lines('unclear')}</div>`;
}

/** What the third group holds, said once above it. */
const UNCLEAR = 'Spelled for neither system — a //server/share or a relative name — or this window could not tell. Each side\'s server decides for itself.';

function groupAttributes(list: string, group: PathGroup): string {
  return ` data-list="${escapeHtml(list)}" data-group="${group}"`;
}

function linesOf(entries: readonly string[], draw: (entry: string) => string): string {
  return entries.length === 0 ? '<div class="hint">None.</div>' : entries.map(draw).join('\n');
}
