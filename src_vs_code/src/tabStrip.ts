import { escapeHtml } from './escapeHtml';

/**
 * The ONE tab strip.
 *
 * <p><b>Why it exists.</b> Three pages render their own — and they have drifted in exactly the way
 * the reuse rule says a second copy will:</p>
 *
 * <table>
 *   <tr><th></th><th>role="tablist"</th><th>aria-selected</th><th>aria-controls</th></tr>
 *   <tr><td>`rolesPage`</td><td>yes</td><td>yes</td><td>yes</td></tr>
 *   <tr><td>`notificationsRows`</td><td>yes</td><td>—</td><td>—</td></tr>
 *   <tr><td>`roundsLog`</td><td><b>no</b></td><td>—</td><td>—</td></tr>
 * </table>
 *
 * <p>The review page needs two strips (project, then language), so writing them by hand would have
 * made a fourth copy of the shape that is already wrong in two places. A plan reviewer said that
 * extracting this while leaving `rolesPage`'s private copy in place would merely ADD a copy, which
 * is correct — so `rolesPage` is converted in the same change and is this module's first caller.
 * `notificationsRows` and `roundsLog` are deliberately not converted here: `roundsLog`'s strip is
 * missing the accessibility attributes above, and repairing a page this story does not otherwise
 * touch is a change to ask the operator about rather than a side effect of a refactor.</p>
 *
 * <p><b>The wrapper is part of the unit, not the caller's job.</b> `rolesPage` had the
 * `role="tablist"` div at the call site and `roundsLog` simply forgot it. A strip that renders only
 * the buttons lets the next caller forget it too, which is the defect being removed rather than
 * moved.</p>
 *
 * <p><b>Everything that reaches an attribute is escaped.</b> The three copies never needed to: their
 * labels are hardcoded English. This one's are repository directory names and language names out of
 * SQLite, so the escaping is the capability being added — and it is why this is a widening of
 * `rolesPage`'s copy rather than a move of it.</p>
 */

/**
 * How a strip looks — the underline tabs this extension's full pages already use.
 *
 * <p><b>The margin is the parameter because it is the only thing the copies disagreed about.</b>
 * `rolesPage` and `roundsLog` carry the same six declarations to the character, differing in
 * `.tabs { margin }` alone (`10px 0` against `4px 0 10px`) — so unifying them with one fixed value
 * would have moved a strip on a page this story does not touch. Widening beats moving here: the
 * shape is shared, the spacing stays each page's own.</p>
 *
 * <p><b>The other two strips are NOT this design and are left alone.</b> `panelView`'s is a
 * segmented control (`flex: 1`, button backgrounds) and `notificationsPageStyle`'s wraps; they are
 * different designs rather than drifted copies, and converting them would be a visual change to two
 * more pages under cover of a refactor.</p>
 */
export function tabCss(margin: string): string {
  return [
    `.tabs { display: flex; gap: 6px; margin: ${margin}; border-bottom: 1px solid var(--vscode-panel-border); }`,
    '.tabs .tab { background: transparent; color: var(--vscode-foreground); border: none;',
    '  border-bottom: 2px solid transparent; border-radius: 0; padding: 6px 10px; opacity: .75; }',
    '.tabs .tab.on { opacity: 1; border-bottom-color: var(--vscode-focusBorder); }',
  ].join('\n');
}

/** One tab. */
export interface Tab {
  /** The value posted back when it is pressed — a language, or a project path. */
  readonly key: string;

  /** What a person reads on it. */
  readonly label: string;

  /**
   * The id fragment for the aria wiring, when {@link key} cannot be one.
   *
   * <p>A project key is a path, and `id="d:/rsd/x"` is markup no CSS selector can reach. The caller
   * supplies something short and stable (its index, say) and the real key stays in `data-tab`,
   * which is what the page posts. Defaults to {@link key}, which is what keeps the converted pages
   * byte-identical.</p>
   */
  readonly slug?: string;

  /** A tooltip — the whole path, when the label is only its last segment. */
  readonly title?: string;
}

/** The ids and the label this strip wires itself with. */
export interface StripNames {
  /** Prefix for each button's own id, e.g. `tab-`. */
  readonly tab: string;

  /** Prefix for the id of the panel each button controls, e.g. `section-` — or, with {@link onePanel}, that id itself. */
  readonly panel: string;

  /**
   * Every tab controls ONE region, whose id is {@link panel} verbatim.
   *
   * <p>`rolesPage` has a section per tab and needs the slug appended. A filter strip does not: the
   * review page's project and language tabs both narrow the same table, and pointing
   * `aria-controls` at a `pairs-0` that does not exist would be worse than not wiring it at all —
   * a screen reader follows it and lands nowhere.</p>
   */
  readonly onePanel?: boolean;

  /** What the tablist is called, for a screen reader. */
  readonly label: string;

  /**
   * A name for the strip, as `data-strip` on the wrapper.
   *
   * <p>A page with two strips gets two `data-tab` presses that mean different things, and the
   * handler has to tell them apart — reading the ancestor is how, and it is one attribute rather
   * than two selectors and two branches. Absent on a page with one strip, which is what keeps the
   * converted pages byte-identical.</p>
   */
  readonly strip?: string;
}

function attribute(name: string, value: string | undefined): string {
  return value === undefined || value === '' ? '' : ` ${name}="${escapeHtml(value)}"`;
}

/**
 * One button.
 *
 * <p>The attribute ORDER is load-bearing: `rolesPage`'s eighteen tab tests match
 * `class="tab on" data-tab="documents"` as one string, so a reordering here would fail them for a
 * reason that has nothing to do with the tabs. They are the proof this extraction changed nothing.</p>
 */
function button(tab: Tab, open: string, names: StripNames): string {
  const slug = escapeHtml(tab.slug ?? tab.key);
  const on = tab.key === open;
  const controls = names.onePanel === true ? names.panel : `${names.panel}${slug}`;

  return `<button type="button" role="tab" id="${names.tab}${slug}" aria-controls="${controls}"`
    + ` aria-selected="${on ? 'true' : 'false'}" class="tab${on ? ' on' : ''}"`
    + ` data-tab="${escapeHtml(tab.key)}"${attribute('title', tab.title)}>${escapeHtml(tab.label)}</button>`;
}

/**
 * The strip, or nothing at all when there are no tabs.
 *
 * <p>An empty tablist announces a control a person cannot use, so there is no such thing here. A
 * page with nothing to show says why in its own words instead.</p>
 */
export function tabStrip(tabs: readonly Tab[], open: string, names: StripNames): string {
  if (tabs.length === 0) return '';

  const buttons = tabs.map((tab) => button(tab, open, names)).join('');

  return `<div class="tabs" role="tablist" aria-label="${escapeHtml(names.label)}"`
    + `${attribute('data-strip', names.strip)}>${buttons}</div>`;
}

/**
 * Which tab is open, given what was last held.
 *
 * <p><b>This is the fallback two plan reviewers asked for independently.</b> A language chosen in
 * one project does not have to exist in the next one: C# is selected, the person switches to a
 * project that is only TypeScript, and a filter that still says C# matches nothing — a blank table
 * with no visible way back. So a held choice is kept only while it is still available, and
 * otherwise the first tab opens.</p>
 *
 * <p>It answers an empty string for no tabs, because inventing a key for a strip that does not
 * exist would put the page back in the state this prevents.</p>
 */
export function openedFrom(tabs: readonly Tab[], held: string): string {
  const kept = tabs.some((tab) => tab.key === held);

  return kept ? held : (tabs[0]?.key ?? '');
}
