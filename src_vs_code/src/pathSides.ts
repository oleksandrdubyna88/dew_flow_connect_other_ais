import { escapeHtml } from './escapeHtml';
import { familyLabel, type PathFamily } from './pathFamily';
import { twoPathFields, type SidesView } from './sidesSeen';
import { seenDay } from './sidesSeenView';

/**
 * The two sides a path setting is drawn for, when it is drawn twice (todo/PLAN_paths_per_side.md design (d), E4): THIS
 * side's field first, named by its family and — for a remote — by the side itself (*This side — WSL / Linux (WSL:
 * Ubuntu)*), then the OTHER side's in a bordered `fieldset` whose `legend` names its family and the day it was last seen
 * here (*The other side — Windows (last seen 2026-10-09)*), or *not seen yet* when the switch says *Always two* before
 * that side was ever opened (E4.4).
 *
 * <p>One decision for every place that draws a path — the Models card's CLI path, the question consultant's folders, the
 * watched data folders and the data directory — so the places cannot disagree about whether there are two fields. The
 * decision itself is E2's {@link twoPathFields}; this only names the sides. Pure: the host reads the registry on every
 * render and hands the view in.</p>
 */

/** The two sides, when a path setting shows two fields. */
export interface PathSides {
  /** The family this window spells paths in — its field is drawn first, and is the one Browse and Add a folder fill. */
  readonly here: PathFamily;
  /** This window's side as Setup › This side names it (`thisSideLabel`): `Windows`, `WSL: Ubuntu`. */
  readonly hereLabel: string;
  readonly other: PathFamily;
  /** When a side of the other family was last seen here — undefined when none was (*Always two* chosen before it). */
  readonly otherLastSeen: number | undefined;
}

/** The muted caption under the other side's legend — the same words at every place. */
export const OTHER_SIDE_CAPTION = 'Used by the server on that side. This window does not check it.';

/**
 * What stands where the other side's box would be while this side keeps its own settings (`coai.perSideSettings`, for
 * that key — `settingsShape.keptPerSide`): a value typed here would be saved into THIS side's own settings, which the
 * other side never reads (todo/PLAN_paths_per_side.md, E4's own review, item 1). The data directory's group says the
 * same thing in its own words, since it is read-only at every setting of the switch.
 */
export const KEEPS_ITS_OWN = 'This side keeps its own settings — set the other side\'s value from a window on that side.';

/** {@link KEEPS_ITS_OWN} as a line of the other side's group — read-only, no control. */
export function keepsItsOwnLine(): string {
  return `<div class="hint keeps-its-own">${escapeHtml(KEEPS_ITS_OWN)}</div>`;
}

/**
 * Why a write to the other side's box is refused while this side keeps its own settings — the page draws no such box,
 * so only a page drawn before the switch moved can post one. Says what was typed: the box it was typed into is gone on
 * the repaint, and the refusal beside the sentence is the only place the text is still seen.
 */
export function keepsItsOwnRefusal(typed: string): string {
  const what = typed.trim().length === 0 ? 'Clearing the box' : `'${typed.trim()}'`;

  return `${what} was not saved — this side keeps its own settings; set the other side's value from a window on that side.`;
}

/**
 * The two sides, or `undefined` — ONE field, as every place looked before E4 — when the switch or the registry says one
 * (`twoPathFields`), or when the page is drawn without the registry (the sidebar, a test that names none).
 *
 * @param family the family the page is drawn for (`PanelState.hostFamily`, else this host's)
 */
export function pathSidesOf(view: SidesView | undefined, family: PathFamily): PathSides | undefined {
  if (view === undefined || !twoPathFields(view)) {
    return undefined;
  }
  const other: PathFamily = family === 'windows' ? 'posix' : 'windows';

  return { here: family, hereLabel: view.here.label, other, otherLastSeen: lastSeenOf(view, other) };
}

/** When a side of this family was last seen within the registry's window — undefined when none was. */
function lastSeenOf(view: SidesView, family: PathFamily): number | undefined {
  const seen = view.seen.filter((side) => side.family === family).map((side) => side.lastSeen);

  return seen.length === 0 ? undefined : Math.max(...seen);
}

/** *This side — Windows*, or *This side — WSL / Linux (WSL: Ubuntu)* when the side has a name of its own. */
export function thisSideCaption(sides: PathSides): string {
  const family = familyLabel(sides.here);
  const named = sides.hereLabel.length > 0 && sides.hereLabel !== family ? ` (${sides.hereLabel})` : '';

  return `This side — ${family}${named}`;
}

/** *The other side — WSL / Linux (last seen 2026-10-09)*, or *(not seen yet)* (E4.4). */
export function otherSideLegend(sides: PathSides): string {
  const when = sides.otherLastSeen === undefined ? 'not seen yet' : `last seen ${seenDay(sides.otherLastSeen)}`;

  return `The other side — ${familyLabel(sides.other)} (${when})`;
}

/**
 * The other side's group: a `fieldset` with its `legend` and the caption, which every control inside points at
 * (`aria-describedby` the caption's id) — the shape design (d) and the accessibility line of E4 ask for.
 *
 * @param captionId the caption's id, unescaped — the controls inside name it in their `aria-describedby`
 * @param body the group's content, already escaped
 * @param attributes more attributes of the fieldset, already escaped (a list's `data-list` / `data-group`), or ''
 */
export function otherSideFieldset(sides: PathSides, captionId: string, body: string, attributes = ''): string {
  return `<fieldset class="other-side"${attributes}><legend>${escapeHtml(otherSideLegend(sides))}</legend>`
    + `<p class="hint other-side-caption" id="${escapeHtml(captionId)}">${escapeHtml(OTHER_SIDE_CAPTION)}</p>${body}</fieldset>`;
}
