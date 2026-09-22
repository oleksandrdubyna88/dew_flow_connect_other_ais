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
 * is 0 or 40, and it never reports a number it did not measure — because a counter that answers
 * "0" when it actually failed to ask is worse than no counter.</p>
 *
 * <p><b>Failing to ASK is no longer failing the job (2026-09-22).</b> This used to exit non-zero
 * when it could not reach GitHub, and that turned pull request #461's check red over a single
 * `502` while reading one previous run's jobs — every editor test in that job had passed. A
 * reporting step that cannot reach an API has learned nothing about the code under review, so it
 * now says UNKNOWN, loudly, and exits 0. The doctrine above is untouched: it still never prints a
 * figure it did not measure. Transient failures are asked again a few times first, and each retry
 * says so, because "it printed UNKNOWN" and "it printed UNKNOWN after trying four times" are
 * different things to an operator.</p>
 *
 * <p>What still exits non-zero is a fault in THIS script. That distinction is why
 * <c>textFor</c> rethrows anything that is not an <c>AskFailed</c> instead of dressing it up as a
 * network problem. (codex and gemini, on the plan round: a bare catch would have made every
 * <c>TypeError</c> here look like GitHub being down, and exit 0.)</p>
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

/**
 * A request that did not answer usefully, carrying the STATUS rather than only a sentence.
 *
 * <p>The status is a field because the retry rule needs it, and recovering a number by reading it
 * back out of an error message is how a rule stops matching the first time somebody rewords the
 * message. `0` is "there was no answer at all" — DNS, a dropped socket, a timeout.</p>
 */
export class AskFailed extends Error {
  constructor(message, status) {
    super(message);
    this.name = 'AskFailed';
    this.status = status;
  }
}

/**
 * Which failures are worth asking again.
 *
 * <p>The server's own faults and the rate limiter. A `401`, a `403` or a `404` is an ANSWER — the
 * token is wrong, or the repository is not there — and a runner's `GITHUB_TOKEN` is minted per job
 * and does not expire between the calls of one step, so there is no refresh window a retry could
 * cover. Asking four more times reports the same thing four times slower.</p>
 */
export function worthRetrying(status) {
  return status === 0 || status === 429 || status >= 500;
}

/** Whether THIS failure gets another attempt. A bug in our own code never does. */
const askAgain = (reason) => reason instanceof AskFailed && worthRetrying(reason.status);

/** A pause a test replaces, so the retries cost a test no wall-clock time. */
const sleeping = (ms) => new Promise((done) => { setTimeout(done, ms); });

/** Where a retry says it happened. Replaced by a test; stdout is the CI log. */
const saying = (line) => { stdout.write(`${line}\n`); };

/**
 * Ask until it answers, or until the attempts run out.
 *
 * @param {() => Promise<T>} ask the request to make
 * @param {attempts?: number, pause?: (ms: number) => Promise<void>, note?: (line: string) => void} how
 * @returns {Promise<T>} whatever the ask answered
 * @template T
 *
 * <p>Backs off 250ms, 500ms, 1s, and says each time — otherwise a log reading UNKNOWN cannot be
 * told from one that never tried. The last failure is thrown as it arrived rather than wrapped in
 * "out of attempts", so whatever reports it says what actually went wrong.</p>
 */
export async function asking(ask, { attempts = 4, pause = sleeping, note = saying } = {}) {
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await ask();
    } catch (reason) {
      if (attempt >= attempts || !askAgain(reason)) {
        throw reason;
      }
      note(`asking again (attempt ${attempt} of ${attempts}): ${reason.message}`);
      await pause(250 * 2 ** (attempt - 1));
    }
  }
}

/** What to say when GitHub could not be asked: no figure, and what stopped it. */
export function unknownText(reason, promoteAt = PROMOTE_AT) {
  return [
    `extension-host streak on main: UNKNOWN of ${promoteAt} — GitHub could not be asked`,
    `this run measured no number, so it reports none: ${reason}`,
  ].join('\n');
}

/**
 * The line this run prints: the streak, or why there is not one.
 *
 * <p>Separated from the asking so the DEGRADE is a unit a test can drive. Only an
 * <c>AskFailed</c> degrades: anything else is a fault in this script, and dressing one up as
 * "GitHub could not be asked" would hide it behind an exit 0 forever.</p>
 */
export async function textFor(count) {
  try {
    return asText(await count());
  } catch (reason) {
    if (reason instanceof AskFailed) {
      return unknownText(reason.message);
    }

    throw reason;
  }
}

/**
 * Where the API is.
 *
 * <p>`GITHUB_API_URL` is the runner's OWN variable, set on every GitHub-hosted job, so reading it
 * costs production nothing and lets a scenario test point the real script at a server that answers
 * 502. (The plan round: the process exiting 0 is the guarantee, and a unit test of a helper cannot
 * observe an exit code.)</p>
 */
const apiRoot = () => env.GITHUB_API_URL ?? 'https://api.github.com';

/** One page of workflow runs of `main`, newest first. Separated so the counting above stays pure. */
async function runsOfMain(repo, token, perPage) {
  const url = `${apiRoot()}/repos/${repo}/actions/runs`
    + `?branch=main&event=push&status=completed&per_page=${perPage}`;
  const headers = { accept: 'application/vnd.github+json', 'user-agent': 'coai-host-job-streak' };
  if (token) {
    headers.authorization = `Bearer ${token}`;
  }
  const answered = await fetch(url, { headers });
  if (!answered.ok) {
    throw new AskFailed(
      `the runs of main could not be read: ${answered.status} ${answered.statusText}`,
      answered.status,
    );
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
    `${apiRoot()}/repos/${repo}/actions/runs/${runId}/jobs?per_page=100`,
    { headers },
  );
  if (!answered.ok) {
    throw new AskFailed(
      `the jobs of run ${runId} could not be read: ${answered.status}`,
      answered.status,
    );
  }
  const body = await answered.json();

  return (body.jobs ?? []).find((job) => job.name === JOB_NAME);
}

/** Ask, count, say. */
/**
 * The streak, asked for.
 *
 * <p>EVERY request goes through <c>asking</c>, both classes of them: a 502 on the twenty-sixth job
 * lookup is exactly as transient as one on the first, and a call site wired straight to the fetch
 * would be a hole the helper's own tests cannot see. (codex and gemini, on the plan round.)</p>
 */
async function counted() {
  const repo = env.GITHUB_REPOSITORY ?? 'oleksandrdubyna88/dew_flow_connect_other_ais';
  const token = env.GITHUB_TOKEN ?? env.GH_TOKEN ?? '';
  // One more than the threshold is all that can change the answer: past PROMOTE_AT the streak is
  // "ready" whatever came before, so asking for a hundred runs would be a hundred job lookups for a
  // number that cannot move.
  const runs = await asking(() => runsOfMain(repo, token, PROMOTE_AT + 5));
  const attempts = [];
  for (const run of runs) {
    const job = await asking(() => hostJobOf(repo, token, run.id));
    if (job !== undefined) {
      attempts.push({ conclusion: job.conclusion });
    }
  }

  return streakOf(attempts);
}

/** Ask, count, say — and say UNKNOWN rather than fail when GitHub could not be asked. */
async function main() {
  const text = await textFor(counted);
  stdout.write(`${text}\n`);

  const summary = env.GITHUB_STEP_SUMMARY;
  if (summary) {
    const { appendFileSync } = await import('node:fs');
    appendFileSync(summary, `### ${text.split('\n').join('\n\n')}\n`);
  }
}

// Only when RUN, never when imported by a test — the counting above is what a test wants, and
// `sonar.coverage` cannot see anything a test reaches by spawning a process.
if (argv[1]?.endsWith('host-job-streak.mjs')) {
  // Still exits 1, and GitHub being unreachable can no longer reach it — `textFor` answers that
  // and returns text. What is left here is a fault in this script, which SHOULD be loud.
  main().catch((reason) => {
    stdout.write(`the streak counter itself failed: ${reason.message}\n`);
    exit(1);
  });
}
