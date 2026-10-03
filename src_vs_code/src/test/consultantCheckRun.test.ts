import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type CheckRecord, parseCheckDocument } from '../consultantHealth';
import {
  CHECK_DEADLINE_SLACK_MS,
  CHECK_INITIAL_CAP_MS,
  type CheckRunPorts,
  runConsultantCheck,
  tightenedCap,
} from '../consultantCheckRun';

/**
 * One Check press, as a process — `coai-mcp --check-consultant --caller <kind>` through ports (E5.3).
 *
 * <p>The exit codes are the contract `.agents/PROJECT.md` fixes: 0 and one JSON document for every classified outcome,
 * 64 for a binary that has never heard of the mode, 65 for a request it refused. The kill cap is the plan round's rule:
 * the process STARTS under a static, generous cap — a previous check's state file carries an expired deadline and a
 * first run has none, so nothing read at launch can be trusted — and the cap is TIGHTENED to the check's own
 * `deadlineUtc` plus slack only from a state file whose `startedUtc` is newer than the spawn. Never extended.</p>
 */

const SPAWN = Date.parse('2026-10-03T10:00:00.000Z');

function record(over: Record<string, unknown> = {}): CheckRecord {
  const parsed = parseCheckDocument(JSON.stringify({ callerKind: 'claude', state: 'checking', ...over }));
  assert.ok(parsed !== undefined, 'the fixture record does not parse');

  return parsed;
}

/** Ports whose process answers `answer` when `finish()` is called, and whose clock and poll the test drives. */
function ports(answer: { code: number; output: string }, state: () => Promise<CheckRecord | undefined> = () => Promise.resolve(undefined)) {
  let now = SPAWN;
  let ticks: (() => void)[] = [];
  let finish: () => void = () => undefined;
  const asked: { args: readonly string[]; capMs: number; stop: () => boolean }[] = [];
  let stopped = 0;
  const port: CheckRunPorts = {
    run: (args, capMs, stop) => {
      asked.push({ args, capMs, stop });
      return new Promise((resolve) => { finish = () => resolve(answer); });
    },
    readState: state,
    nowMs: () => now,
    every: (_ms, tick) => {
      ticks = [...ticks, tick];
      return () => { stopped += 1; ticks = []; };
    },
  };

  return {
    port,
    asked,
    finish: () => finish(),
    at: (ms: number) => { now = ms; },
    tick: async () => { for (const one of ticks) { one(); } await new Promise((resolve) => setImmediate(resolve)); },
    stopped: () => stopped,
  };
}

test('the check is run as the one-shot mode, for that caller kind, under the static cap', async () => {
  const kit = ports({ code: 0, output: JSON.stringify(record({ state: 'answered' })) });
  const done = runConsultantCheck('gemini', kit.port);
  kit.finish();
  await done;

  assert.deepEqual(kit.asked[0]?.args, ['--check-consultant', '--caller', 'gemini']);
  assert.equal(kit.asked[0]?.capMs, CHECK_INITIAL_CAP_MS);
});

test('exit 0 with one document is reported — every classified outcome, unavailable included', async () => {
  const kit = ports({ code: 0, output: JSON.stringify(record({ state: 'unavailable', reason: 'not configured' })) });
  const done = runConsultantCheck('claude', kit.port);
  kit.finish();
  const result = await done;

  assert.equal(result.kind, 'reported');
  assert.equal(result.kind === 'reported' ? result.record.state : '', 'unavailable');
});

test('exit 64 is a server too old for the mode', async () => {
  const kit = ports({ code: 64, output: '' });
  const done = runConsultantCheck('claude', kit.port);
  kit.finish();

  assert.deepEqual(await done, { kind: 'too-old' });
});

test('exit 65 is a refusal, with the server\'s words when stdout has any and the exit code when it has none', async () => {
  const said = ports({ code: 65, output: JSON.stringify({ error: '--check-consultant needs --caller' }) });
  const one = runConsultantCheck('claude', said.port);
  said.finish();
  assert.deepEqual(await one, { kind: 'refused', said: '--check-consultant needs --caller' });

  const silent = ports({ code: 65, output: '' });
  const two = runConsultantCheck('claude', silent.port);
  silent.finish();
  const result = await two;
  assert.equal(result.kind, 'refused');
  assert.match(result.kind === 'refused' ? result.said : '', /65/u);
});

test('anything else is a crash that names what happened — a kill at the cap, an exit code, a body that is not a result', async () => {
  const cases: readonly { readonly answer: { code: number; output: string }; readonly pattern: RegExp }[] = [
    { answer: { code: -1, output: '' }, pattern: /stopped/u },
    { answer: { code: 74, output: '' }, pattern: /74/u },
    { answer: { code: 0, output: 'not json' }, pattern: /result/u },
  ];
  for (const { answer, pattern } of cases) {
    const kit = ports(answer);
    const done = runConsultantCheck('claude', kit.port);
    kit.finish();
    const result = await done;
    assert.equal(result.kind, 'crashed', `exit ${answer.code} was not a crash`);
    assert.match(result.kind === 'crashed' ? result.why : '', pattern);
  }
});

test('before any fresher state file the cap is the static one — the stop never fires early', async () => {
  const kit = ports({ code: 0, output: '{}' });
  const done = runConsultantCheck('claude', kit.port);
  kit.at(SPAWN + CHECK_INITIAL_CAP_MS - 1);

  assert.equal(kit.asked[0]?.stop(), false, 'a cap derived from nothing killed a check that was still inside its static allowance');
  kit.finish();
  await done;
});

test('a state file from BEFORE the spawn never tightens the cap — its deadline is a previous check\'s', async () => {
  const old = record({ startedUtc: '2026-10-03T09:00:00.0000000Z', deadlineUtc: '2026-10-03T09:07:00.0000000Z' });
  const kit = ports({ code: 0, output: '{}' }, () => Promise.resolve(old));
  const done = runConsultantCheck('claude', kit.port);
  await kit.tick();
  kit.at(SPAWN + 60_000);

  assert.equal(kit.asked[0]?.stop(), false, 'a previous check\'s expired deadline killed this one at once');
  kit.finish();
  await done;
});

test('a state file the check wrote after the spawn tightens the cap to its deadline plus slack', async () => {
  const mine = record({ startedUtc: '2026-10-03T10:00:01.0000000Z', deadlineUtc: '2026-10-03T10:07:00.0000000Z' });
  const kit = ports({ code: 0, output: '{}' }, () => Promise.resolve(mine));
  const done = runConsultantCheck('claude', kit.port);
  await kit.tick();
  const deadline = Date.parse('2026-10-03T10:07:00.000Z');

  kit.at(deadline + CHECK_DEADLINE_SLACK_MS - 1);
  assert.equal(kit.asked[0]?.stop(), false);
  kit.at(deadline + CHECK_DEADLINE_SLACK_MS + 1);
  assert.equal(kit.asked[0]?.stop(), true, 'a check past its own promised end was left running');
  kit.finish();
  await done;
});

test('the cap is tightened only — a fresher file promising a later end never extends the static allowance', () => {
  const capAt = SPAWN + CHECK_INITIAL_CAP_MS;
  const late = record({ startedUtc: '2026-10-03T10:00:01.0000000Z', deadlineUtc: '2026-10-04T10:00:00.0000000Z' });

  assert.equal(tightenedCap(capAt, SPAWN, late), capAt);
  assert.equal(tightenedCap(capAt, SPAWN, undefined), capAt);
  assert.equal(tightenedCap(capAt, SPAWN, record({ startedUtc: '2026-10-03T10:00:01.0000000Z', deadlineUtc: 'soon' })), capAt,
    'a deadline nobody can parse tightened the cap to NaN');
});

test('an early exit ends the run at once, and the state poll stops with it', async () => {
  const kit = ports({ code: 64, output: '' });
  const done = runConsultantCheck('claude', kit.port);
  kit.finish();
  await done;

  assert.equal(kit.stopped(), 1, 'the poll of the state file outlived the process it was watching');
});

test('a state read that throws is a tick that tightens nothing — never the reason the run fails', async () => {
  const kit = ports({ code: 0, output: JSON.stringify(record({ state: 'answered' })) }, () => Promise.reject(new Error('EBUSY')));
  const done = runConsultantCheck('claude', kit.port);
  await kit.tick();
  kit.finish();

  assert.equal((await done).kind, 'reported');
});
