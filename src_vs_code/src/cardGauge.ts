import { gaugeLines, reasonWords } from './gaugeWords';
import type { RoundsSnapshot } from './roundCountsCache';
import { rowBelongsTo, serverVendorOf } from './teamServers';
import type { GaugeReading } from './teamServerApi';
import type { TeamServerState } from './teamServerView';
import type { Vendor } from './vendors';

/**
 * The vendor's own gauge on a model card (story 6.2 of todo/PLAN_every_round_is_counted.md) — pure, from what the panel
 * already holds: the Team servers' cached catalogs and the cached local round-count answer. Never a read per render.
 *
 * <p><b>Only an explicit match shows a server's observation.</b> Local capture (story 6.1b) is deferred, so this machine
 * reads no gauge. A row that RUNS on a Team server spends that server's accounts, so its card shows the server's newest
 * reading of the vendor, naming the server it came from; a local codex row is never guessed to be the same account as a
 * server's and says instead — from the local answer's own reason — that this machine does not read the gauge. A row whose
 * vendor reports no gauge says nothing, and an absent reading is absent, never 0 %.</p>
 */

/** The runtimes whose account reports a gauge coai reads (story 6.1a: codex only). */
const GAUGED_RUNTIMES: readonly string[] = ['codex'];

/** The card's gauge lines for one row — none when there is nothing honest to say. */
export function cardGaugeLines(row: Vendor, servers: readonly TeamServerState[], snapshot: RoundsSnapshot | undefined, now: number, timeZone?: string): readonly string[] {
  return row.runtime === 'remote' ? remoteLines(row, servers, now, timeZone) : localLines(row, snapshot);
}

/** The server's reading of the vendor this row runs as, each line naming the server. */
function remoteLines(row: Vendor, servers: readonly TeamServerState[], now: number, timeZone: string | undefined): readonly string[] {
  const state = servers.find((one) => rowBelongsTo(row, one.server));
  const reading = state === undefined ? undefined : readingOf(state, serverVendorOf(row, state.server.id));

  return gaugeLines(reading, now, timeZone).map((line) => `${line} · read on ${state!.server.name}`);
}

function readingOf(state: TeamServerState, vendor: string): GaugeReading | undefined {
  return state.catalog?.vendors.find((one) => one.id === vendor)?.slots?.gauge;
}

/** A local row of a gauged vendor: why this machine shows no gauge, as the local answer said it. */
function localLines(row: Vendor, snapshot: RoundsSnapshot | undefined): readonly string[] {
  const reason = localReason(snapshot);

  return reason !== undefined && GAUGED_RUNTIMES.includes(row.runtime) ? [`${row.runtime} gauge: unavailable — ${reasonWords(reason)}`] : [];
}

/** The local answer's reason, once it has been read and counted — undefined before a read and from an older binary. */
function localReason(snapshot: RoundsSnapshot | undefined): string | undefined {
  const read = snapshot?.read;

  return read?.state === 'counted' ? read.answer.gaugeUnavailable : undefined;
}
