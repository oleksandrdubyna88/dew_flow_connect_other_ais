import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CONSULTANTS_CAP_MS, CONSULTANTS_TTL_MS, ConsultantsProbe, type ConsultantsProbeHost } from '../consultantsProbe';

/**
 * `coai-mcp --consultants`, asked by a render and NEVER awaited by one (E5.4).
 *
 * <p>The panel's renders run side by side, and must stay that way: a render coalescer once turned one stalled fetch into
 * a frozen panel (`renderTracker.ts`, 2026-10-02). So this probe answers from what it already knows, starts a spawn
 * for whatever is stale, and asks for a repaint when an answer CHANGED — the `providerHealth` / `CadenceProbes` shape.
 * The spawn, the clock and the repaint are handed in, so each of those rules is a test.</p>
 */

const REPORT = JSON.stringify({ utc: '2026-10-03T10:00:00Z', side: 'windows', consultants: [] });

/** What the fake host records, and the knobs a test turns. */
interface Box {
  readonly runs: { executable: string; args: readonly string[] }[];
  readonly logs: string[];
  renders: number;
  executable: string;
  now: number;
}

interface Kit {
  readonly probe: ConsultantsProbe;
  readonly box: Box;
  /** Settles the oldest spawn still waiting, with this answer. */
  answer(code: number, output?: string): Promise<void>;
  reject(): Promise<void>;
}

function kit(): Kit {
  const waiting: { resolve: (value: { code: number; output: string }) => void; reject: (error: Error) => void }[] = [];
  const box: Box = { runs: [], logs: [], renders: 0, executable: 'C:/coai/coai-mcp.exe', now: 1_000_000 };
  const host: ConsultantsProbeHost = {
    executable: () => box.executable,
    run: (executable, args) => {
      box.runs.push({ executable, args });
      return new Promise((resolve, reject) => { waiting.push({ resolve, reject }); });
    },
    now: () => box.now,
    render: () => { box.renders += 1; },
    log: (message) => { box.logs.push(message); },
  };
  const settled = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

  return {
    probe: new ConsultantsProbe(host),
    box,
    answer: async (code, output = '') => {
      waiting.shift()?.resolve({ code, output });
      await settled();
    },
    reject: async () => {
      waiting.shift()?.reject(new Error('spawn EACCES'));
      await settled();
    },
  };
}

test('with no server installed nothing is spawned, and the tab says so', () => {
  const one = kit();
  one.box.executable = '';

  assert.deepEqual(one.probe.shown(), { kind: 'not-installed' });
  assert.equal(one.box.runs.length, 0);
});

test('the first render is told the server is being asked, and starts exactly one spawn of the mode', () => {
  const one = kit();

  assert.deepEqual(one.probe.shown(), { kind: 'asking' });
  one.probe.shown();
  assert.deepEqual(one.box.runs, [{ executable: 'C:/coai/coai-mcp.exe', args: ['--consultants'] }], 'a second render while one spawn is in flight started another');
});

test('the answer lands, the panel is asked to repaint, and the next render shows it without waiting', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);

  assert.equal(one.box.renders, 1, 'an answer that changed what is drawn was never painted');
  assert.equal(one.probe.shown().kind, 'answered');
});

test('a fresh answer is reused until it is stale, and asked again after', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  one.probe.shown();
  assert.equal(one.box.runs.length, 1, 'every repaint spawned the server again');

  one.box.now += CONSULTANTS_TTL_MS;
  one.probe.shown();
  assert.equal(one.box.runs.length, 2);
});

test('an unchanged answer asks for no repaint — so a repaint can never start the next probe for ever', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  one.box.now += CONSULTANTS_TTL_MS;
  one.probe.shown();
  await one.answer(0, REPORT);

  assert.equal(one.box.renders, 1);
});

test('exit 64 is a server too old for the mode', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(64);

  assert.deepEqual(one.probe.shown(), { kind: 'too-old' });
});

test('a probe that fails keeps the last answer on screen, says why, and logs it once', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  for (let attempt = 0; attempt < 2; attempt += 1) {
    one.box.now += CONSULTANTS_TTL_MS;
    one.probe.shown();
    await one.answer(74);
  }
  const shown = one.probe.shown();

  assert.equal(shown.kind, 'unanswered');
  assert.notEqual(shown.kind === 'unanswered' ? shown.last : undefined, undefined, 'a failed refresh blanked the facts a person was reading');
  assert.equal(one.box.logs.length, 1, 'the same failure was logged on every refresh');
});

test('a spawn that rejects is an unanswered probe, never an unhandled rejection', async () => {
  const one = kit();
  one.probe.shown();
  await one.reject();

  assert.equal(one.probe.shown().kind, 'unanswered');
});

test('forget makes the next render ask again, however fresh the answer was', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  one.probe.forget();
  one.probe.shown();

  assert.equal(one.box.runs.length, 2, 'a check that changed the state was not re-asked of the server');
});

test('an answer from a probe a forget overtook is stored stale — and the panel is asked to repaint so it is asked again', async () => {
  const one = kit();
  one.probe.shown();
  one.probe.forget();
  await one.answer(0, REPORT);

  assert.ok(one.box.renders >= 1, 'the overtaken answer was stored and nothing asked again until unrelated activity');
  one.probe.shown();
  assert.equal(one.box.runs.length, 2, 'an answer older than the forget was taken as fresh');
});

test('a server reinstalled at another path is asked again at once', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  one.box.executable = 'C:/elsewhere/coai-mcp.exe';
  one.probe.shown();

  assert.equal(one.box.runs.at(-1)?.executable, 'C:/elsewhere/coai-mcp.exe');
});

test('the spawn cap leaves room for the server\'s own sixty-second probe ceiling', () => {
  assert.ok(CONSULTANTS_CAP_MS > 60_000, 'the panel would kill a survey the server was still allowed to be running');
});

test('an answer that differs from the last only in its time asks for no repaint — every --consultants is stamped afresh (N1)', async () => {
  const one = kit();
  one.probe.shown();
  await one.answer(0, REPORT);
  const renders = one.box.renders;
  one.probe.forget();
  one.probe.shown();
  await one.answer(0, JSON.stringify({ utc: '2026-10-03T10:05:00Z', side: 'windows', consultants: [] }));

  assert.equal(one.box.renders, renders, 'a repaint for a new stamp alone rebuilt the Settings page under the person on every ask');
  const shown = one.probe.shown();
  assert.equal(shown.kind === 'answered' ? shown.report.utc : '', '2026-10-03T10:05:00Z', 'and the latest answer is still the one kept');
});
