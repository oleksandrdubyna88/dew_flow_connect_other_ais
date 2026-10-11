import { join } from 'node:path';

import { claudeAdapter } from './claudeAdapter';
import type { ReportedUsage } from './chatUsage';
import { appendLine } from './jsonlLedger';
import { USAGE_NOT_CAPTURED } from './roundsLog';
import type { UsageEntry } from './usage';

/**
 * What the extension's Claude model probe spent, written down (story 3.3 of todo/PLAN_every_round_is_counted.md —
 * the paid-path census of research/RESULTS_vendor_quota_gauges.md, row 8).
 *
 * <p>The probe asks `claude --model <candidate> -p hi --output-format json` for up to four candidates a week; every
 * one is a billed request, and none was recorded anywhere. <b>Its own ledger</b>, chosen on the consultation of
 * 2026-10-11 between three options: a `kind` inside `chat-usage.jsonl` was refused because every reader of that file,
 * older builds included, reads each line as a chat turn with real money (the reason `chatDoors.ts` keeps its door
 * records out of it); a new `coai-mcp` mode writing `usage.jsonl` was refused as a release dependency that fixes
 * nothing. So: one more file, one writer, the extension's own append helper.</p>
 *
 * <p>The lines are `UsageEntry`-shaped, kind `probe`, so every parser of `usage.jsonl` reads them unchanged; the
 * Spending tab and the Rounds block read this file beside it. A run that did not finish — `capture` answers -1 for a
 * spawn error, a timeout and an unreadable exit alike — is <i>usage not captured</i>, never a free 0/0: the request may
 * have been read and billed.</p>
 */

/** What the probe ledger is called, beside the server's own `usage.jsonl` and the chat's ledgers. */
export const PROBE_USAGE_FILE = 'probe-usage.jsonl';

export function probeUsagePath(dataDir: string): string {
  return join(dataDir, PROBE_USAGE_FILE);
}

/** One request's exit and output, as the probe's `run` port answers it. */
export interface ProbeRun {
  readonly code: number;
  readonly output: string;
}

export type ProbeRunner = (args: readonly string[]) => Promise<ProbeRun>;

/** The ledger line one candidate's request writes, newline included. */
export function probeLine(asked: string, ran: ProbeRun, startedMs: number, endedMs: number): string {
  const entry: UsageEntry = {
    utc: new Date(endedMs).toISOString(),
    provider: 'claude',
    model: asked,
    role: 'ModelProbe',
    stage: 'Probe',
    seconds: Math.round((endedMs - startedMs) / 100) / 10,
    ...spentOf(ran.code === -1 ? undefined : reportedUsage(ran.output)),
    outcome: outcomeOf(ran),
    kind: 'probe',
  };

  return `${JSON.stringify(entry)}\n`;
}

/** The tokens and money a request reported — or *usage not captured*, never a free 0/0, when it reported none. */
function spentOf(usage: ReportedUsage | undefined): Pick<UsageEntry, 'tokensIn' | 'tokensOut' | 'costUsd' | 'usageNote'> {
  return usage === undefined
    ? { tokensIn: 0, tokensOut: 0, costUsd: null, usageNote: USAGE_NOT_CAPTURED }
    : { tokensIn: usage.tokensIn, tokensOut: usage.tokensOut, costUsd: usage.costUsd };
}

/**
 * The probe's `run`, recording every request it makes — failures included — in `path`.
 *
 * <p>Wrapped at the LAUNCH boundary, before the probe's own logic merges or throws anything away (the consultation's
 * advice): every billed request is one line. The append is not awaited — nothing a person waits on may wait on a disk
 * (`jsonlLedger.ts`) — and the answer is handed back exactly as `run` gave it.</p>
 */
export function recordingRun(run: ProbeRunner, path: string, now: () => number): ProbeRunner {
  return async (args) => {
    const started = now();
    const ran = await run(args);
    void appendLine(path, probeLine(askedIn(args), ran, started, now()), 'a model probe');

    return ran;
  };
}

/** The candidate a probe request asked about — the value after `--model`. */
function askedIn(args: readonly string[]): string {
  const at = args.indexOf('--model');

  return at >= 0 ? args[at + 1] ?? '' : '';
}

/** What Claude reported for the request, through the chat's own reader of the same `result` object. */
function reportedUsage(output: string): ReportedUsage | undefined {
  const event = claudeAdapter.classify(output.trim());

  return 'usage' in event ? event.usage : undefined;
}

function outcomeOf(ran: ProbeRun): string {
  if (ran.code === -1) {
    return 'did not finish';
  }

  return ran.code === 0 ? 'ok' : `exited ${ran.code}`;
}
