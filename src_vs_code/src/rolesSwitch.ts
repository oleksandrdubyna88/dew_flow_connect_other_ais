import { bucketOf, composed, isActive, PLAN_STAGE, stageOf, type RoleBucket, type RoleRow } from './roles';

/**
 * ONE switch per role on the new Settings page (todo/PLAN_one_model_catalog.md E4.3). A role has had two: the catalog's
 * `active` (a `COAI_ROLES` row, written by the Review roles tab) and the panel's `roleEnabled` (`COAI_ENABLED_*`, read by
 * a server from 0.18.13 — `ROLE_SWITCH_SINCE`). A box showing one of them disagreed with a round decided by both. Here
 * they are read as one — on only when both are — and written as one: `active` first, through every refusal the roles
 * have, and `roleEnabled` following only once that write has LANDED. A server older than 0.18.13 reads the catalog row;
 * a newer one reads both; both say the same. Pure.
 */

/** Whether the role carries the panel's switch too — every role but the plan stage's. */
function switched(role: RoleRow): boolean {
  return stageOf(role) !== PLAN_STAGE;
}

/** The one switch, as drawn: on only when every switch the role has is on. */
export function switchedOn(role: RoleRow, roleEnabled: Readonly<Record<string, boolean>>): boolean {
  return isActive(role) && (!switched(role) || roleEnabled[role.id] !== false);
}

/**
 * The roles ON in one bucket by {@link switchedOn} — the roles a round of that bucket launches, since a server of either
 * age skips a role off by either switch.
 *
 * <p><b>The one count, read by every place that guards a bucket</b> (todo/PLAN_one_model_catalog.md E5.1b): the new
 * page's role block (`rolesBlocks.canDeactivate`), the host's twin of it (`rolesEdit.ts`, `lastStanding`) and the current
 * page's code-role count (`settingsShape.enabledCodeRoles`). The new page counted the catalog's `active` alone — two
 * active code roles, one of them switched off by `roleEnabled`, let the other be switched off too, and the code stage
 * ran no role at all — while the current page counted both switches. Two counts of one thing is how they came to
 * disagree, so there is one.</p>
 *
 * <p>An empty `roleEnabled` reads the catalog alone, which is what a page that knows only the catalog's switch (the
 * Review roles tab) asks with.</p>
 */
export function rolesOn(rows: readonly RoleRow[], bucket: RoleBucket, roleEnabled: Readonly<Record<string, boolean>>): readonly RoleRow[] {
  return composed(rows).filter((role) => bucketOf(role) === bucket && switchedOn(role, roleEnabled));
}

/**
 * Whether this role is the last one ON in its bucket — the one that switching off, removing or moving would leave the
 * bucket with no role, in EVERY bucket: a stage with no role is a round with no reviewer, which the session counts as
 * unresolved, whichever stage it is.
 */
export function lastOn(rows: readonly RoleRow[], role: RoleRow, roleEnabled: Readonly<Record<string, boolean>>): boolean {
  return switchedOn(role, roleEnabled) && rolesOn(rows, bucketOf(role), roleEnabled).length <= 1;
}

/**
 * The panel's switch after the catalog's moved — or `undefined` when nothing is to be written: the catalog write was
 * refused or has not landed, the role has no second switch, or it already says the same.
 *
 * @param rows the roles as stored AFTER the catalog write
 */
export function roleSwitchFollows(
  rows: readonly RoleRow[],
  id: string,
  on: boolean,
  roleEnabled: Readonly<Record<string, boolean>>,
): Readonly<Record<string, boolean>> | undefined {
  return landed(rows, id, on) && (roleEnabled[id] !== false) !== on ? { ...roleEnabled, [id]: on } : undefined;
}

/** Whether the catalog's switch of a role that has the panel's too now says `on`. */
function landed(rows: readonly RoleRow[], id: string, on: boolean): boolean {
  const role = composed(rows).find((one) => one.id === id);

  return role !== undefined && switched(role) && isActive(role) === on;
}
