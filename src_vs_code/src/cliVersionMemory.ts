import type { CliStatus } from './cliVersions';

/**
 * The catalog probe's LAST answer about each vendor row's CLI, kept where a chat turn can read it without asking.
 *
 * <p>E3.S3 of todo/PLAN_the_round_counter_tail_E3.md: a chat line names the version of the CLI that ran it, and the
 * version is one the panel already asked (`PanelViewProvider.vendorCliStatus`, a spawn per vendor every half hour at
 * most). The turn must never be the thing that spawns a process to learn it — an answer waits on nothing — so this is a
 * read of what the probe left behind: a row nobody has probed reads as *not recorded* (empty), not as a version.</p>
 *
 * <p>Held in this module, not on the panel, because a chat turn is a function with no handle on the panel; the panel
 * replaces the whole record each time it refreshes ({@link rememberCliStatus}), so nothing here is ever edited in place.</p>
 */
let remembered: Readonly<Record<string, CliStatus>> = {};

/** Replace what is remembered with the probe's newest answer, by vendor row id. */
export function rememberCliStatus(statuses: Readonly<Record<string, CliStatus>>): void {
  remembered = statuses;
}

/** The version the probe last read for this vendor row's CLI, or empty when it never read one. */
export function cachedCliVersion(vendorId: string): string {
  return remembered[vendorId]?.installed ?? '';
}
