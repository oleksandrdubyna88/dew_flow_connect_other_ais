/**
 * How many consecutive runs of `main` the extension-host job has been green for.
 *
 * <p><b>Why this exists.</b> `.github/workflows/ci.yml` says the job is not required to merge and
 * that it should be PROMOTED to required <i>"once it has been green on TWENTY CONSECUTIVE RUNS OF
 * main — a number, so the promotion is a measurement rather than a mood"</i>. The rule was written
 * down and then nobody counted, which is the same failure as not having the rule: the promoted plan
 * had to record the promotion as *pending, nobody counting, no figure given because none was
 * measured*. A number a person has to go and assemble by hand is a number that stays unassembled.</p>
 *
 * <p><b>It reports, it does not gate.</b> Promotion means editing branch protection, which is the
 * operator's to do; this only makes the number impossible to not know. It exits 0 whether the streak
 * is 0 or 40 — and exits non-zero only when it could not find out, because a counter that answers
 * "0" when it actually failed to ask is worse than no counter.</p>
 */

import { argv, env, exit, stdout } from 'node:process';

/** The rule, from the comment beside the job it is about. One place to change it if it ever moves. */
export const PROMOTE_AT = 20;

/** The job as its `name:` renders it, which is what the API reports. */
export const JOB_NAME = 'extension · a real editor';

/**
 * The streak, newest first.
 *
 * @param {readonly {conclusion: string|null}[]} attempts the job's conclusion in each run of `main`,
 *   newest first. A run where the job did not appear at all is not an attempt and is not passed in.
 * @returns {number} how many from the newest concluded `success` before anything else did not.
 *
 * <p>A run still in flight (`conclusion: null`) ENDS the streak rather than being skipped. Skipping
 * it would let a counter say twenty while the newest run is failing right now; ending on it is the
 * conservative reading, and the streak resumes once it concludes.</p>
 */
export function streakOf(attempts) {
  let green = 0;
  for (const attempt of attempts) {
    if (attempt.conclusion !== 'success') {
      return green;
    }
    green += 1;
  }

  return green;
}

/** What to say about a streak — the number, the threshold, and what it means for the operator. */
export function asText(streak, promoteAt = PROMOTE_AT) {
  const ready = streak >= promoteAt;

  return [
    `extension-host streak on main: ${streak} of ${promoteAt} consecutive green runs`,
    ready
      ? `READY TO PROMOTE — make "${JOB_NAME}" a required status check.`
      : `not yet — ${promoteAt - streak} more green run(s) of main.`,
  ].join('\n');
}

/** One page of workflow runs of `main`, newest first. Separated so the counting above stays pure. */
async function runsOfMain(repo, token, perPage) {
  const url = `https://api.github.com/repos/${repo}/actions/runs`
    + `?branch=main&event=push&status=completed&per_page=${perPage}`;
  const headers = { accept: 'application/vnd.github+json', 'user-agent': 'coai-host-job-streak' };
  if (token) {
    headers.authorization = `Bearer ${token}`;
  }
  const answered = await fetch(url, { headers });
  if (!answered.ok) {
    throw new Error(`the runs of main could not be read: ${answered.status} ${answered.statusText}`);
  }
  const body = await answered.json();

  return body.workflow_runs ?? [];
}

/** The one job we care about, out of a run. Absent means the run predates the job. */
async function hostJobOf(repo, token, runId) {
  const headers = { accept: 'application/vnd.github+json', 'user-agent': 'coai-host-job-streak' };
  if (token) {
    headers.authorization = `Bearer ${token}`;
  }
  const answered = await fetch(
    `https://api.github.com/repos/${repo}/actions/runs/${runId}/jobs?per_page=100`,
    { headers },
  );
  if (!answered.ok) {
    throw new Error(`the jobs of run ${runId} could not be read: ${answered.status}`);
  }
  const body = await answered.json();

  return (body.jobs ?? []).find((job) => job.name === JOB_NAME);
}

/** Ask, count, say. */
async function main() {
  const repo = env.GITHUB_REPOSITORY ?? 'oleksandrdubyna88/dew_flow_connect_other_ais';
  const token = env.GITHUB_TOKEN ?? env.GH_TOKEN ?? '';
  // One more than the threshold is all that can change the answer: past PROMOTE_AT the streak is
  // "ready" whatever came before, so asking for a hundred runs would be a hundred job lookups for a
  // number that cannot move.
  const runs = await runsOfMain(repo, token, PROMOTE_AT + 5);
  const attempts = [];
  for (const run of runs) {
    const job = await hostJobOf(repo, token, run.id);
    if (job !== undefined) {
      attempts.push({ conclusion: job.conclusion });
    }
  }
  const streak = streakOf(attempts);
  stdout.write(`${asText(streak)}\n`);

  const summary = env.GITHUB_STEP_SUMMARY;
  if (summary) {
    const { appendFileSync } = await import('node:fs');
    appendFileSync(summary, `### ${asText(streak).split('\n').join('\n\n')}\n`);
  }
}

// Only when RUN, never when imported by a test — the counting above is what a test wants, and
// `sonar.coverage` cannot see anything a test reaches by spawning a process.
if (argv[1]?.endsWith('host-job-streak.mjs')) {
  main().catch((reason) => {
    stdout.write(`could not count the extension-host streak: ${reason.message}\n`);
    exit(1);
  });
}
