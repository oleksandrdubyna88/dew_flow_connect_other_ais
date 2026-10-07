import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type CheckRunResult, parseCheckDocument } from '../consultantHealth';
import { ConsultantHealthHost, type HealthHostPorts, confirmationOf, landedSentence } from '../consultantHealthHost';
import { textCopier } from '../copyText';

/**
 * The Check button and the snippet's Copy, on ports — the host half of E5.3.
 *
 * <p>A Check is one real, PAID turn, so the rules worth a test are the ones that keep it from running when nobody meant
 * it to: a refused confirmation runs nothing, a second press while one is asked or running runs nothing, a stray id
 * runs nothing. And the in-flight flag this window keeps is optimistic only — it must clear however the run ends, or a
 * button stays disabled for ever (`durable-status.md` rule 4).</p>
 */

/** What the fake ports record. */
interface Box {
  readonly confirms: { title: string; detail: string; go: string }[];
  readonly runs: string[];
  readonly wrote: string[];
  readonly said: string[];
  readonly posted: string[];
  readonly announced: { kind: string; sentence: string }[];
  settled: number;
  renders: number;
}

interface Kit extends Box {
  readonly host: ConsultantHealthHost;
  /** Ends the oldest run still waiting, with this result — or with a throw. */
  finish(result: CheckRunResult | Error): Promise<void>;
}

function kit(answer: boolean | Promise<boolean> = true, writeText: (text: string) => Promise<void> = () => Promise.resolve()): Kit {
  const waiting: { resolve: (result: CheckRunResult) => void; reject: (error: Error) => void }[] = [];
  const box: Box = { confirms: [], runs: [], wrote: [], said: [], posted: [], announced: [], settled: 0, renders: 0 };
  const ports: HealthHostPorts = {
    confirm: (title, detail, go) => {
      box.confirms.push({ title, detail, go });
      return Promise.resolve(answer);
    },
    runCheck: (kind) => {
      box.runs.push(kind);
      return new Promise((resolve, reject) => { waiting.push({ resolve, reject }); });
    },
    settled: () => { box.settled += 1; },
    announce: (kind, sentence) => { box.announced.push({ kind, sentence }); },
    render: () => { box.renders += 1; },
    nowUtc: () => '2026-10-03T10:05:00.000Z',
    copier: textCopier({ writeText: (text) => { box.wrote.push(text); return writeText(text); }, say: (message) => { box.said.push(message); return { dispose: () => undefined }; } }),
  };
  const host = new ConsultantHealthHost(ports);

  return Object.assign(box, {
    host,
    finish: async (result: CheckRunResult | Error) => {
      const one = waiting.shift();
      if (result instanceof Error) {
        one?.reject(result);
      } else {
        one?.resolve(result);
      }
      await new Promise((resolve) => setImmediate(resolve));
    },
  });
}

const ABOUT = { vendor: 'antigravity', model: 'gemini-3.1-pro-high' };

test('the confirmation names the vendor and model, the scratch folder, and the session it leaves in the vendor\'s store', () => {
  const { title, detail, go } = confirmationOf('claude', ABOUT);
  const said = `${title} ${detail}`;

  assert.match(said, /antigravity · gemini-3\.1-pro-high/u);
  assert.match(said, /paid/u);
  assert.match(said, /coai-check-/u);
  assert.match(said, /never your code/u);
  assert.match(said, /session/u);
  assert.match(go, /paid/u);
});

test('a refused confirmation runs nothing and marks nothing as checking', async () => {
  const one = kit(false);
  await one.host.check('claude', ABOUT);

  assert.equal(one.confirms.length, 1);
  assert.deepEqual(one.runs, [], 'a paid turn ran that the person declined');
  assert.deepEqual(one.host.checking(), []);
});

test('a confirmed check is marked checking, painted, and run once — and the press returns before the turn ends', async () => {
  const one = kit();
  await one.host.check('claude', ABOUT);

  assert.deepEqual(one.runs, ['claude']);
  assert.deepEqual(one.host.checking(), ['claude']);
  assert.ok(one.renders >= 1, 'the button did not say Checking until the paid turn was over');
});

test('a second press while the first is still being confirmed asks nothing and runs nothing', async () => {
  let open: (value: boolean) => void = () => undefined;
  const one = kit(new Promise((resolve) => { open = resolve; }));
  const first = one.host.check('claude', ABOUT);
  await one.host.check('claude', ABOUT);
  open(true);
  await first;

  assert.equal(one.confirms.length, 1);
  assert.deepEqual(one.runs, ['claude']);
});

test('a second press while checking runs once', async () => {
  const one = kit();
  await one.host.check('claude', ABOUT);
  await one.host.check('claude', ABOUT);

  assert.deepEqual(one.runs, ['claude'], 'two presses were two paid turns');
});

test('a run that THROWS clears the flag, is recorded as a crash, and asks the server again', async () => {
  const one = kit();
  await one.host.check('claude', ABOUT);
  await one.finish(new Error('spawn EPERM'));

  assert.deepEqual(one.host.checking(), [], 'the button stays disabled for ever after a crash');
  assert.equal(one.host.runs()['claude']?.result.kind, 'crashed');
  assert.equal(one.settled, 1);
});

test('exit 64 lands as too-old, which the row words as "update the MCP server"', async () => {
  const one = kit();
  await one.host.check('codex', ABOUT);
  await one.finish({ kind: 'too-old' });

  assert.deepEqual(one.host.runs()['codex'], { result: { kind: 'too-old' }, landedUtc: '2026-10-03T10:05:00.000Z' });
  assert.deepEqual(one.host.checking(), []);
});

test('a press naming no caller kind this build knows runs nothing and asks nothing', async () => {
  const one = kit();
  await one.host.check('../../x', ABOUT);

  assert.deepEqual(one.confirms, []);
  assert.deepEqual(one.runs, []);
});

test('copying a snippet nobody was shown touches no clipboard, and tells the page nothing', async () => {
  const one = kit();
  const report = await one.host.copySnippet('claude', undefined, (kind) => one.posted.push(kind));

  assert.equal(report.copied, false);
  assert.deepEqual(one.wrote, [], 'a copy with nothing to copy put something on the clipboard');
  assert.deepEqual(one.posted, []);
});

test('copying the snippet writes it, says where it goes and that it is not read-only, and tells the page', async () => {
  const one = kit();
  const report = await one.host.copySnippet('claude', { text: 'command(git grep)', settingsPath: '/home/u/.gemini/antigravity-cli/settings.json' }, (kind) => one.posted.push(kind));

  assert.equal(report.copied, true);
  assert.deepEqual(one.wrote, ['command(git grep)']);
  assert.match(report.said, /settings\.json/u);
  assert.match(report.said, /not read-only/u);
  assert.deepEqual(one.posted, ['claude']);
});

test('a clipboard that refuses is said in the snippet\'s own words, and the page is not told it was copied', async () => {
  const one = kit(true, () => Promise.reject(new Error('held')));
  const report = await one.host.copySnippet('claude', { text: 'command(git grep)', settingsPath: '/x' }, (kind) => one.posted.push(kind));

  assert.equal(report.copied, false);
  assert.match(report.said, /allow rule could not be copied/u);
  assert.deepEqual(one.posted, []);
});

// ---------- the whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), P ----------

/** A finished check as the server prints it. */
function reported(over: Record<string, unknown>): CheckRunResult {
  const record = parseCheckDocument(JSON.stringify({ callerKind: 'claude', state: 'answered', ...over }));
  assert.ok(record !== undefined, 'the fixture record does not parse');

  return { kind: 'reported', record };
}

test('a check that lands is announced once, through the funnel, in a sentence a person hears without the page', async () => {
  const one = kit();
  await one.host.check('claude', ABOUT);
  await one.finish(reported({ state: 'answered', markerRead: true, canary: 'denied-by-cli' }));

  assert.equal(one.announced.length, 1, 'the page cannot announce a landed check — the lines are rebuilt with every repaint');
  assert.equal(one.announced[0]?.kind, 'claude');
  assert.match(one.announced[0]?.sentence ?? '', /answered and read the marker/u);
  assert.match(one.announced[0]?.sentence ?? '', /observed confinement/u);
});

test('a refused confirmation runs nothing and announces nothing', async () => {
  const one = kit(false);
  await one.host.check('claude', ABOUT);

  assert.deepEqual(one.announced, []);
});

test('every way a press can land has its own sentence', () => {
  assert.match(landedSentence('codex', reported({ state: 'failed', failureKind: 'quota' })), /failed — Quota spent/u);
  assert.match(landedSentence('codex', reported({ state: 'already-checking', reason: 'another check holds the lock' })), /already-checking — another check holds the lock/u);
  assert.match(landedSentence('codex', { kind: 'too-old' }), /update the MCP server/u);
  // A row that asked to stream hears whether it did (research/PLAN_api_streaming.md, Story C); one that did not, nothing.
  assert.match(landedSentence('qwen', reported({ markerRead: true, streamed: 'streamed' })), /the answer was streamed/u);
  assert.match(landedSentence('qwen', reported({ markerRead: true, streamed: 'not-streamed' })), /NOT streamed — the endpoint or this coai-mcp ignored the switch/u);
  assert.doesNotMatch(landedSentence('qwen', reported({ markerRead: true })), /stream/u);
  assert.match(landedSentence('codex', { kind: 'refused', said: 'no such caller' }), /refused by the MCP server: no such caller/u);
  assert.match(landedSentence('codex', { kind: 'crashed', why: 'killed at its cap' }), /did not finish: killed at its cap/u);
});
