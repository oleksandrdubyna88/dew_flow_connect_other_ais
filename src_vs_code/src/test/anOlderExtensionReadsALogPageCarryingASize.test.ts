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
 * <p>`roundsDb.ts`, `roundsLog.ts` and `rounds.ts` are byte-identical to the newest published extension's
 * (`git diff extension-v0.75.0 -- src_vs_code/src/roundsDb.ts src_vs_code/src/roundsLog.ts src_vs_code/src/rounds.ts` was
 * empty when this was written), so this parser IS the released parser; the epic changes nothing under `src_vs_code/src`.</p>
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

function page(withSize: boolean): string {
  return JSON.stringify({
    rounds: [{
      repoPath: 'D:\\repo', branch: 'main', stage: 'CodeReview', number: 1,
      startedUtc: '2026-10-01T07:41:00.000Z', sessionId: 's1', accepted: 1, rejected: 0,
      findings: [], cursor: '2026-10-01T07:41:00.000Z|1', foundCount: 1, resolvedUtc: '', note: '',
      ...(withSize ? { size: SIZE } : {}),
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
