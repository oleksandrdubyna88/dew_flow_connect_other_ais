import { escapeHtml } from './escapeHtml';
import { help, segmentedRadio } from './panelControls';
import { familyLabel } from './pathFamily';
import { KNOWN_FOR_MS, knownFamilies, twoPathFields, type PathFieldsMode, type SeenSide, type SidesView } from './sidesSeen';

/**
 * Setup › This side's list of the sides this profile has been used on, with **Forget** per side, and the switch that
 * decides how many fields a path setting shows (research/PLAN_paths_per_side.md E2.4).
 *
 * <p>Pure: the host reads the registry on every render (`sidesIn`) and hands it in, so a side another window stamped
 * appears at the next repaint — the measured E0 result says the stamp itself reaches an open window live. Forget is a
 * panel BUTTON (the operator's rule: every action is one), posting the side's key; this window's own row has none,
 * since it is in use now and the host refuses it too (`forgetSide`).</p>
 */

const DAY_MS = 24 * 60 * 60 * 1000;

/** The switch's three choices, in the words the page and the help use. */
const MODE_WORDS: Readonly<Record<PathFieldsMode, string>> = { auto: 'Automatic', always: 'Always two', never: 'Always one' };
const MODES: readonly (readonly [PathFieldsMode, string])[] = (['auto', 'always', 'never'] as const).map((mode) => [mode, MODE_WORDS[mode]]);

/** The list, the switch, and the sentence that says what the switch decides today and why. */
export function sidesSeenHtml(view: SidesView): string {
  return `<h3>Sides this profile has been used on</h3>
<p class="hint">Every window of this profile records its side here, once a day. A side not seen for ${KNOWN_FOR_MS / DAY_MS} days is forgotten on its own; <b>Forget</b> drops one now.</p>
<table class="map sides-seen"><thead><tr><th scope="col">Side</th><th scope="col">Paths spelled for</th><th scope="col">Last seen</th><th scope="col">Actions</th></tr></thead>
<tbody>${rowsOf(view)}</tbody></table>
<div class="field">
  <label>${help('pathFieldsPerSide')}Path fields per side</label>
  ${segmentedRadio('pathFieldsPerSide', view.mode, 'How many fields a path setting shows', MODES)}
  <div class="hint path-fields-now">${escapeHtml(whatItDecides(view))}</div>
</div>`;
}

function rowsOf(view: SidesView): string {
  return view.seen.length === 0
    ? '<tr><td colspan="4"><i>No side recorded yet — this window records its own in a moment.</i></td></tr>'
    : view.seen.map((side) => row(side, view.here.key)).join('');
}

function row(side: SeenSide, here: string): string {
  const mine = side.side === here;
  const name = `${escapeHtml(side.label)}${mine ? ' <span class="hint">(this window)</span>' : ''}`;
  const forget = mine ? '' : `<button type="button" class="run" data-command="forgetSide" data-id="${escapeHtml(side.side)}">Forget</button>`;
  const when = `<time datetime="${new Date(side.lastSeen).toISOString()}">${seenDay(side.lastSeen)}</time>`;

  return `<tr data-side="${escapeHtml(side.side)}"><td>${name}</td><td>${familyLabel(side.family)}</td><td>${when}</td><td>${forget}</td></tr>`;
}

/**
 * The day a side was last seen, on the reader's own calendar. The record holds a UTC instant (`utc-timestamps.md`);
 * this is the presentation edge, the one place it becomes a local day.
 */
export function seenDay(at: number): string {
  const day = new Date(at);
  const two = (n: number): string => String(n).padStart(2, '0');

  return `${day.getFullYear()}-${two(day.getMonth() + 1)}-${two(day.getDate())}`;
}

/** What the switch decides today, and which signal decided it — the page is where a person learns why two fields appear. */
function whatItDecides(view: SidesView): string {
  const fields = twoPathFields(view) ? 'Path settings get two fields' : 'Path settings keep one field';

  return `${fields} — ${view.mode === 'auto' ? seenWithin(view) : `${MODE_WORDS[view.mode]} is chosen`}.`;
}

/** Why Automatic decided as it did: which families the registry has seen within the window. */
function seenWithin(view: SidesView): string {
  const families = new Set([view.here.family, ...knownFamilies(view.seen, view.now)]);
  const seen = families.size === 2 ? 'Windows and WSL / Linux have both been seen' : `only ${familyLabel(view.here.family)} has been seen`;

  return `${seen} within ${KNOWN_FOR_MS / DAY_MS} days`;
}
