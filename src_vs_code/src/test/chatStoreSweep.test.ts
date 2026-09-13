import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  DEBRIS_AGE_MS,
  HEARTBEAT_SHAPE,
  HEARTBEAT_STALE_MS,
  HeartbeatFile,
  MARKER_SHAPE,
  NOTHING_TO_SWEEP,
  ORPHAN_GRACE_MS,
  SWEEP_BUDGET_MS,
  SWEEP_CLAIM_MS,
  SWEEP_EVERY_MS,
  Stamp,
  Survey,
  SweepMarker,
  SweepReport,
  describeSweep,
  heartbeatName,
  heartbeatOwner,
  heartbeatText,
  liveHeartbeat,
  markerText,
  parseHeartbeat,
  parseMarker,
  planIsEmpty,
  planSweep,
  heldElsewhere,
  protectedIds,
  quarantinedAt,
  sweepDue,
} from '../chatStoreSweep';
import { CONVERSATION_VERSION, ConversationMeta, KEEP_FOR_MS, sourceOfSession } from '../chatStore';

/**
 * The rules of the sweep, as values against a pinned clock — nothing here touches a disk.
 *
 * <p>Every rule that deletes by clock is a rule that can delete the wrong thing, so each has a test
 * on both sides of its boundary and a test for what protects a record from it: the heartbeat of a
 * window that holds it, and the set this window holds right now. The I/O that carries these out is
 * `chatStoreKeeper.test.ts`; the re-check under the lock is `chatStoreRetire.test.ts`.</p>
 */

const NOW = Date.UTC(2026, 8, 13, 12, 0, 0);
const OWN_PID = 4242;

const stamp = (mtimeMs: number, size = 300): Stamp => ({ mtimeMs, size });

function meta(over: Partial<ConversationMeta> = {}): ConversationMeta {
  return {
    version: CONVERSATION_VERSION,
    id: 'a1',
    rev: 1,
    title: 'main',
    modelId: 'gpt-5.4',
    turns: 2,
    lastLine: 'because',
    source: sourceOfSession('9f1c-uuid'),
    workspace: 'D:\\rsd\\coai',
    updatedAt: NOW - 1_000,
    ...over,
  };
}

/** A directory as the survey would classify it, with every list empty unless the test fills it. */
function survey(over: Partial<Survey> = {}): Survey {
  return {
    records: new Map(),
    metas: new Map(),
    locks: new Map(),
    temps: [],
    quarantined: [],
    heartbeats: [],
    foreign: [],
    ...over,
  };
}

/** A conversation as the directory holds one: a record and its metadata, both written at `at`. */
function pair(ids: readonly string[], at = NOW - 1_000): Pick<Survey, 'records' | 'metas'> {
  return {
    records: new Map(ids.map((id) => [id, stamp(at, 20_000)])),
    metas: new Map(ids.map((id) => [id, stamp(at)])),
  };
}

/** Another window's heartbeat, as the survey hands it over. */
function beat(pid: number, ids: readonly string[], at: number): HeartbeatFile {
  return { name: heartbeatName(pid), stamp: stamp(at, 120), beat: { pid, at, ids } };
}

const EXPIRED_AT = NOW - KEEP_FOR_MS - 1;

// ---------------------------------------------------------------------------------------------
// Expiry: ninety days from the last write, nominated with the revision the listing showed.
// ---------------------------------------------------------------------------------------------

test('a conversation last written over ninety days ago is nominated, with the revision the listing showed', () => {
  const plan = planSweep(survey(pair(['a1'])), [meta({ id: 'a1', rev: 3, updatedAt: EXPIRED_AT })], NOW, [], OWN_PID);

  assert.deepEqual(plan.expired, [{ id: 'a1', rev: 3 }], 'an expired conversation was not nominated, or without its revision');
});

test('exactly ninety days is not expired; one millisecond more is', () => {
  const edge = [meta({ id: 'a1', updatedAt: NOW - KEEP_FOR_MS })];
  const past = [meta({ id: 'a1', updatedAt: NOW - KEEP_FOR_MS - 1 })];

  assert.deepEqual(planSweep(survey(pair(['a1'])), edge, NOW, [], OWN_PID).expired, [], 'a conversation exactly at the window was retired');
  assert.equal(planSweep(survey(pair(['a1'])), past, NOW, [], OWN_PID).expired.length, 1, 'a conversation one millisecond past the window was kept');
});

test('a conversation used yesterday is not nominated, whatever its revision', () => {
  const plan = planSweep(survey(pair(['a1'])), [meta({ id: 'a1', rev: 40, updatedAt: NOW - 86_400_000 })], NOW, [], OWN_PID);

  assert.deepEqual(plan, NOTHING_TO_SWEEP, 'a live conversation was nominated for something');
});

test('an expired listing whose record is NOT there is an orphaned index entry, never an expiry', () => {
  // The two nominations go to two different store operations, and the retire one would answer
  // "absent" and leave the row where it is.
  const alone = survey({ metas: new Map([['a1', stamp(NOW)]]) });

  const plan = planSweep(alone, [meta({ id: 'a1', updatedAt: EXPIRED_AT })], NOW, [], OWN_PID);

  assert.deepEqual(plan.expired, [], 'a listing with no record behind it was nominated for retirement');
  assert.deepEqual(plan.orphanMetas, ['a1'], 'a row that opens onto nothing was left');
});

// ---------------------------------------------------------------------------------------------
// What protects a record: a live heartbeat, or this window's own tabs.
// ---------------------------------------------------------------------------------------------

test('an expired conversation named by a LIVE heartbeat of another window is left alone', () => {
  // The finding all three vendors converged on: the directory is shared, so a window-scoped check
  // protects nothing. Window A holds it open for three months; window B must not sweep it.
  const held = survey({ ...pair(['a1']), heartbeats: [beat(7, ['a1'], NOW - 60_000)] });

  const plan = planSweep(held, [meta({ id: 'a1', updatedAt: EXPIRED_AT })], NOW, [], OWN_PID);

  assert.deepEqual(plan.expired, [], 'a conversation open in another window was nominated for deletion');
});

test('a heartbeat nobody has refreshed protects nothing, and is itself collected', () => {
  const stale = survey({ ...pair(['a1']), heartbeats: [beat(7, ['a1'], NOW - HEARTBEAT_STALE_MS)] });

  const plan = planSweep(stale, [meta({ id: 'a1', updatedAt: EXPIRED_AT })], NOW, [], OWN_PID);

  assert.deepEqual(plan.expired, [{ id: 'a1', rev: 1 }], 'a dead window went on protecting what it once held');
  assert.deepEqual(plan.staleHeartbeats, [heartbeatName(7)], 'the stale heartbeat was not collected');
});

test('a heartbeat one refresh short of stale is still believed', () => {
  const nearly = survey({ ...pair(['a1']), heartbeats: [beat(7, ['a1'], NOW - HEARTBEAT_STALE_MS + 1)] });

  const plan = planSweep(nearly, [meta({ id: 'a1', updatedAt: EXPIRED_AT })], NOW, [], OWN_PID);

  assert.deepEqual(plan.expired, [], 'a heartbeat inside the window was disbelieved');
  assert.deepEqual(plan.staleHeartbeats, [], 'a heartbeat inside the window was collected');
});

test('a heartbeat dated in the future is live — a stepped clock over-protects, never exposes', () => {
  assert.equal(liveHeartbeat({ pid: 7, at: NOW + 3_600_000, ids: [] }, NOW), true);
  assert.equal(liveHeartbeat({ pid: 7, at: NOW - HEARTBEAT_STALE_MS + 1, ids: [] }, NOW), true);
  assert.equal(liveHeartbeat({ pid: 7, at: NOW - HEARTBEAT_STALE_MS, ids: [] }, NOW), false);
});

test('this window’s own open conversations are protected even before its heartbeat has named them', () => {
  // The heartbeat is a minute behind the tabs. A tab restored a moment ago is in `held`, and `held`
  // is read at decision time.
  const plan = planSweep(survey(pair(['a1', 'b2'])), [
    meta({ id: 'a1', updatedAt: EXPIRED_AT }),
    meta({ id: 'b2', updatedAt: EXPIRED_AT }),
  ], NOW, ['a1'], OWN_PID);

  assert.deepEqual(plan.expired, [{ id: 'b2', rev: 1 }], 'a conversation this window holds open was nominated, or the other was spared');
});

test('this window’s own heartbeat file is never collected, however old it reads', () => {
  const own = survey({ heartbeats: [beat(OWN_PID, ['a1'], NOW - HEARTBEAT_STALE_MS * 4)] });

  assert.deepEqual(planSweep(own, [], NOW, [], OWN_PID).staleHeartbeats, [], 'the sweep collected the heartbeat of the window running it');
});

test('a torn heartbeat file protects nothing and is aged by its mtime', () => {
  const torn = (at: number): HeartbeatFile => ({ name: heartbeatName(9), stamp: stamp(at, 0), beat: undefined });

  assert.deepEqual(planSweep(survey({ heartbeats: [torn(NOW - 1_000)] }), [], NOW, [], OWN_PID).staleHeartbeats, [], 'a torn but fresh heartbeat — one being written — was collected');
  assert.deepEqual(planSweep(survey({ heartbeats: [torn(NOW - HEARTBEAT_STALE_MS)] }), [], NOW, [], OWN_PID).staleHeartbeats, [heartbeatName(9)], 'a torn old heartbeat was kept for ever');
  assert.deepEqual([...protectedIds([torn(NOW)], NOW, [])], [], 'a torn heartbeat protected something');
});

test('the protected set is the union of every live heartbeat and what this window holds', () => {
  const ids = protectedIds([beat(7, ['a1'], NOW), beat(8, ['b2'], NOW - HEARTBEAT_STALE_MS), beat(9, ['c3'], NOW - 1)], NOW, ['d4']);

  assert.deepEqual([...ids].sort(), ['a1', 'c3', 'd4']);
});

test('what ANOTHER window holds leaves out this one, so the picker never refuses to reopen its own', () => {
  // The picker asks a different question from the sweep's. The sweep asks "may this be deleted" and
  // every window's answer counts, this one included. The picker asks "would reopening this give the
  // record a second writer" — and this window's own tabs are not an obstacle to that, they are the
  // case it reveals. Believing our own heartbeat here would be worse than useless: it is written at
  // most once a minute, so it goes on naming a conversation whose tab closed seconds ago, and the
  // picker would refuse to reopen something nothing holds.
  const ours = beat(OWN_PID, ['mine'], NOW);
  const theirs = beat(7, ['theirs'], NOW);
  const gone = beat(8, ['stale'], NOW - HEARTBEAT_STALE_MS);

  assert.deepEqual([...heldElsewhere([ours, theirs, gone], NOW, OWN_PID)].sort(), ['theirs']);
  // Liveness is judged at the moment of ASKING, not of the survey: a picker left open must stop
  // believing a window that has since gone quiet, or it holds those conversations hostage.
  assert.deepEqual([...heldElsewhere([theirs], NOW + HEARTBEAT_STALE_MS, OWN_PID)], [],
    'a heartbeat that went stale while the list was up still held its conversations');
  assert.deepEqual([...heldElsewhere([], NOW, OWN_PID)], [], 'no heartbeats at all is not an empty answer');
});

// ---------------------------------------------------------------------------------------------
// Orphans: a row onto nothing goes; a transcript nobody lists is set aside, and only after a day.
// ---------------------------------------------------------------------------------------------

test('a transcript with no metadata beside it is NOT nominated inside the grace period', () => {
  // Between a save's two writes the record is legitimately alone; an unbounded rule would set aside
  // conversations that are being created.
  const young = survey({ records: new Map([['a1', stamp(NOW - ORPHAN_GRACE_MS)]]) });

  assert.deepEqual(planSweep(young, [], NOW, [], OWN_PID).orphanRecords, [], 'a record a day old was set aside; the grace is exclusive at the boundary');
});

test('a transcript with no metadata beside it, past the grace period, is nominated for QUARANTINE — never for deletion', () => {
  const old = survey({ records: new Map([['a1', stamp(NOW - ORPHAN_GRACE_MS - 1)]]) });

  const plan = planSweep(old, [], NOW, [], OWN_PID);

  assert.deepEqual(plan.orphanRecords, ['a1'], 'an orphaned transcript past the grace period was left invisible for ever');
  assert.deepEqual(plan.expired, [], 'an orphaned transcript was nominated for deletion');
  assert.deepEqual(plan.orphanMetas, [], 'an orphaned transcript was mistaken for an orphaned index entry');
});

test('an orphaned transcript a live window holds is not set aside — a partial save is being retried there', () => {
  const old = survey({ records: new Map([['a1', stamp(NOW - ORPHAN_GRACE_MS - 1)]]), heartbeats: [beat(7, ['a1'], NOW)] });

  assert.deepEqual(planSweep(old, [], NOW, [], OWN_PID).orphanRecords, [], 'a transcript a window is still writing was set aside from under it');
});

test('an orphaned index entry a live window holds is left for that window to repair', () => {
  const alone = survey({ metas: new Map([['a1', stamp(NOW)]]), heartbeats: [beat(7, ['a1'], NOW)] });

  assert.deepEqual(planSweep(alone, [], NOW, [], OWN_PID).orphanMetas, []);
});

// ---------------------------------------------------------------------------------------------
// Debris: temporaries and locks, an hour old.
// ---------------------------------------------------------------------------------------------

test('a temporary younger than an hour may be a write in flight and is kept; an older one is a crash and goes', () => {
  const temps = survey({
    temps: [
      { name: 'a1.json.100.1.tmp', stamp: stamp(NOW - DEBRIS_AGE_MS, 4_000) },
      { name: 'b2.json.100.2.tmp', stamp: stamp(NOW - DEBRIS_AGE_MS - 1, 4_000) },
    ],
  });

  assert.deepEqual(planSweep(temps, [], NOW, [], OWN_PID).staleTemps, ['b2.json.100.2.tmp']);
});

test('a lock is collected only when it is far older than the lock module’s own window — an hour, not thirty seconds', () => {
  // The sweep does not break locks: `chatStoreLock.ts` owns that rule. What the sweep nominates is a
  // lock so old that claiming through the store — which runs the one breaking path there is — is the
  // right thing to do for an id nobody will write to again.
  const locks = survey({
    locks: new Map([
      ['a1', stamp(NOW - 31_000, 80)],
      ['b2', stamp(NOW - DEBRIS_AGE_MS - 1, 80)],
    ]),
  });

  assert.deepEqual(planSweep(locks, [], NOW, [], OWN_PID).abandonedLocks, ['b2'], 'a lock thirty seconds old was nominated, or an hour-old one was not');
});

// ---------------------------------------------------------------------------------------------
// Quarantine: dated by the instant in the name, kept ninety days.
// ---------------------------------------------------------------------------------------------

test('a quarantined value is dated by the instant in its name, and one without an instant is never collected', () => {
  assert.equal(quarantinedAt('coai.chatTabs-entry-0-1789000000000.json'), 1_789_000_000_000);
  assert.equal(quarantinedAt('a1-orphan-1789000000000.json'), 1_789_000_000_000);
  assert.equal(quarantinedAt('notes.json'), undefined);
  assert.equal(quarantinedAt('a1-orphan-1789000000000.txt'), undefined);

  const files = survey({
    quarantined: [
      { name: `old-${NOW - KEEP_FOR_MS - 1}.json`, stamp: stamp(NOW - 1, 500) },
      { name: `edge-${NOW - KEEP_FOR_MS}.json`, stamp: stamp(NOW - 1, 500) },
      { name: 'undated.json', stamp: stamp(NOW - KEEP_FOR_MS * 3, 500) },
    ],
  });

  assert.deepEqual(planSweep(files, [], NOW, [], OWN_PID).oldQuarantine, [`old-${NOW - KEEP_FOR_MS - 1}.json`],
    'a quarantined value was collected by its mtime, at the boundary, or an undated one was collected at all');
});

// ---------------------------------------------------------------------------------------------
// One window sweeps: the marker.
// ---------------------------------------------------------------------------------------------

const finishedAt = (finished: number): SweepMarker => ({ kind: 'finished', pid: 7, began: finished - 5_000, finished });
const claimedAt = (began: number): SweepMarker => ({ kind: 'claimed', pid: 7, began });

test('a sweep is due with no marker, not due behind a recent FINISHED one, and due again a day later', () => {
  assert.equal(sweepDue(undefined, NOW), true, 'a store that has never been swept was not swept');
  assert.equal(sweepDue(finishedAt(NOW - 3_600_000), NOW), false, 'a second window swept an hour after the first');
  assert.equal(sweepDue(finishedAt(NOW - SWEEP_EVERY_MS), NOW), true, 'a day-old marker still blocked the sweep');
});

test('a CLAIM stands only for as long as a sweep can run: not due inside the claim window, due the moment it is past', () => {
  // The claim is what stops six windows sweeping at once; a survey that fails behind it, or a budget
  // that runs out, leaves it to age out in minutes rather than a day. (codex and the local round.)
  assert.equal(sweepDue(claimedAt(NOW - 1_000), NOW), false, 'a window opened beside a running sweep swept too');
  assert.equal(sweepDue(claimedAt(NOW - SWEEP_CLAIM_MS + 1), NOW), false, 'a claim inside its window was disbelieved');
  assert.equal(sweepDue(claimedAt(NOW - SWEEP_CLAIM_MS), NOW), true, 'a claim nobody finished blocked the sweep past its window');
  assert.ok(SWEEP_CLAIM_MS >= SWEEP_BUDGET_MS, 'a claim can age out while the sweep that took it is still inside its budget');
});

test('a marker from the future blocks for at most its window — a stepped clock cannot stop sweeping for a year', () => {
  assert.equal(sweepDue(finishedAt(NOW + 3_600_000), NOW), false);
  assert.equal(sweepDue(finishedAt(NOW + SWEEP_EVERY_MS), NOW), true, 'a marker a day in the future blocked the sweep');
  assert.equal(sweepDue(claimedAt(NOW + 1_000), NOW), false);
  assert.equal(sweepDue(claimedAt(NOW + SWEEP_CLAIM_MS), NOW), true, 'a claim from the future blocked the sweep past its window');
});

test('both kinds of marker round-trip through their text, with every instant in UTC', () => {
  const claim = markerText(claimedAt(NOW));
  assert.match(claim, /"began":"2026-09-13T12:00:00\.000Z"/u, 'the claim is not dated in UTC');
  assert.doesNotMatch(claim, /finished/u, 'a claim carries a finished instant');
  assert.deepEqual(parseMarker(JSON.parse(claim)), { kind: 'marker', marker: claimedAt(NOW) });

  const done = markerText(finishedAt(NOW));
  assert.match(done, /"finished":"2026-09-13T12:00:00\.000Z"/u);
  assert.deepEqual(parseMarker(JSON.parse(done)), { kind: 'marker', marker: finishedAt(NOW) });
});

test('a marker of another shape is invalid, and the reason names the legal shape', () => {
  // A malformed marker used to read silently as "no marker"; it is a value somebody could hand-edit,
  // and its reader says why it was refused. (The code round.)
  const began = new Date(NOW).toISOString();
  const cases: readonly [unknown, RegExp][] = [
    ['text', /not an object/u],
    [{ version: 99, pid: 7, began }, /version 99 is not 1/u],
    [{ version: 1, pid: 0, began }, /pid 0 is not a positive whole number/u],
    [{ version: 1, pid: 7, began: 'yesterday' }, /began yesterday is not a UTC instant/u],
    [{ version: 1, pid: 7, began, finished: 'noon' }, /finished noon is not a UTC instant/u],
    [{ version: 1, pid: 7, at: began }, /began undefined is not a UTC instant/u],
  ];
  for (const [value, expected] of cases) {
    const out = parseMarker(value);
    assert.equal(out.kind, 'invalid', `${JSON.stringify(value)} was accepted as a marker`);
    const reason = out.kind === 'invalid' ? out.reason : '';
    assert.match(reason, expected);
    assert.ok(reason.endsWith(MARKER_SHAPE), `the reason for ${JSON.stringify(value)} does not name the legal shape: ${reason}`);
  }
});

// ---------------------------------------------------------------------------------------------
// The heartbeat's shape, strict both ways.
// ---------------------------------------------------------------------------------------------

test('a heartbeat round-trips through its text, with the instant in UTC', () => {
  const text = heartbeatText(7, ['a1', 'b2'], NOW);

  assert.match(text, /"at":"2026-09-13T12:00:00\.000Z"/u, 'the heartbeat is not dated in UTC');
  assert.deepEqual(parseHeartbeat(JSON.parse(text)), { kind: 'heartbeat', beat: { pid: 7, at: NOW, ids: ['a1', 'b2'] } });
});

test('a heartbeat of the wrong shape is invalid, and the reason names the legal shape', () => {
  const cases: readonly [unknown, RegExp][] = [
    ['text', /not an object/u],
    [{ version: 2, pid: 7, at: new Date(NOW).toISOString(), ids: [] }, /version 2 is not 1/u],
    [{ version: 1, pid: 0, at: new Date(NOW).toISOString(), ids: [] }, /pid 0 is not a positive whole number/u],
    [{ version: 1, pid: 7, at: 'noon', ids: [] }, /at noon is not a UTC instant/u],
    [{ version: 1, pid: 7, at: new Date(NOW).toISOString(), ids: 'a1' }, /ids is not an array/u],
    [{ version: 1, pid: 7, at: new Date(NOW).toISOString(), ids: ['a1', '../x'] }, /id "\.\.\/x" is not a conversation id/u],
  ];
  for (const [value, expected] of cases) {
    const out = parseHeartbeat(value);
    assert.equal(out.kind, 'invalid', `${JSON.stringify(value)} was accepted as a heartbeat`);
    const reason = out.kind === 'invalid' ? out.reason : '';
    assert.match(reason, expected);
    assert.ok(reason.endsWith(HEARTBEAT_SHAPE), `the reason for ${JSON.stringify(value)} does not name the legal shape: ${reason}`);
  }
});

test('a heartbeat file is named for its window’s pid, the way the orphan ledger names its file', () => {
  assert.equal(heartbeatName(4242), 'window-4242.json');
  assert.equal(heartbeatOwner('window-4242.json'), 4242);
  assert.equal(heartbeatOwner('sweep.json'), 0, 'the marker was read as a window');
  assert.equal(heartbeatOwner('window-x.json'), 0);
});

// ---------------------------------------------------------------------------------------------
// The report.
// ---------------------------------------------------------------------------------------------

test('an empty plan is empty, and a report reads as one line a person can act on', () => {
  assert.equal(planIsEmpty(NOTHING_TO_SWEEP), true);
  assert.equal(planIsEmpty({ ...NOTHING_TO_SWEEP, staleTemps: ['x.tmp'] }), false);

  const swept: SweepReport = {
    kind: 'swept', retired: 3, keptUnderLock: 1, orphanMetasRemoved: 0, recordsQuarantined: 1,
    tempsRemoved: 2, locksCollected: 0, quarantineRemoved: 0, heartbeatsRemoved: 1, failed: 0, unfinished: false,
  };
  const line = describeSweep(swept);
  assert.match(line, /retired 3 expired/u);
  assert.match(line, /kept 1 that had changed under the lock/u);
  assert.match(line, /set aside 1 orphaned transcript/u);
  assert.doesNotMatch(line, /retried|budget/u);

  assert.match(describeSweep({ ...swept, failed: 2, unfinished: true }), /2 could not be done and will be retried; the budget ran out.*stays due$/u);
  assert.match(describeSweep({ kind: 'skipped', why: 'unavailable', reason: 'a file is where the conversation store should be' }),
    /did not run — the store could not be read \(a file is where the conversation store should be\)/u);
  assert.match(describeSweep({ kind: 'skipped', why: 'recent', reason: '' }), /another window swept recently$/u);
  assert.match(describeSweep({ kind: 'skipped', why: 'sweeping', reason: '' }), /another window is sweeping now$/u);
  assert.match(describeSweep({ kind: 'skipped', why: 'unclaimed', reason: '' }), /could not claim the store/u);
  assert.match(describeSweep({ kind: 'skipped', why: 'unannounced', reason: '' }), /could not announce what it holds open/u);
});

test('a heartbeat is THIS window\u2019s if EITHER its name or its body says so', () => {
  // A file names its window twice, and they agree in every file this code writes. The question asked
  // is whether either says ours, which is the safe direction: a file that might be ours is left out
  // of what somebody else holds, and being wrong that way costs a conversation this window can
  // reopen rather than a second tab on a record another window is writing. Two reviewers read the
  // inline form as its own opposite, which is why it is a named function now.
  const bodyOnly: HeartbeatFile = { name: heartbeatName(7), stamp: stamp(NOW, 120), beat: { pid: OWN_PID, at: NOW, ids: ['a1'] } };
  const nameOnly: HeartbeatFile = { name: heartbeatName(OWN_PID), stamp: stamp(NOW, 120), beat: { pid: 7, at: NOW, ids: ['b2'] } };

  assert.deepEqual([...heldElsewhere([bodyOnly], NOW, OWN_PID)], [], 'a heartbeat whose BODY says it is ours was read as another window');
  assert.deepEqual([...heldElsewhere([nameOnly], NOW, OWN_PID)], [], 'a heartbeat whose NAME says it is ours was read as another window');
  assert.deepEqual([...heldElsewhere([beat(7, ['c3'], NOW)], NOW, OWN_PID)], ['c3'], 'a heartbeat that is nobody ours was dropped');
});
