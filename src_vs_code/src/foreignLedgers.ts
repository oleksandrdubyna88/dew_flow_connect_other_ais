import { join } from 'node:path';

import type { Tail } from './ledgerTail';
import type { RoundStore } from './roundCounts';
import type { OtherLedger, StoredLine } from './roundCountsCache';
import type { UsageEntry } from './usage';

/**
 * The OTHER stores' ledger tails, read beside this side's (E6.S2 of todo/PLAN_the_round_counter_tail_E6.md).
 *
 * <p><b>What crosses the boundary is an append-only text file, read from its end</b> — never the other side's `coai.db`, which
 * is WAL and not coordinated across `\\wsl.localhost` / drvfs. A line is tagged with the STORE it came from, in memory only:
 * the id the merged answer gave the store (so two spellings of one store are one store, read ONCE). A directory no store of the answer names is not read at all.</p>
 *
 * <p><b>Bounded, and never in this side's way.</b> Every read races the D7 deadline; one that loses is a failure *did not answer
 * in time* in {@link OtherLedger}, and this side's counts and money are published without it. A read still pending at the
 * deadline is KEPT (one in flight per store): the next refresh awaits the same promise instead of starting another against a
 * share that is not answering, and takes its lines if it has settled by then.</p>
 */

/** The file the owning side's coai-mcp writes after every round, and the ledger it appends to. */
export const SNAPSHOT_FILE = 'round-counts-snapshot.json';
export const USAGE_FILE = 'usage.jsonl';

/** What a read that lost the race says. */
export const DID_NOT_ANSWER = 'did not answer in time';

/** One other store to read a ledger tail from: the store (its id, or the directory when no answer named it) and where. */
export interface ForeignTarget {
  readonly store: string;
  readonly directory: string;
}

/** How a foreign tail is read and how long it may take — both seams, so a test needs neither a clock nor a dead share. */
export interface ForeignReads {
  readonly deadlineMs: number;
  readonly read: (path: string, sinceMs: number) => Promise<Tail<UsageEntry>>;
}

/** What reading the other stores' tails came to. */
export interface ForeignLoad {
  readonly lines: readonly StoredLine[];
  readonly ledgers: readonly OtherLedger[];
}

/**
 * The stores to read, from the directories this window was told about and the stores the merged answer named. A directory
 * whose snapshot path the answer puts under the LIVE store is this side's own store through another spelling — its ledger is
 * already read — and a store two directories reach is read through the first of them.
 */
export function foreignTargets(directories: readonly string[], stores: readonly RoundStore[]): readonly ForeignTarget[] {
  const targets = directories.flatMap((directory) => targetOf(directory, stores));

  return targets.filter((target, at) => targets.findIndex((other) => other.store === target.store) === at);
}

/**
 * One directory's store — none for this side's own, and none for a directory NO store of the answer names. That is the old
 * binary (a plain answer names no store), a merged call that timed out, and a store whose snapshot was not counted: with no id
 * there is no telling two spellings of one directory apart, so reading both would double the money — and a store that is *not
 * included* contributes neither its rounds nor its spend.
 */
function targetOf(directory: string, stores: readonly RoundStore[]): readonly ForeignTarget[] {
  const store = stores.find((one) => one.paths.includes(join(directory, SNAPSHOT_FILE)));

  return store === undefined || store.live ? [] : [{ store: store.id, directory }];
}

/** Either the tail, or the instant the deadline won. */
type Raced = { readonly tail: Tail<UsageEntry> } | { readonly late: true };

export class ForeignLedgers {
  /** One read per store at most — kept while it is pending past its deadline, dropped once an answer has been taken. */
  private readonly pending = new Map<string, Promise<Tail<UsageEntry>>>();

  constructor(private readonly reads: ForeignReads) {}

  /** Every target's tail, read in parallel and bounded; the lines tagged with their store. */
  async load(targets: readonly ForeignTarget[], sinceMs: number): Promise<ForeignLoad> {
    const loaded = await Promise.all(targets.map((target) => this.one(target, sinceMs)));

    return { lines: loaded.flatMap((one) => one.lines), ledgers: loaded.map((one) => one.ledger) };
  }

  private async one(target: ForeignTarget, sinceMs: number): Promise<{ lines: readonly StoredLine[]; ledger: OtherLedger }> {
    const raced = await this.race(target, sinceMs);
    const ledger = { store: target.store, directory: target.directory };
    if ('late' in raced) {
      return { lines: [], ledger: { ...ledger, failure: DID_NOT_ANSWER } };
    }
    this.pending.delete(target.store);

    return { lines: raced.tail.records.map((record) => ({ ...record, store: target.store })), ledger: { ...ledger, failure: raced.tail.failure } };
  }

  private async race(target: ForeignTarget, sinceMs: number): Promise<Raced> {
    const read = this.pending.get(target.store) ?? this.start(target, sinceMs);
    this.pending.set(target.store, read);
    let timer: NodeJS.Timeout | undefined = undefined;
    const late = new Promise<Raced>((resolve) => {
      timer = setTimeout(() => resolve({ late: true }), this.reads.deadlineMs);
    });
    try {
      return await Promise.race([read.then((tail): Raced => ({ tail })), late]);
    } finally {
      clearTimeout(timer);
    }
  }

  /** A read that throws is a failure with its reason, like any tail that would not read. */
  private start(target: ForeignTarget, sinceMs: number): Promise<Tail<UsageEntry>> {
    return this.reads.read(join(target.directory, USAGE_FILE), sinceMs)
      .catch((reason: unknown) => ({ records: [], bytesRead: 0, failure: String(reason) }));
  }
}
