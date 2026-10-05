/**
 * Where the new Settings page can open (todo/PLAN_one_model_catalog.md, E3.1; D11): six tabs, their sub-tabs, and a place
 * for every id the old page answered to.
 *
 * <p>A PLACE is `tab` or `tab/sub` — the one string the host holds and the page's script opens, so a deep link names a
 * sub-tab too (the mockup's hash named the tab alone). Pure: the host holds it, the page draws from it, tests read it.</p>
 */

/** One sub-tab: its id within its tab, and what the strip says. */
export interface CatalogSub {
  readonly id: string;
  readonly label: string;
}

/** One tab of the new page, and its sub-tabs — none for a tab that is one page. */
export interface CatalogTab {
  readonly id: string;
  readonly label: string;
  readonly subs: readonly CatalogSub[];
}

/** The six tabs, in the order the design gives them (`new_design/app.js` TABS, without the mockup's Design notes). */
export const CATALOG_TABS: readonly CatalogTab[] = [
  { id: 'models', label: 'Models', subs: [] },
  {
    id: 'reviews',
    label: 'Reviews',
    subs: [
      { id: 'stages', label: 'Stages' },
      { id: 'roles', label: 'Roles & prompts' },
      { id: 'prompts', label: 'Prompts per round' },
      { id: 'gate', label: 'The gate' },
      { id: 'commands', label: 'Commands' },
      { id: 'limits', label: 'Limits' },
    ],
  },
  { id: 'consultants', label: 'Consultants', subs: [{ id: 'consultant', label: 'Consultant' }, { id: 'qconsult', label: 'Question consultant' }] },
  { id: 'security', label: 'Security lane', subs: [] },
  { id: 'chat', label: 'Chat', subs: [] },
  {
    id: 'setup',
    label: 'Setup',
    subs: [
      { id: 'keys', label: 'Vendor keys' },
      { id: 'team', label: 'Team servers' },
      { id: 'mcp', label: 'MCP server' },
      { id: 'side', label: 'This side' },
    ],
  },
];

/**
 * Every id the old Settings page answered to, and the place that holds it now (D11). A test reads the old page's own
 * sections, so an old tab added without a place here is a red test, not a deep link that opens the first tab.
 */
export const OLD_TAB_PLACES: Readonly<Record<string, string>> = {
  reviewers: 'models',
  chat: 'chat',
  consultant: 'consultants/consultant',
  questionconsultant: 'consultants/qconsult',
  securityLane: 'security',
  prompts: 'reviews/prompts',
  gate: 'reviews/gate',
  limits: 'reviews/limits',
  keys: 'setup/keys',
  teamServers: 'setup/team',
  side: 'setup/side',
  server: 'setup/mcp',
};

/** A place as the page opens it: a tab with sub-tabs always names one (its first, when only the tab was named). */
function canonical(requested: string): string {
  const [tabId, subId] = requested.split('/');
  const tab = CATALOG_TABS.find((one) => one.id === tabId);

  return tab === undefined ? '' : placeIn(tab, subId);
}

/** A place within one tab: the tab alone when it has no sub-tabs, else one of its sub-tabs — '' for anything else. */
function placeIn(tab: CatalogTab, subId: string | undefined): string {
  if (tab.subs.length === 0) {
    return subId === undefined ? tab.id : '';
  }

  return subPlace(tab, subId ?? tab.subs[0]!.id);
}

function subPlace(tab: CatalogTab, subId: string): string {
  return tab.subs.some((sub) => sub.id === subId) ? `${tab.id}/${subId}` : '';
}

/**
 * The place to hold after a request — the page pressed a tab, or `coai.openSettings` was run with an argument: an old
 * id is taken to its new place, a place or a bare tab is taken as it is, and ANYTHING else leaves the held place as it
 * was (the command is reachable from the palette and `executeCommand`, so its argument can be anything at all).
 */
export function placeOf(requested: unknown, held: string): string {
  return askedPlace(requested) || canonical(held) || 'models';
}

/** What a request names — an old id taken to its new place — or '' for anything that names no place. */
function askedPlace(requested: unknown): string {
  return typeof requested === 'string' ? canonical(OLD_TAB_PLACES[requested] ?? requested) : '';
}

/** The old page's tab for a place, so switching the preview off keeps the person where they were — '' when it had none. */
export function oldIdOf(place: string): string {
  return Object.entries(OLD_TAB_PLACES).find(([, at]) => at === canonical(place))?.[0] ?? '';
}

/**
 * What the one Settings tab holds for EACH page (epic 3's code round): the new page's place and the current page's tab,
 * so switching the preview off opens the tab the person last had there — not the first tab, for a place the current
 * page never had.
 */
export interface HeldTabs {
  readonly place: string;
  readonly oldTab: string;
}

/** The held tabs after a request on the page that is showing — the other page's position moves only where both have it. */
export function heldAfter(held: HeldTabs, requested: unknown, preview: boolean, oldIds: readonly string[]): HeldTabs {
  return preview ? heldOnNew(held, requested) : heldOnOld(held, requested, oldIds);
}

function heldOnNew(held: HeldTabs, requested: unknown): HeldTabs {
  const place = placeOf(requested, held.place || placeOf(held.oldTab, ''));

  return { place, oldTab: oldIdOf(place) || held.oldTab };
}

function heldOnOld(held: HeldTabs, requested: unknown, oldIds: readonly string[]): HeldTabs {
  const oldTab = typeof requested === 'string' && oldIds.includes(requested) ? requested : held.oldTab;

  return { place: OLD_TAB_PLACES[oldTab] ?? held.place, oldTab };
}
