import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { execFile } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * Three guarantees about the workflows themselves, each of which had stopped being true.
 *
 * <p>These are readings of YAML, and a reading is the weakest kind of test — so each one pins the
 * WHOLE condition rather than a fragment, because a fragment match survives its own break. The
 * actionlint one is more than a reading: it stands up a server that answers 504 and runs the real
 * argument list against it, which is the only way to know the flags do what the comment says.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const workflows = path.join(repoRoot, '.github', 'workflows');

const read = (name: string): string => fs.readFileSync(path.join(workflows, name), 'utf8');

// --------------------------------------------------------------------------------------------
// The submodule is another repository, and Sonar stops judging it as ours.
// --------------------------------------------------------------------------------------------

test('Sonar does not ANALYSE the conventions submodule, and the exclusion is in the analysis list', () => {
  // Measured 2026-09-21: main's gate was ERROR from 32 new-code findings, 17 of them in this
  // submodule — another repository's code, unfixable from here, keeping the gate permanently red so
  // that a real regression on main would arrive invisible.
  const yaml = read('sonarcloud.yml');
  const analysis = /\/d:sonar\.exclusions="([^"]*)"/u.exec(yaml);

  assert.ok(analysis !== null, 'sonarcloud.yml declares no sonar.exclusions at all');
  assert.ok(analysis[1]!.split(',').includes('.agents/conventions/**'),
    'the submodule must be in the ANALYSIS exclusions; coverage-only would still judge its code');
});

// --------------------------------------------------------------------------------------------
// The actionlint download survives an outage longer than six seconds.
// --------------------------------------------------------------------------------------------

/** The retry arguments the workflow actually passes, read out of it rather than restated here. */
function retryArguments(): readonly string[] {
  const line = read('ci.yml').split('\n').find((one) => one.includes("curl -fsSL --proto '=https'"));
  assert.ok(line !== undefined, 'ci.yml no longer fetches actionlint with curl');

  return line.trim().replace(/\\$/u, '').trim().split(/\s+/u);
}

test('the actionlint fetch backs off exponentially under a ceiling, with no flat delay', () => {
  const args = retryArguments();

  // The whole condition: a flat delay REPLACES curl's backoff, which is how six seconds happened.
  assert.ok(!args.includes('--retry-delay'),
    'a fixed --retry-delay replaces the exponential backoff — that is the defect, measured at 6.08 s');
  assert.equal(args[args.indexOf('--retry') + 1], '5');
  assert.equal(args[args.indexOf('--retry-max-time') + 1], '120');
  assert.ok(args.includes('--retry-all-errors'),
    'curl treats a 504 as a final answer without it, so nothing would be retried at all');
});

test('those arguments really do ride out three 504s and then succeed', async () => {
  // The reading above is a spelling check. This is the behaviour: a local server that fails the way
  // the CDN failed, and the workflow's own argument list run against it.
  let answered = 0;
  const server = createServer((_, response) => {
    answered += 1;
    if (answered <= 3) {
      response.writeHead(504);
      response.end('gateway timeout');

      return;
    }
    response.writeHead(200);
    response.end('the binary');
  });

  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as { port: number }).port;
  try {
    // The RETRY policy only: --proto and --tlsv1.2 are about transport security and would refuse a
    // local http server outright. (They did, and curl then retried the refusal for the full 120 s,
    // which is how this test found its own first mistake.)
    const all = retryArguments();
    const args = all.filter((one, at) => one.startsWith('--retry')
      || (at > 0 && all[at - 1]!.startsWith('--retry') && !one.startsWith('--')));
    // ASYNC on purpose: spawnSync blocks this thread, so the server above - which lives in this
    // very process - could never answer, and curl retried an unanswerable request for the whole 120
    // seconds. The test measured its own harness rather than the flags. (Found by running the same
    // arguments by hand, where they rode out three 504s in 7.1 s.)
    const ran = await new Promise<{ code: number; out: string; err: string }>((resolve) => {
      const child = execFile('curl', ['-fsS', ...args, `http://127.0.0.1:${port}/actionlint.tar.gz`],
        { timeout: 120_000 },
        (_, out, err) => resolve({ code: child.exitCode ?? -1, out, err }));
    });

    assert.equal(ran.code, 0, `curl gave up: ${ran.err}`);
    assert.equal(ran.out, 'the binary');
    assert.equal(answered, 4, 'three refusals and one success is the whole point');
  } finally {
    server.close();
  }
});

// --------------------------------------------------------------------------------------------
// Nothing runs for six hours because it hung.
// --------------------------------------------------------------------------------------------

test('every job in every workflow declares a timeout', () => {
  // GitHub's default is 360 minutes. The suites here take two to four, so a hung test burns six
  // hours of a runner while the pull request looks like it is still working.
  const missing: string[] = [];

  for (const name of fs.readdirSync(workflows).filter((one) => one.endsWith('.yml'))) {
    const lines = read(name).split('\n');
    lines.forEach((line, at) => {
      if (!/^ {4}runs-on: /u.test(line)) {
        return;
      }
      const job = lines.slice(at + 1).findIndex((one) => /^ {2}\S/u.test(one) && one.trim().length > 0);
      const block = lines.slice(at - 8 < 0 ? 0 : at - 8, job < 0 ? lines.length : at + 1 + job);
      if (!block.some((one) => /^ {4}timeout-minutes: /u.test(one))) {
        missing.push(`${name}:${at + 1}`);
      }
    });
  }

  assert.deepEqual(missing, [], 'these jobs would run for GitHub’s default 360 minutes if they hung');
});
