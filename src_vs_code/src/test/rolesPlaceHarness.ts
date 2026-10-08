import { catalogHtml } from '../catalogPage';
import { type BusySnapshot } from '../busySnapshot';
import { type PanelState } from '../panelView';
import { type Tombstone } from '../roleDeletion';
import { type RoleRow } from '../roles';
import { CUSTOM_ROLES_SINCE } from '../rolesBlocks';
import { panelState, type PageClock } from './panelPageHarness';
import { paneIn } from './panelPages';
import { type Page, runPageHtml } from './pageScriptHarness';
import { pageTree, selectorsOf, type PageNode } from './pageTree';

/**
 * Reviews › Roles & prompts on the Settings page, drawn and RUN — what the Review roles tab's tests read once that tab
 * was deleted (todo/PLAN_one_model_catalog.md E5.1 step 4). The tab drew the same blocks (`rolesBlocks.roleBlock`), so
 * what it held about a role is asked here of the page a person sees.
 *
 * <p>Every control a test fires at is taken from the page as drawn (`pageTree.ts`), never built by the test.</p>
 */

/** What the roles' tests vary: the rows, the prompt bodies, the server, the side, the stranded deletions, the switches. */
export interface RolesPlaceState {
  readonly rows?: readonly RoleRow[];
  readonly texts?: Readonly<Record<string, string>>;
  readonly serverVersion?: string;
  readonly perSide?: boolean;
  readonly stranded?: readonly Tombstone[];
  readonly roleEnabled?: Readonly<Record<string, boolean>>;
  /** What is still running when the page is painted. */
  readonly busy?: BusySnapshot;
}

/** A panel state holding those roles — a server new enough to read them, unless a test says otherwise. */
export function rolesPanelState(over: RolesPlaceState = {}): PanelState {
  const base = panelState('reviewers');
  const rows = over.rows ?? [];

  return {
    ...base,
    settings: { ...base.settings, roles: rows, roleEnabled: { ...base.settings.roleEnabled, ...over.roleEnabled } },
    roles: {
      rows, texts: over.texts ?? {}, serverVersion: over.serverVersion ?? CUSTOM_ROLES_SINCE, perSide: over.perSide ?? false,
      stranded: over.stranded ?? [],
    },
    ...(over.busy === undefined ? {} : { busy: over.busy }),
  };
}

/** What the page's scripts select at load, so they bind to what the page drew. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The page opened on Roles & prompts: the running page, and the place's pane as drawn. */
export interface RolesPlace {
  readonly page: Page;
  readonly pane: PageNode;
  readonly html: string;
}

/** The Settings page on Roles & prompts, drawn and running — on a clock when a test moves time. */
export function runRolesPlace(over: RolesPlaceState = {}, clock?: PageClock): RolesPlace {
  const html = catalogHtml(rolesPanelState(over), 'test-nonce', 'reviews/roles');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), clock, { value: undefined });

  return { page, pane: tree.one((node) => node.dataset.pane === 'reviews/roles', 'Roles & prompts pane'), html };
}

/** The Roles & prompts pane's markup, for a test that reads what is drawn rather than presses it. */
export function rolesPlaceHtml(over: RolesPlaceState = {}): string {
  return paneIn(catalogHtml(rolesPanelState(over), 'test-nonce', 'reviews/roles'), 'reviews/roles');
}

/** One role's block in the pane. */
export function roleBlockIn(pane: PageNode, id: string): PageNode {
  return pane.one((node) => node.tagName === 'DETAILS' && node.dataset.id === id, `role ${id}`);
}

/** The roles' edits the page posted, in order — what the one editing core is handed (`rolesMessages.roleEdit`). */
export function roleEditsOf(page: Page): readonly Record<string, unknown>[] {
  return page.posted.filter((one) => one['type'] === 'roles').map((one) => one['edit'] as Record<string, unknown>);
}
