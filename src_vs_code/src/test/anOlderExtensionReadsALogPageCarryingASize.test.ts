import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseLog } from '../roundsDb';
import { roundsLogHtml, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';

/**
 * `--log` now carries a `size` on each code round (E2.S5 of
 * `todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md`, contract C3), and no page of THIS extension reads it yet:
 * the card line is E5's, the report E6's. The two halves of the product update separately, so an extension without that
 * work reads a server that sends it — and the only thing owed is that the page is the page it was.
 *
 * <p>`roundsDb.ts` is byte-identical to the newest published extension's
 * (`git diff extension-v0.80.1 -- src_vs_code/src/roundsDb.ts` is empty), so this parser IS the released parser — the one
 * thing this file proves. `roundsLog.ts` and `rounds.ts` are NOT pinned: the plan gate's E2.S3 adds the plan-source line to
 * them, and the page-level case below only says that the page a log page produces does not change with a member it ignores.</p>
 *
 * <p>The same holds for `planSource` (E2.S2 of `todo/PLAN_the_plan_gate_reads_the_committed_plan_E2.md`, contract C2): `--log`
 * now carries it on a plan round, and `parseLog` reads field by field, so the member is ignored.</p>
 *
 * <p>And for `authorsState` (E5.S3 of `todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md`): `--log` now carries the state
 * of each round's authors, and the released parser ignores it as it ignores the other two.</p>
 */

/** What `--log --paged` prints for one code round that has a size, binary cell included: `added`/`removed` are ABSENT there. */
const SIZE = {
  whole: {
    state: 'recorded',
    from: 'origin/main',
    cells: [
      { coverage: 'shown', kind: 'code', files: 3, added: 40, removed: 5 },
      { coverage: 'binary', kind: 'unknown', files: 2 },
    ],
  },
  delta: { state: 'unavailable: first round', from: '', cells: [] },
  origin: 'live',
};

/** What `--log --paged` prints for one committed plan round (E1's `PlanSource`, camelCase). */
const PLAN_SOURCE = {
  origin: 'commit',
  commit: 'a'.repeat(40),
  path: 'todo/PLAN_x.md',
  blob: 'b'.repeat(40),
  bytes: 98304,
  textId: '0123456789abcdef',
};

function page(withSize: boolean, withPlanSource = false, withAuthorsState = false): string {
  return JSON.stringify({
    rounds: [{
      repoPath: 'D:\\repo', branch: 'main', stage: withPlanSource ? 'PlanReview' : 'CodeReview', number: 1,
      startedUtc: '2026-10-01T07:41:00.000Z', sessionId: 's1', accepted: 1, rejected: 0,
      findings: [], cursor: '2026-10-01T07:41:00.000Z|1', foundCount: 1, resolvedUtc: '', note: '',
      ...(withSize ? { size: SIZE } : {}),
      ...(withPlanSource ? { planSource: PLAN_SOURCE } : {}),
      ...(withAuthorsState ? { authorsState: 'partial: peer running' } : {}),
    }],
    blindSpots: [],
    defended: [],
    totals: { rounds: 1, findings: 1 },
  });
}

test('AnOlderExtensionReadsALogPageCarryingASize: the same rounds, and no error', () => {
  const without = parseLog(page(false), true);
  const carrying = parseLog(page(true), true);

  assert.equal(carrying.read, true, 'the page was read, not refused as a shape from a future server');
  assert.equal(carrying.rounds.length, 1);
  assert.deepEqual(carrying, without, 'a member this parser does not know changes nothing it does');
});

test('AnOlderExtensionReadsALogPageCarryingAPlanSource: the same round, and no error', () => {
  const carrying = parseLog(page(false, true), true);
  const stripped = JSON.parse(page(false, true)) as { rounds: Record<string, unknown>[] };
  delete stripped.rounds[0].planSource;
  const plain = parseLog(JSON.stringify(stripped), true);

  assert.equal(carrying.read, true, 'the page was read, not refused as a shape from a future server');
  assert.equal(carrying.rounds.length, 1);
  assert.equal(carrying.rounds[0].stage, 'PlanReview');
  assert.deepEqual(carrying, plain, 'a member this parser does not know changes nothing it does');
});

test('AnOlderExtensionReadsALogPageCarryingAnAuthorsState: the same round, and no error', () => {
  const carrying = parseLog(page(false, false, true), true);
  const plain = parseLog(page(false), true);

  assert.equal(carrying.read, true, 'the page was read, not refused as a shape from a future server');
  assert.equal(carrying.rounds.length, 1);
  assert.deepEqual(carrying, plain, 'a round carrying authorsState parses equal to the same page without it');
});

/** The session file the page joins the database round to — the round the log page above describes. */
const SESSION = {
  state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
  rounds: [{
    stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 1 reviewers answered', status: 'done',
    startedUtc: '2026-10-01T07:41:00.000Z', completedUtc: '2026-10-01T07:43:10.000Z', subject: 'SCOPE',
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 0, note: '' }],
  } as unknown as RoundRecord],
} as unknown as SessionFile;

test('AnOlderExtensionReadsALogPageCarryingASize: the page it draws is the page it drew', () => {
  const now = Date.parse('2026-10-01T08:00:00.000Z');
  const draw = (text: string): { html: string; rows: number } => {
    const rows = rowsFrom([SESSION], now, () => undefined, [], parseLog(text, true));

    return { html: roundsLogHtml(rows, [], 'n'), rows: rows.length };
  };

  const carrying = draw(page(true));
  assert.equal(carrying.rows, 1, 'the round is still joined to its session row');
  assert.deepEqual(carrying, draw(page(false)));
});
