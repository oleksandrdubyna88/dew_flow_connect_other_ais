import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import {
  AskFailed, JOB_NAME, PROMOTE_AT, asText, asking, streakOf, textFor, worthRetrying,
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
    if (asked < 3) {
      throw new AskFailed('the jobs of run 1 could not be read: 502', 502);
    }

    return 'the answer';
  };

  assert.equal(await asking(flaky, quietly), 'the answer');
  assert.equal(asked, 3, 'it stopped asking before the request would have succeeded');
});

test('every retry says so, so UNKNOWN can be told from never having tried', async () => {
  const said = [];
  let asked = 0;
  const flaky = async () => {
    asked += 1;
    if (asked < 3) {
      throw new AskFailed('the runs of main could not be read: 503 Service Unavailable', 503);
    }

    return 'the answer';
  };

  await asking(flaky, { ...quietly, note: (line) => said.push(line) });

  assert.equal(said.length, 2, 'the log cannot show how hard it tried');
  assert.match(said[0], /attempt 1 of 4/u);
  assert.match(said[0], /503/u, 'the line does not say what went wrong');
});

test('an answered failure is asked exactly once', async () => {
  let asked = 0;
  const gone = async () => {
    asked += 1;
    throw new AskFailed('the runs of main could not be read: 404 Not Found', 404);
  };

  await assert.rejects(() => asking(gone, quietly), /404/u);
  assert.equal(asked, 1, 'a 404 was retried');
});

test('a bug in this script is never retried', async () => {
  // Why the guard asks the FAILURE and not only the attempt count: a TypeError from our own code
  // is not a flaky network, and asking again only delays the stack trace.
  let asked = 0;
  const broken = async () => {
    asked += 1;
    throw new TypeError('runs.map is not a function');
  };

  await assert.rejects(() => asking(broken, quietly), TypeError);
  assert.equal(asked, 1, 'a programming error was retried as if it were a network blip');
});

test('when the attempts run out it throws the LAST failure, not a summary of them', async () => {
  let asked = 0;
  const down = async () => {
    asked += 1;
    throw new AskFailed(`attempt ${asked} got 503`, 503);
  };

  await assert.rejects(() => asking(down, { ...quietly, attempts: 3 }), /attempt 3 got 503/u);
  assert.equal(asked, 3, 'the attempt budget was not honoured');
});

test('GitHub being unreachable says UNKNOWN and no figure', async () => {
  const text = await textFor(() => {
    throw new AskFailed('the jobs of run 35641590649 could not be read: 502', 502);
  });

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
  assert.match(await textFor(() => 7), /7 of 20 consecutive green runs/u,
    'degrading on failure cost the count it reports when it succeeds');
});

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
      const child = spawn(process.execPath, [
        fileURLToPath(new URL('../../scripts/host-job-streak.mjs', import.meta.url)),
      ], {
        // No GITHUB_STEP_SUMMARY: this must not append to a real summary file, and no token,
        // because the fake server does not want one.
        env: { PATH: process.env.PATH, GITHUB_API_URL: `http://127.0.0.1:${port}` },
      });

      let said = '';
      child.stdout.on('data', (chunk) => { said += chunk; });
      child.stderr.on('data', (chunk) => { said += chunk; });
      child.on('error', (reason) => { server.close(); fail(reason); });
      child.on('close', (status) => {
        server.close();
        ready({ status, stdout: said });
      });
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
