import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CADENCE_TTL_MS, CadenceProbes } from '../cadenceProbe';
import type { SessionFile } from '../rounds';

/**
 * The sidebar's cadence probes — bounded, never awaited, and honest about what failed
 * (todo/PLAN_consult_on_a_cadence.md, the risk consultation for story 4.2, point 3).
 */

const T0 = Date.parse('2026-09-25T12:00:00Z');

const session = (branch = 'feat/x', plan = 'todo/PLAN_x.md'): SessionFile => ({
  state: { sessionId: branch, repoPath: 'D:/repo', branch, stage: 'CodeReview', awaitingResolve: false },
  rounds: [{ stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: '2026-09-25T11:00:00Z' }],
  plan,
});

const body = (closed: number[]): string => JSON.stringify({
  plan: 'todo/PLAN_x.md', mode: 'remind', epics: 6, epicsClosed: closed,
  groups: [{ range: '1-3', consulted: true }, { range: '4-6', consulted: false }], risk: [], riskAnswered: true, unreadable: '',
});

/** A probe set over a scripted server: each run takes the next answer, or waits for `release`. */
function harness(answers: { code: number; output: string }[]) {
  let now = T0;
  const asked: string[][] = [];
  const logged: string[] = [];
  let renders = 0;
  let held: (() => void) | undefined;
  let hold = false;
  const probes = new CadenceProbes({
    executable: () => 'coai-mcp',
    run: async (_exe, args) => {
      asked.push([...args]);
      if (hold) {
        await new Promise<void>((resolve) => { held = resolve; });
      }
      return answers.shift() ?? { code: 74, output: '' };
    },
    now: () => now,
    render: () => { renders += 1; },
    log: (message) => { logged.push(message); },
  });

  return {
    probes,
    asked,
    logged,
    renders: () => renders,
    advance: (ms: number) => { now += ms; },
    holdNext: () => { hold = true; },
    release: () => { hold = false; held?.(); },
  };
}

/** Lets every settled promise run its continuations — the probe is never awaited by the render. */
const settled = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

test('the first render draws nothing and asks once; the answer lands and repaints', async () => {
  const h = harness([{ code: 0, output: body([1]) }]);

  assert.deepEqual(h.probes.lines([session()]), [], 'a render never waits on a spawn');
  await settled();

  assert.deepEqual(h.asked, [['--cadence', '--repo', 'D:/repo', '--branch', 'feat/x', '--plan', 'todo/PLAN_x.md']]);
  assert.equal(h.renders(), 1);
  assert.deepEqual(h.probes.lines([session()]).map((l) => l.answer.epicsClosed), [[1]]);
});

test('within the TTL nothing is asked again; past it, it is', async () => {
  const h = harness([{ code: 0, output: body([1]) }, { code: 0, output: body([1, 2]) }]);
  h.probes.lines([session()]);
  await settled();

  h.advance(CADENCE_TTL_MS - 1);
  h.probes.lines([session()]);
  await settled();
  assert.equal(h.asked.length, 1);

  h.advance(1);
  h.probes.lines([session()]);
  await settled();
  assert.equal(h.asked.length, 2);
  assert.deepEqual(h.probes.lines([session()])[0]?.answer.epicsClosed, [1, 2]);
});

test('an outcome recorded on a LAPSED consultation — which the watcher never reports — is picked up by the TTL', async () => {
  // The watcher compares live consultations only; the clock is what reaches a lapsed one.
  const consulted = body([1]).replace('"range":"4-6","consulted":false', '"range":"4-6","consulted":true');
  const h = harness([{ code: 0, output: body([1]) }, { code: 0, output: consulted }]);
  h.probes.lines([session()]);
  await settled();

  h.advance(CADENCE_TTL_MS);
  h.probes.lines([session()]);
  await settled();

  assert.equal(h.probes.lines([session()])[0]?.answer.groups[1]?.consulted, true);
});

test('forgetting — a consultation changed — asks again at once, and the line does not blink while it does', async () => {
  const h = harness([{ code: 0, output: body([1]) }, { code: 0, output: body([1, 2]) }]);
  h.probes.lines([session()]);
  await settled();

  h.probes.forget();
  assert.equal(h.probes.lines([session()]).length, 1, 'the last answer stays up while the next is asked');
  await settled();

  assert.equal(h.asked.length, 2);
});

test('one probe at a time, whatever the renders do meanwhile', async () => {
  const h = harness([{ code: 0, output: body([1]) }, { code: 0, output: body([1]) }]);
  h.holdNext();
  h.probes.lines([session('a'), session('b')]);
  h.probes.lines([session('a'), session('b')]);
  h.probes.lines([session('a'), session('b')]);
  await settled();

  assert.equal(h.asked.length, 1, 'the second session waits for the first, and no render starts another');
  h.release();
  await settled();
  await settled();

  assert.equal(h.asked.length, 2);
});

test('an unchanged answer does not repaint — the probe must not feed its own loop', async () => {
  const h = harness([{ code: 0, output: body([1]) }, { code: 0, output: body([1]) }]);
  h.probes.lines([session()]);
  await settled();
  h.advance(CADENCE_TTL_MS);
  h.probes.lines([session()]);
  await settled();

  assert.equal(h.asked.length, 2);
  assert.equal(h.renders(), 1);
});

test('a session nobody should probe is never asked about', async () => {
  const h = harness([]);
  const old = { ...session(), rounds: [{ stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: '2026-09-20T00:00:00Z' }] };

  h.probes.lines([old, session('feat/y', '')]);
  await settled();

  assert.deepEqual(h.asked, []);
});

test('a 65 — no session, or a torn read — keeps the last answer and is logged once, never taken for an old server', async () => {
  const h = harness([
    { code: 0, output: body([1]) },
    { code: 65, output: '' },
    { code: 65, output: '' },
    { code: 0, output: body([1, 2]) },
  ]);
  h.probes.lines([session()]);
  await settled();
  for (let i = 0; i < 2; i += 1) {
    h.advance(CADENCE_TTL_MS);
    h.probes.lines([session()]);
    await settled();
    assert.deepEqual(h.probes.lines([session()])[0]?.answer.epicsClosed, [1], 'the last answer is kept');
  }
  assert.equal(h.logged.length, 1, 'once, however often it repeats');

  h.advance(CADENCE_TTL_MS);
  h.probes.lines([session()]);
  await settled();
  assert.deepEqual(h.probes.lines([session()])[0]?.answer.epicsClosed, [1, 2], 'and probing went on');
});

test('a 64 is a server too old for the mode: nothing more is asked and no line is drawn', async () => {
  const h = harness([{ code: 64, output: '' }]);
  h.probes.lines([session()]);
  await settled();
  h.advance(CADENCE_TTL_MS * 10);

  assert.deepEqual(h.probes.lines([session()]), []);
  await settled();
  assert.equal(h.asked.length, 1);
});

test('a failure or a body that is not an answer keeps the last answer up', async () => {
  const h = harness([{ code: 0, output: body([1]) }, { code: 74, output: '' }, { code: 0, output: '{ torn' }]);
  h.probes.lines([session()]);
  await settled();
  for (let i = 0; i < 2; i += 1) {
    h.advance(CADENCE_TTL_MS);
    h.probes.lines([session()]);
    await settled();
  }

  assert.deepEqual(h.probes.lines([session()])[0]?.answer.epicsClosed, [1]);
});

test('`null` — the cadence off, or no plan — draws no line for that session', async () => {
  const h = harness([{ code: 0, output: 'null' }]);
  h.probes.lines([session()]);
  await settled();

  assert.deepEqual(h.probes.lines([session()]), []);
});

test('no server installed asks nothing', async () => {
  const asked: unknown[] = [];
  const probes = new CadenceProbes({
    executable: () => '',
    run: async (...args) => { asked.push(args); return { code: 0, output: '' }; },
    now: () => T0,
    render: () => undefined,
    log: () => undefined,
  });
  probes.lines([session()]);
  await settled();

  assert.deepEqual(asked, []);
});

// The code round (codex): the sidebar painted only after the whole batch, so twenty cold probes in a
// row were a minute of no lines although the first answers were in hand.
test('each answer is painted as it lands, not after the whole batch', async () => {
  let second: (() => void) | undefined;
  let renders = 0;
  let calls = 0;
  const probes = new CadenceProbes({
    executable: () => 'coai-mcp',
    run: async () => {
      calls += 1;
      if (calls === 2) {
        await new Promise<void>((resolve) => { second = resolve; });
      }
      return { code: 0, output: body([calls]) };
    },
    now: () => T0,
    render: () => { renders += 1; },
    log: () => undefined,
  });

  probes.lines([session('a'), session('b')]);
  await settled();

  assert.equal(renders, 1, 'the first answer is on screen while the second is still being asked');
  second?.();
  await settled();
  assert.equal(renders, 2);
});
