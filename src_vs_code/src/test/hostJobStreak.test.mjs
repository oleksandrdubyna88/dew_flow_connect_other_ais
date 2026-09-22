import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import {
  ATTEMPTS, JOB_NAME, NO_ANSWER, PROMOTE_AT, REQUEST_CEILING_MS,
  answered, asText, asking, refused, streakOf, textFor, worthRetrying,
} from '../../scripts/host-job-streak.mjs';

/**
 * The counter behind the extension-host job's promotion rule.
 *
 * <p><b>What it is for.</b> `ci.yml` says the job becomes a required check once it has been green on
 * twenty consecutive runs of `main` — <i>"a number, so the promotion is a measurement rather than a
 * mood"</i>. Nobody counted, and the promoted tail plan had to record the promotion as pending with
 * no figure, because none had been measured. This is the machine doing the counting.</p>
 *
 * <p>The counting half is pure and is imported rather than spawned, because a script exercised only
 * by spawning reports zero coverage and would fail the new-code gate. The RETRY half follows the
 * same rule: what is asserted is the decision — which statuses get asked again, how many times,
 * and what is reported when the asking fails — with the request and the pause injected, so those
 * cases touch no network and cost no wall-clock time.</p>
 *
 * <p><b>And two of them DO spawn the script.</b> The defect this file grew for was the process
 * exiting 1 on a 502, and an exit code is not something a unit test of a helper can observe: a
 * wiring mistake or an unawaited promise would leave every case above green and the job still red.
 * Those two run the real script against a local server that answers 502 — once for each class of
 * request it makes — and assert the exit code. They are additional to the unit cases, not instead
 * of them, so coverage still comes from the imports. (codex and gemini, on the plan round.)</p>
 */

/** A run whose host job concluded the given way. */
const ran = (conclusion) => ({ conclusion });

test('a streak is counted from the newest run backwards', () => {
  const streak = streakOf([ran('success'), ran('success'), ran('success'), ran('failure')]);

  assert.equal(streak, 3, 'the streak did not stop at the first run that was not green');
});

test('a failure at the newest run means a streak of nothing, however good the history', () => {
  // The case the counter exists to get right. Twenty green runs behind one red one is not a
  // promotion: making it required would break the very next merge.
  const history = [ran('failure'), ...Array.from({ length: 30 }, () => ran('success'))];

  assert.equal(streakOf(history), 0, 'a red newest run was counted as part of a streak');
});

test('a run still in flight ENDS the streak rather than being skipped over', () => {
  // Skipping it would let this say twenty while the newest run is failing at that moment. Ending on
  // it is the conservative reading, and the streak resumes by itself once the run concludes.
  const streak = streakOf([ran(null), ran('success'), ran('success')]);

  assert.equal(streak, 0, 'an unfinished run was treated as green');
});

test('a cancelled run is not a green one', () => {
  assert.equal(streakOf([ran('success'), ran('cancelled'), ran('success')]), 1,
    'a cancelled run was counted towards the streak');
});

test('no runs at all is a streak of zero, not a promotion', () => {
  // An empty list must never read as "nothing has failed, so promote it" - the same shape as a glob
  // that matches nothing passing a test suite.
  assert.equal(streakOf([]), 0, 'an empty history was read as a streak');
  assert.match(asText(streakOf([])), /not yet/u, 'an empty history said the job was ready to promote');
});

test('the threshold is what decides the verdict, and the verdict names the job', () => {
  const short = asText(PROMOTE_AT - 1);
  const met = asText(PROMOTE_AT);

  assert.match(short, /not yet — 1 more green run/u, 'one run short did not say how many were left');
  assert.match(met, /READY TO PROMOTE/u, 'the threshold was reached and the text did not say so');
  assert.ok(met.includes(JOB_NAME),
    'the promotion line does not name the check an operator has to go and tick');
});

test('past the threshold it stays ready rather than starting over', () => {
  assert.match(asText(PROMOTE_AT + 11), /READY TO PROMOTE/u, 'a longer streak stopped being ready');
});

/** Retries never wait in a test, and never write to the log: both are injected. */
const quietly = { pause: async () => {}, note: () => {} };

test('the server\'s own faults and the rate limiter are worth asking again', () => {
  for (const status of [0, 429, 500, 502, 503, 504]) {
    assert.ok(worthRetrying(status), `${status} should have been asked again`);
  }
});

test('an answered failure is not worth asking again, however many times you ask', () => {
  // A 401 or a 404 IS an answer: the token is wrong, or the repository is not there. A runner's
  // token is minted per job and does not expire between the calls of one step, so there is no
  // refresh window a retry could cover - four attempts report the same thing four times slower.
  for (const status of [400, 401, 403, 404, 422]) {
    assert.ok(!worthRetrying(status), `${status} would have been retried pointlessly`);
  }
});

test('a transient failure is asked again, and the answer is taken', async () => {
  let asked = 0;
  const flaky = async () => {
    asked += 1;

    return asked < 3
      ? refused('the jobs of run 1 could not be read: 502 Bad Gateway', 502)
      : answered('the answer');
  };

  assert.deepEqual(await asking(flaky, quietly), { ok: true, value: 'the answer' });
  assert.equal(asked, 3, 'it stopped asking before the request would have succeeded');
});

test('every retry says so, so UNKNOWN can be told from never having tried', async () => {
  const said = [];
  let asked = 0;
  const flaky = async () => {
    asked += 1;

    return asked < 3
      ? refused('the runs of main could not be read: 503 Service Unavailable', 503)
      : answered('the answer');
  };

  await asking(flaky, { ...quietly, note: (line) => said.push(line) });

  assert.equal(said.length, 2, 'the log cannot show how hard it tried');
  assert.match(said[0], /attempt 1 of 4/u);
  assert.match(said[0], /503/u, 'the line does not say what went wrong');
});

test('an answered failure is asked exactly once, and comes back as the refusal', async () => {
  let asked = 0;
  const gone = async () => {
    asked += 1;

    return refused('the runs of main could not be read: 404 Not Found', 404);
  };

  const got = await asking(gone, quietly);

  assert.equal(got.ok, false);
  assert.match(got.message, /404/u);
  assert.equal(asked, 1, 'a 404 was retried');
});

test('a bug in this script is never retried, and is never mistaken for a refusal', async () => {
  // An expected failure is a VALUE here, so anything that THROWS is by construction unexpected:
  // it passes straight through the retry and reaches the loud exit. That separation is the whole
  // reason the failures are values. (codex, on the code round: `common/coding-style.md`.)
  let asked = 0;
  const broken = async () => {
    asked += 1;
    throw new TypeError('attempts.push is not a function');
  };

  await assert.rejects(() => asking(broken, quietly), TypeError);
  assert.equal(asked, 1, 'a programming error was retried as if it were a network blip');
});

test('when the attempts run out it answers the LAST refusal, not a summary of them', async () => {
  let asked = 0;
  const down = async () => {
    asked += 1;

    return refused(`attempt ${asked} got 503`, 503);
  };

  const got = await asking(down, { ...quietly, attempts: 3 });

  assert.equal(got.message, 'attempt 3 got 503', 'the reported reason is not the one that happened');
  assert.equal(asked, 3, 'the attempt budget was not honoured');
});

test('GitHub being unreachable says UNKNOWN and no figure', async () => {
  const text = await textFor(
    () => refused('the jobs of run 35641590649 could not be read: 502 Bad Gateway', 502),
  );

  assert.match(text, /UNKNOWN/u, 'it did not say that it could not find out');
  assert.match(text, /502/u, 'the reason a person needs in order to act is not in the line');
  assert.doesNotMatch(text, /consecutive green runs/u,
    'a figure was reported that this run did not measure, which it must never do');
});

test('a fault in the script itself is NOT dressed up as GitHub being down', async () => {
  // The plan round's finding, and it was right: a bare catch here would have turned every
  // TypeError into "GitHub could not be asked" and exited 0, hiding a broken counter for good.
  await assert.rejects(
    () => textFor(() => { throw new TypeError('attempts.push is not a function'); }),
    TypeError,
    'a script bug degraded to UNKNOWN instead of reaching the loud exit',
  );
});

test('a streak that WAS measured is still reported as a number', async () => {
  assert.match(await textFor(() => answered(7)), /7 of 20 consecutive green runs/u,
    'degrading on failure cost the count it reports when it succeeds');
});

test('no answer at all is worth asking again, which is the case that had no producer', () => {
  // The code round's finding, and five reviewers across three vendors found it: `worthRetrying`
  // documented status 0 and NOTHING could ever construct it. A DNS failure or a reset socket makes
  // `fetch` reject with a native TypeError, which the first draft neither retried nor degraded -
  // it exited 1, the exact failure this whole change exists to remove.
  assert.equal(NO_ANSWER, 0);
  assert.ok(worthRetrying(NO_ANSWER), 'a request that got no answer at all was not asked again');
});

test('one request carries a deadline, because node fetch has none', () => {
  // Structural, and deliberately the WHOLE call rather than a fragment of it: a connection GitHub
  // accepts and never answers would otherwise hold the step until the runner's own limit.
  // Asserting it by waiting would cost this suite four ten-second attempts, so the WIRING is what
  // is pinned. (gemini and codex, on the code round.)
  const source = readFileSync(
    fileURLToPath(new URL('../../scripts/host-job-streak.mjs', import.meta.url)),
    'utf8',
  );

  assert.ok(source.includes('signal: AbortSignal.timeout(REQUEST_CEILING_MS),'),
    'the fetch has no deadline, so a stalled connection hangs the job');
  assert.ok(Number.isFinite(REQUEST_CEILING_MS) && REQUEST_CEILING_MS > 0);
  assert.equal(ATTEMPTS, 4, 'the attempt budget the retry messages promise');
});

/** The script itself, run against `apiUrl`, with nothing of the ambient environment. */
function spawned(apiUrl) {
  const child = spawn(process.execPath, [
    fileURLToPath(new URL('../../scripts/host-job-streak.mjs', import.meta.url)),
  ], {
    // No GITHUB_STEP_SUMMARY: this must not append to a real summary file, and no token, because
    // the fake server does not want one and a real token must never reach a local endpoint.
    env: { PATH: process.env.PATH, GITHUB_API_URL: apiUrl },
  });

  return new Promise((ready, fail) => {
    let said = '';
    child.stdout.on('data', (chunk) => { said += chunk; });
    child.stderr.on('data', (chunk) => { said += chunk; });
    child.on('error', fail);
    child.on('close', (status) => ready({ status, stdout: said }));
  });
}

/**
 * The script, run for real against `answer`, with nothing of the ambient environment.
 *
 * <p><b>`spawn`, never `spawnSync`.</b> The first draft used the sync one and the suite hung for
 * good: `spawnSync` blocks this process's event loop, so the server below could never answer the
 * child's request and both sides waited for each other. The child and the server share one
 * runtime here; only the async form lets the runtime serve while the child runs.</p>
 */
function running(answer) {
  const server = createServer(answer);
  server.listen(0, '127.0.0.1');

  return new Promise((ready, fail) => {
    server.on('listening', () => {
      const { port } = server.address();
      spawned(`http://127.0.0.1:${port}`).then(
        (ran) => { server.close(); ready(ran); },
        (reason) => { server.close(); fail(reason); },
      );
    });
  });
}

const refusing = (status) => (request, answer) => {
  answer.writeHead(status, { 'content-type': 'application/json' });
  answer.end('{"message":"Server Error"}');
};

test('a 502 on the RUNS request leaves the job green and the number unreported', async () => {
  // The defect itself, at the level it actually bit: the process exit code. Every unit case above
  // stayed green while this was exit 1.
  const ran = await running(refusing(502));

  assert.equal(ran.status, 0, `the job was failed by a 502; it said: ${ran.stdout}`);
  assert.match(ran.stdout, /UNKNOWN/u);
  assert.match(ran.stdout, /asking again \(attempt 1 of 4\)/u, 'it gave up without retrying');
  assert.doesNotMatch(ran.stdout, /consecutive green runs/u);
});

test('a connection nothing accepts is retried and degrades, instead of failing the job', async () => {
  // The hole the code round found, at the level it bites. `fetch` rejects with a native TypeError
  // for a refused connection; the first draft neither retried nor degraded it and exited 1 - the
  // very failure this change removes for a 502. A closed port is the cheapest real producer of it.
  const closed = createServer(() => {});
  closed.listen(0, '127.0.0.1');
  const port = await new Promise((ready) => {
    closed.on('listening', () => {
      const chosen = closed.address().port;
      closed.close(() => ready(chosen));
    });
  });

  const ran = await spawned(`http://127.0.0.1:${port}`);

  assert.equal(ran.status, 0, `a refused connection failed the job; it said: ${ran.stdout}`);
  assert.match(ran.stdout, /UNKNOWN/u);
  assert.match(ran.stdout, /asking again \(attempt 1 of 4\)/u,
    'a network rejection was not retried, which is what status 0 is for');
  assert.doesNotMatch(ran.stdout, /consecutive green runs/u);
});

test('a 502 on the JOBS request leaves the job green too', async () => {
  // The second class of request, and the one the real failure came from: run 35641590649's jobs.
  // A call site wired straight to the fetch would pass every test above and fail here.
  const oneRun = (request, answer) => {
    if (request.url.includes('/jobs')) {
      refusing(502)(request, answer);

      return;
    }
    answer.writeHead(200, { 'content-type': 'application/json' });
    answer.end('{"workflow_runs":[{"id":1}]}');
  };

  const ran = await running(oneRun);

  assert.equal(ran.status, 0, `the job was failed by a 502 on the jobs request: ${ran.stdout}`);
  assert.match(ran.stdout, /UNKNOWN/u);
  assert.match(ran.stdout, /asking again \(attempt 1 of 4\)/u,
    'the jobs call site is wired straight to the fetch and never retried');
  assert.doesNotMatch(ran.stdout, /consecutive green runs/u);
});
