import { notify } from './notify';
import { TOO_OLD_FOR_COMMENTS } from './commentContract';
import type { KeepWrite } from './roundsDbRead';

/**
 * What a decision write came to, said — or nothing, when it all landed.
 *
 * <p><b>A module of its own because it speaks through VS Code</b>, and `reviewComment.ts` must not:
 * that one is pure, and `roundsDbRead.ts` imports from it, which the unit suite loads with no
 * editor at all. Putting this there made three test files fail to LOAD on `Cannot find module
 * 'vscode'` — a failure a type check does not see. Moved out of `bugzReviewPanel.ts`, which the
 * comment's write took past the 800 lines the lint allows.</p>
 *
 * <p>A FAILURE, not "none of them existed": the two send a person to different places, and saying
 * the wrong one sends them to collect again for nothing. (Code round, codex.) And `notify`, which
 * waits for the DISK and not for the person: a VS Code error toast does not dismiss itself, so a
 * write that waited on one froze the panel until somebody closed it. (gemini, the code round.)</p>
 *
 * <p><b>A binary too old for comments is its own sentence</b> (story 4.2): the words are still in
 * their box, and what to do is install a newer server — not retry, and not re-decide.</p>
 */
export async function reportWrite(written: KeepWrite, asked: number): Promise<void> {
  if (!written.ok) {
    await notify(failureOf(written.why, written.tooOld === true));
  } else if (written.decided !== asked) {
    await notify({
      as: 'warning',
      class: 'failure',
      source: 'bugzReview',
      code: 'bugz-decisions-partly-written',
      title: `${written.decided} of ${asked} decisions were written. The rest name pairs this `
        + 'database does not have — collect again and they will come back.',
      detail: `${written.decided} of ${asked}`,
    });
  }
}

/** The notice for a write that failed — the binary too old for comments, or anything else. */
function failureOf(why: string, tooOld: boolean): Parameters<typeof notify>[0] {
  return tooOld
    ? { as: 'error', class: 'failure', source: 'bugzReview', code: 'bugz-comment-needs-a-newer-server', title: TOO_OLD_FOR_COMMENTS, detail: why }
    : { as: 'error', class: 'failure', source: 'bugzReview', code: 'bugz-decision-not-saved', title: `The decision could not be saved: ${why}`, detail: why };
}
