import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

import type { ForeignReads } from '../foreignLedgers';
import { readTailSince, type Tail } from '../ledgerTail';
import { readRoundCounts, roundsLoader } from '../roundCountsRead';
import { parseUsageLine, type UsageEntry } from '../usage';
import { ANSWER_JSON, MERGED_JSON, THIS_STORE, WSL_STORE } from './roundCountsFixtures';

/**
 * Asking `coai-mcp --round-counts-merged` first (E6.S1, section 4.6 of todo/PLAN_the_round_counter_tail_E6.md): with each other
 * store's snapshot path; an older binary (exit 64) or a merged call that did not answer in time (-1, D7) falls back to
 * `--round-counts`, today's single-store sentence, and a timeout names every snapshot store as not answering.
 *
 * And (E6.S2) the other stores' `usage.jsonl` tails read beside this side's, tagged by store id, a store read once, every
 * foreign read bounded by the D7 deadline, a foreign failure in `otherLedgers` and never in the global `ledgerFailure`.
 */

const SNAPSHOTS = ['D:\\coai-win\\round-counts-snapshot.json', '/mnt/c/coai/round-counts-snapshot.json'] as const;

type Spawned = { code: number; output: string };

/** A fake spawn that answers per mode and records what it was asked. */
function fake(answers: Readonly<Record<string, Spawned>>): { asked: string[][]; run: (args: readonly string[]) => Promise<Spawned> } {
  const asked: string[][] = [];

  return {
    asked,
    run: async (args) => {
      asked.push([...args]);

      return answers[args[0] ?? ''] ?? { code: 64, output: '' };
    },
  };
}

test('TheMergedMode_IsAskedFirst_WithEachOtherStoresSnapshotPath', async () => {
  const spawn = fake({ '--round-counts-merged': { code: 0, output: MERGED_JSON } });

  const read = await readRoundCounts('coai-mcp', SNAPSHOTS, spawn.run);

  assert.deepEqual(spawn.asked, [['--round-counts-merged', ...SNAPSHOTS]]);
  assert.equal(read.state, 'counted');
  assert.equal(read.state === 'counted' ? read.answer.stores?.length : -1, 2, 'the merged answer\'s stores were lost on the way');
});

test('no other store is still a merged request — the keys of this side alone are what launches per round needs (D5)', async () => {
  const spawn = fake({ '--round-counts-merged': { code: 0, output: MERGED_JSON } });

  await readRoundCounts('coai-mcp', [], spawn.run);

  assert.deepEqual(spawn.asked, [['--round-counts-merged']]);
});

test('AnOldBinary_FallsBackToRoundCounts_AndTheSentenceIsTodays', async () => {
  const spawn = fake({ '--round-counts-merged': { code: 64, output: '' }, '--round-counts': { code: 0, output: ANSWER_JSON } });

  const read = await readRoundCounts('coai-mcp', SNAPSHOTS, spawn.run);

  assert.deepEqual(spawn.asked, [['--round-counts-merged', ...SNAPSHOTS], ['--round-counts']]);
  assert.equal(read.state, 'counted', 'an older coai-mcp lost the count');
  assert.equal(read.state === 'counted' ? read.answer.stores : 'unavailable', undefined, 'the single-store answer grew stores');
  assert.equal(read.state === 'counted' ? read.answer.storesNotIncluded : 'unavailable', undefined);
});

test('a binary that knows neither mode is unavailable with today\'s sentence — never a count', async () => {
  const read = await readRoundCounts('coai-mcp', SNAPSHOTS, fake({}).run);

  assert.equal(read.state, 'unavailable');
  assert.match(read.state === 'unavailable' ? read.why : '', /older than --round-counts/u);
});

test('AMergedCallThatTimesOut_FallsBackToThisSide_AndNamesTheStoresAsNotAnswering', async () => {
  const spawn = fake({ '--round-counts-merged': { code: -1, output: '' }, '--round-counts': { code: 0, output: ANSWER_JSON } });

  const read = await readRoundCounts('coai-mcp', SNAPSHOTS, spawn.run);

  assert.deepEqual(spawn.asked.map((one) => one[0]), ['--round-counts-merged', '--round-counts']);
  assert.equal(read.state, 'counted');
  const named = read.state === 'counted' ? (read.answer.storesNotIncluded ?? []) : [];
  assert.deepEqual(named.map((one) => one.path), [...SNAPSHOTS], 'every snapshot store must be named, in the order it was handed');
  for (const one of named) {
    assert.match(one.detail, /did not answer in time/u);
  }
});

test('a merged request coai-mcp refuses (65) or cannot read (74) is unavailable with its reason — the fallback is for 64 and a timeout only', async () => {
  for (const [code, why] of [[65, /refused/u], [74, /could not read its rounds database/u]] as const) {
    const spawn = fake({ '--round-counts-merged': { code, output: '' }, '--round-counts': { code: 0, output: ANSWER_JSON } });

    const read = await readRoundCounts('coai-mcp', SNAPSHOTS, spawn.run);

    assert.deepEqual(spawn.asked.map((one) => one[0]), ['--round-counts-merged'], `exit ${code} fell back and hid the fault`);
    assert.equal(read.state, 'unavailable');
    assert.match(read.state === 'unavailable' ? read.why : '', why);
  }
});

test('no binary is no count', async () => {
  assert.equal((await readRoundCounts('', SNAPSHOTS, async () => ({ code: 0, output: ANSWER_JSON }))).state, 'unavailable');
});

// ---------------------------------------------------------------------------------------------------------------------
// E6.S2 — the other stores' ledger tails, read beside this side's (the loader).
// ---------------------------------------------------------------------------------------------------------------------

const NOW = Date.parse('2026-10-10T12:00:00Z');

function dirWithLedger(lines: readonly object[] | undefined): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-store-'));
  if (lines !== undefined) {
    fs.writeFileSync(path.join(dir, 'usage.jsonl'), lines.map((one) => `${JSON.stringify(one)}\n`).join(''));
  }

  return dir;
}

function launch(over: Record<string, unknown> = {}): object {
  return { utc: new Date(NOW - 3_600_000).toISOString(), provider: 'codex', model: 'gpt-6-luna', tokensIn: 1000, tokensOut: 10, costUsd: 1, outcome: 'ok', attempt: 'first', cli: 'codex', ...over };
}

/** The merged fixture with the WSL store's `paths[]` replaced by the given snapshot paths, as the mode would echo them. */
function mergedNaming(...paths: string[]): string {
  const answer = JSON.parse(MERGED_JSON) as { stores: { paths: string[] }[] };
  answer.stores[1]!.paths = paths;

  return JSON.stringify(answer);
}

const snapshotIn = (directory: string): string => path.join(directory, 'round-counts-snapshot.json');

/** A spawn that answers the merged mode with the given exit and the plain mode with the plain fixture. */
function answering(merged: Spawned): (args: readonly string[]) => Promise<Spawned> {
  return async (args) => (args[0] === '--round-counts-merged' ? merged : { code: 0, output: ANSWER_JSON });
}

function cleanup(...dirs: string[]): void {
  for (const dir of dirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

/** The real tail read, recording which files it was asked for. */
function recordingReads(reads: string[]): ForeignReads {
  return {
    deadlineMs: 1_000,
    read: async (file, since) => {
      reads.push(file);

      return readTailSince(file, since, parseUsageLine);
    },
  };
}

test('TheOtherStoresLedgerTail_IsReadBesideThisSides_WithItsStore (no store named: the directory stands for it)', async () => {
  const mine = dirWithLedger([launch({ tokensIn: 1 })]);
  const other = dirWithLedger([launch({ tokensIn: 2 })]);
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 64, output: '' }), [], [other]);

  const loaded = await load(NOW);

  assert.deepEqual(loaded.ledger.map((one) => [one.tokensIn, one.store]), [[1, undefined], [2, other]],
    'the other store\'s tail was not read beside this side\'s, or it carries no store');
  assert.equal(loaded.ledgerFailure, '');
  assert.deepEqual(loaded.otherLedgers, [{ store: other, directory: other, failure: '' }]);
  cleanup(mine, other);
});

test('a store the merged answer names is tagged with ITS ID — and this side\'s own lines with this side\'s', async () => {
  const mine = dirWithLedger([launch({ tokensIn: 1 })]);
  const wsl = dirWithLedger([launch({ tokensIn: 2 })]);
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 0, output: mergedNaming(snapshotIn(wsl)) }), [], [wsl]);

  const loaded = await load(NOW);

  assert.deepEqual(loaded.ledger.map((one) => [one.tokensIn, one.store]), [[1, THIS_STORE], [2, WSL_STORE]]);
  assert.deepEqual(loaded.otherLedgers, [{ store: WSL_STORE, directory: wsl, failure: '' }]);
  cleanup(mine, wsl);
});

test('ATailThatWillNotRead_IsNotIncluded_LikeTheSnapshot — in otherLedgers, NEVER in the global ledgerFailure', async () => {
  const mine = dirWithLedger([launch({ tokensIn: 1 })]);
  const broken = dirWithLedger(undefined);
  fs.mkdirSync(path.join(broken, 'usage.jsonl')); // a directory in the ledger's place: it exists and will not read
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 64, output: '' }), [], [broken]);

  const loaded = await load(NOW);

  assert.equal(loaded.ledgerFailure, '', 'a foreign failure hid every money cell of THIS side');
  assert.deepEqual(loaded.ledger.map((one) => one.tokensIn), [1], 'this side\'s line was lost with the other tail');
  assert.equal(loaded.otherLedgers?.length, 1);
  assert.equal(loaded.otherLedgers?.[0]?.store, broken);
  assert.match(loaded.otherLedgers?.[0]?.failure ?? '', /not a file/u);
  cleanup(mine, broken);
});

test('AnOtherStoreWithNoLedger_IsNoLines_NotAFailure', async () => {
  const mine = dirWithLedger([launch()]);
  const empty = dirWithLedger(undefined);
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 64, output: '' }), [], [empty]);

  const loaded = await load(NOW);

  assert.deepEqual(loaded.otherLedgers, [{ store: empty, directory: empty, failure: '' }]);
  assert.equal(loaded.ledger.length, 1);
  cleanup(mine, empty);
});

test('TwoSpellingsOfOneStore_AreReadOnce — the store is read through its first directory and its lines are counted once', async () => {
  const mine = dirWithLedger([]);
  const first = dirWithLedger([launch({ tokensIn: 2 })]);
  const second = dirWithLedger([launch({ tokensIn: 2 })]);
  const reads: string[] = [];
  const merged = { code: 0, output: mergedNaming(snapshotIn(first), snapshotIn(second)) };
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering(merged), [], [first, second], recordingReads(reads));

  const loaded = await load(NOW);

  assert.deepEqual(reads, [path.join(first, 'usage.jsonl')], 'a store two spellings reach was read twice');
  assert.equal(loaded.ledger.length, 1, 'the same ledger was counted twice');
  assert.equal(loaded.otherLedgers?.length, 1);
  cleanup(mine, first, second);
});

test('this side\'s own store reached through another spelling is not a foreign store — its ledger is already read', async () => {
  const mine = dirWithLedger([launch({ tokensIn: 1 })]);
  const reads: string[] = [];
  const own = JSON.parse(MERGED_JSON) as { stores: { paths: string[] }[] };
  own.stores[0]!.paths = [snapshotIn(mine)]; // the answer's LIVE store says this path is its own
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 0, output: JSON.stringify(own) }), [], [mine], recordingReads(reads));

  const loaded = await load(NOW);

  assert.deepEqual(reads, [], 'this side\'s own ledger was read a second time as another store');
  assert.deepEqual(loaded.ledger.map((one) => one.tokensIn), [1]);
  cleanup(mine);
});

test('AForeignTailThatNeverAnswers_DoesNotHoldThisSidesCounts — did not answer in time, and a second refresh starts no second read', async () => {
  const mine = dirWithLedger([launch({ tokensIn: 1 })]);
  const dead = dirWithLedger(undefined);
  let reads = 0;
  const foreign: ForeignReads = { deadlineMs: 30, read: () => { reads += 1; return new Promise(() => undefined); } };
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: -1, output: '' }), [], [dead], foreign);

  const first = await load(NOW);
  const second = await load(NOW + 1_000);

  assert.equal(first.read.state, 'counted', 'this side\'s counts waited on the dead tail');
  assert.deepEqual(first.ledger.map((one) => one.tokensIn), [1]);
  assert.equal(first.ledgerFailure, '');
  assert.match(first.otherLedgers?.[0]?.failure ?? '', /did not answer in time/u);
  assert.match(second.otherLedgers?.[0]?.failure ?? '', /did not answer in time/u);
  assert.equal(reads, 1, 'a refresh while a read was pending started another one');
  cleanup(mine, dead);
});

test('a foreign read that settles after its deadline is used by the next refresh — and then a fresh read may start', async () => {
  const mine = dirWithLedger([]);
  const slow = dirWithLedger(undefined);
  let release: (tail: Tail<UsageEntry>) => void = () => undefined;
  let reads = 0;
  const foreign: ForeignReads = { deadlineMs: 20, read: () => { reads += 1; return new Promise((resolve) => { release = resolve; }); } };
  const load = roundsLoader(() => 'coai-mcp', path.join(mine, 'usage.jsonl'), answering({ code: 64, output: '' }), [], [slow], foreign);

  await load(NOW);
  release({ records: [parseUsageLine(JSON.stringify(launch({ tokensIn: 7 })))!], bytesRead: 1, failure: '' });
  const later = await load(NOW + 1_000);

  assert.deepEqual(later.ledger.map((one) => one.tokensIn), [7], 'the late answer was thrown away');
  assert.equal(later.otherLedgers?.[0]?.failure, '');
  assert.equal(reads, 1);
  await load(NOW + 2_000);
  assert.equal(reads, 2, 'an answered read was reused for ever');
  cleanup(mine, slow);
});
