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
 * <p><b>A request answers a VALUE, never a thrown failure.</b> `common/coding-style.md`:
 * *"Expected failures are values, not exceptions."* A 502 is an expected failure of a network
 * call, and the first draft of this change made it an `AskFailed` exception with an `instanceof`
 * test downstream — which is how the rule reads when you write it by habit. As a value there is no
 * class, no `instanceof`, and no question about whether some path throws something with a
 * non-numeric status: `requested` is the ONE place a failure is constructed, and it always
 * constructs the same shape. (codex, on the code round.)</p>
 *
 * <p>What still exits non-zero is a fault in THIS script — a `TypeError` in the counting, say.
 * Those are not caught anywhere, so they reach the top-level catch and are loud, which is the
 * distinction the whole file turns on.</p>
 */

import { argv, env, exit, stdout } from 'node:process';

/** The rule, from the comment beside the job it is about. One place to change it if it ever moves. */
export const PROMOTE_AT = 20;

/** The job as its `name:` renders it, which is what the API reports. */
export const JOB_NAME = 'extension · a real editor';

/** The status of a request that got no HTTP answer at all: DNS, a socket, a TLS handshake, a deadline. */
export const NO_ANSWER = 0;

/** How many times one request is made before the answer is given up on. */
export const ATTEMPTS = 4;

/** The first backoff; each later one doubles it — 250ms, 500ms, 1s. */
export const BACKOFF_MS = 250;

/**
 * How long ONE request may take before it is abandoned.
 *
 * <p>Node's `fetch` has no default timeout. A connection GitHub accepts and never answers would
 * otherwise hold this step until the runner's own limit — hours, for a counter nobody is waiting
 * on. Ten seconds is generous for a single Actions API read. (gemini and codex, on the code round.)</p>
 */
export const REQUEST_CEILING_MS = 10_000;

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

/** A request that answered. */
export const answered = (value) => ({ ok: true, value });

/** A request that did not, carrying the STATUS so nothing has to read one back out of prose. */
export const refused = (message, status) => ({ ok: false, message, status });

/**
 * Which failures are worth asking again.
 *
 * <p>The server's own faults, the rate limiter, and no answer at all. A `401`, a `403` or a `404`
 * is an ANSWER — the token is wrong, or the repository is not there — and a runner's `GITHUB_TOKEN`
 * is minted per job and does not expire between the calls of one step, so there is no refresh
 * window a retry could cover. Asking again reports the same thing, slower.</p>
 */
export function worthRetrying(status) {
  return status === NO_ANSWER || status === 429 || status >= 500;
}

/** A pause a test replaces, so the retries cost a test no wall-clock time. */
const sleeping = (ms) => new Promise((done) => { setTimeout(done, ms); });

/** Where a retry says it happened. Replaced by a test; stdout is the CI log. */
const saying = (line) => { stdout.write(`${line}\n`); };

/**
 * Ask until it answers, or until the attempts run out.
 *
 * @param {() => Promise<{ok: boolean, value?: unknown, message?: string, status?: number}>} ask
 * @param {{attempts?: number, pause?: (ms: number) => Promise<void>, note?: (line: string) => void}} how
 * @returns {Promise<{ok: boolean, value?: unknown, message?: string, status?: number}>}
 *
 * <p>Backs off 250ms, 500ms, 1s, and says each time — otherwise a log reading UNKNOWN cannot be
 * told from one that never tried. The LAST refusal is what comes back, as it arrived, so whatever
 * reports it says what actually went wrong rather than "out of attempts".</p>
 */
export async function asking(ask, { attempts = ATTEMPTS, pause = sleeping, note = saying } = {}) {
  let last = refused('nothing was asked', NO_ANSWER);
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    last = await ask();
    if (last.ok || !worthRetrying(last.status)) {
      return last;
    }
    if (attempt < attempts) {
      note(`asking again (attempt ${attempt} of ${attempts}): ${last.message}`);
      await pause(BACKOFF_MS * 2 ** (attempt - 1));
    }
  }

  return last;
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
 * <p>Separated from the asking so the DEGRADE is a unit a test can drive. It branches on a value
 * and catches nothing, so a fault in this script — anything that throws — goes straight past it to
 * the loud exit rather than being dressed up as "GitHub could not be asked".</p>
 */
export async function textFor(count) {
  const got = await count();

  return got.ok ? asText(got.value) : unknownText(got.message);
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

/**
 * The headers for one request — and the token only over HTTPS.
 *
 * <p>Not a defence against somebody who can already set this job's environment: they can read the
 * token directly. It is a defence against a MISTAKE — an enterprise API root typed with `http://`
 * would otherwise put the repository token on the wire in clear. (gemini, on the code round.)</p>
 */
function headersFor(url, token) {
  const headers = { accept: 'application/vnd.github+json', 'user-agent': 'coai-host-job-streak' };
  if (token && url.startsWith('https://')) {
    headers.authorization = `Bearer ${token}`;
  }

  return headers;
}

/**
 * One request, with a deadline, answering a value either way.
 *
 * <p>The ONE place a refusal is built, which is what makes "the status is always a number" a fact
 * about the code rather than a hope. `fetch` rejects with a `TypeError` for DNS, a reset socket or
 * a TLS failure, and with an `AbortError` when the deadline passes; both are "no answer at all",
 * which is exactly the case `worthRetrying` documents and — before this — nothing could produce.
 * Five reviewers across three vendors found that hole on the code round: the retry claimed to
 * cover a network failure and the native rejection sailed straight past it to exit 1.</p>
 */
async function requested(url, token) {
  try {
    const got = await fetch(url, {
      headers: headersFor(url, token),
      signal: AbortSignal.timeout(REQUEST_CEILING_MS),
    });
    if (!got.ok) {
      return refused(`${got.status} ${got.statusText}`.trim(), got.status);
    }

    return answered(await got.json());
  } catch (reason) {
    return refused(`no answer at all — ${reason.message}`, NO_ANSWER);
  }
}

/** One page of workflow runs of `main`, newest first. */
async function runsOfMain(repo, token, perPage) {
  const url = `${apiRoot()}/repos/${repo}/actions/runs`
    + `?branch=main&event=push&status=completed&per_page=${perPage}`;
  const got = await requested(url, token);
  if (!got.ok) {
    return refused(`the runs of main could not be read: ${got.message}`, got.status);
  }

  return answered(got.value.workflow_runs ?? []);
}

/** The one job we care about, out of a run. Absent means the run predates the job. */
async function hostJobOf(repo, token, runId) {
  const got = await requested(
    `${apiRoot()}/repos/${repo}/actions/runs/${runId}/jobs?per_page=100`,
    token,
  );
  if (!got.ok) {
    return refused(`the jobs of run ${runId} could not be read: ${got.message}`, got.status);
  }

  return answered((got.value.jobs ?? []).find((job) => job.name === JOB_NAME));
}

/**
 * The streak, asked for.
 *
 * <p>EVERY request goes through `asking`, both classes of them: a 502 on the twenty-sixth job
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
  if (!runs.ok) {
    return runs;
  }

  const attempts = [];
  for (const run of runs.value) {
    const job = await asking(() => hostJobOf(repo, token, run.id));
    if (!job.ok) {
      return job;
    }
    if (job.value !== undefined) {
      attempts.push({ conclusion: job.value.conclusion });
    }
  }

  return answered(streakOf(attempts));
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
  // Still exits 1, and GitHub being unreachable can no longer reach it — a failed request is a
  // value now, and `textFor` turns it into text. What is left here is a fault in this script.
  main().catch((reason) => {
    stdout.write(`the streak counter itself failed: ${reason.message}\n`);
    exit(1);
  });
}
