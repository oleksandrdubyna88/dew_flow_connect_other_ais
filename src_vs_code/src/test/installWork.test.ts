import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { InstallWork, type InstallPorts } from '../installWork';
import type { Notice } from '../notice';
import type { SpawnServer } from '../setupClients';

/**
 * The install module's work, in the order the story fixes (v2 E4, s5-install-module): the binary check, `--setup-clients` once with the extension's own binary named,
 * and the notices — ONE for every client that failed, none for anything else. Run over fake ports: the spawn answers what a chosen binary would, the funnel records.
 */

const BINARY = '/storage/coai-mcp';
const report = (...clients: readonly Record<string, unknown>[]): string => JSON.stringify({ mode: 'setup-clients', clients });

interface World {
  readonly work: InstallWork;
  readonly said: Notice[];
  readonly logged: string[];
  /** Everything that happened, in order: `exists`, `spawn <args>`, `offer`. */
  readonly events: string[];
}

function world(options: { readonly code?: number; readonly output?: string; readonly binary?: string | undefined; readonly exists?: boolean; readonly offer?: (installed: () => void) => Promise<void>; readonly spawns?: SpawnServer } = {}): World {
  const said: Notice[] = [];
  const logged: string[] = [];
  const events: string[] = [];
  const ports: InstallPorts = {
    binary: () => ('binary' in options ? options.binary : BINARY),
    exists: () => { events.push('exists'); return Promise.resolve(options.exists ?? true); },
    offerUpdate: async (installed) => { events.push('offer'); await options.offer?.(installed); },
    spawn: () => options.spawns ?? ((args) => { events.push(`spawn ${args.join(' ')}`); return Promise.resolve({ code: options.code ?? 0, output: options.output ?? report({ cli: 'claude', result: 'unchanged' }) }); }),
    say: (notice) => { said.push(notice); return Promise.resolve(); },
    log: (message) => { logged.push(message); },
  };

  return { work: new InstallWork(ports), said, logged, events };
}

test('start checks the binary, runs --setup-clients with the binary named, then the update offer; nobody is told when every client is fine', async () => {
  const w = world();

  await w.work.start();

  assert.deepEqual(w.events, ['exists', `spawn --setup-clients --binary ${BINARY}`, 'offer']);
  assert.deepEqual(w.said, []);
  assert.deepEqual(w.logged, []);
});

test('start is once per activation: asked three times it runs the mode once', async () => {
  const w = world();

  await Promise.all([w.work.start(), w.work.start()]);
  await w.work.start();

  assert.equal(w.events.filter((one) => one.startsWith('spawn')).length, 1);
  assert.equal(w.events.filter((one) => one === 'offer').length, 1);
});

test('a refused client gives exactly one notification, with no button', async () => {
  const w = world({ code: 78, output: report({ cli: 'claude', result: 'changed' }, { cli: 'codex', result: 'refused', reason: 'a wrapper owns the coai entry' }) });

  await w.work.start();

  assert.equal(w.said.length, 1);
  assert.equal(w.said[0]?.code, 'clients-not-set-up');
  assert.match(w.said[0]?.title ?? '', /codex \(refused: a wrapper owns the coai entry\)/u);
  assert.equal(w.said[0]?.action, undefined);
});

test('exit 64, an older binary, gives no notification and nothing in the console', async () => {
  const w = world({ code: 64, output: 'unknown argument' });

  await w.work.start();

  assert.deepEqual(w.said, []);
  assert.deepEqual(w.logged, []);
  assert.ok(w.events.includes('offer'), 'the update offer still comes: an older server is exactly the one that needs it');
});

test('no installed binary: the mode is not run and nobody is told; the update offer still runs', async () => {
  for (const options of [{ exists: false }, { binary: undefined }]) {
    const w = world(options);

    await w.work.start();

    assert.equal(w.events.some((one) => one.startsWith('spawn')), false);
    assert.deepEqual(w.said, []);
    assert.ok(w.events.includes('offer'));
  }
});

test('an output the extension cannot read is the console\'s and never a notification', async () => {
  const w = world({ code: 0, output: 'Setting up clients…' });

  await w.work.start();

  assert.deepEqual(w.said, []);
  assert.equal(w.logged.length, 1);
  assert.match(w.logged[0] ?? '', /--setup-clients/u);
});

test('a step that throws is logged and the next step still runs; start never rejects', async () => {
  const w = world({ spawns: () => Promise.reject(new Error('spawn exploded')), offer: () => Promise.reject(new Error('offer exploded')) });

  await w.work.start();

  assert.deepEqual(w.said, []);
  assert.deepEqual(w.logged, ['--setup-clients failed', 'the update offer failed']);
  assert.ok(w.events.includes('offer'), 'the offer ran although the mode threw');
});

test('the update offer does not hold the mode back: the mode finished before the offer was asked', async () => {
  let release!: () => void;
  const waiting = new Promise<void>((resolve) => { release = resolve; });
  const w = world({ offer: () => waiting });

  const done = w.work.start();
  await new Promise((resolve) => { setImmediate(resolve); });

  assert.deepEqual(w.events, ['exists', `spawn --setup-clients --binary ${BINARY}`, 'offer'], 'the person has not answered, and the mode already ran');
  release();
  await done;
});

test('an update the person took runs the mode once more with the new binary, and two runs never overlap', async () => {
  let installed: () => void = () => undefined;
  let running = 0;
  let overlapped = false;
  let runs = 0;
  const spawns: SpawnServer = async () => {
    runs += 1;
    running += 1;
    overlapped ||= running > 1;
    await new Promise((resolve) => { setImmediate(resolve); });
    running -= 1;

    return { code: 0, output: report({ cli: 'claude', result: 'unchanged' }) };
  };
  const w = world({ spawns, offer: (taken) => { installed = taken; return Promise.resolve(); } });

  await w.work.start();
  assert.equal(runs, 1);
  installed();
  await w.work.serverInstalled();

  assert.equal(runs, 3, 'the first run, the install the offer reported, and the install the command reported: each is a run, and they follow one another');
  assert.equal(overlapped, false);
});

const STDIN_REASON = 'claude 2.1.310 cannot read `mcp add-json` from stdin; update Claude Code (claude update)';
const STILL_REFUSED = report({ cli: 'claude', result: 'refused', reason: STDIN_REASON }, { cli: 'codex', result: 'changed' });

test('an old claude that cannot take the entry is ONE notification with the server\'s reason in it, never a modal and never a button', async () => {
  const w = world({ code: 78, output: STILL_REFUSED });

  await w.work.start();

  assert.equal(w.said.length, 1);
  assert.equal(w.said[0]?.code, 'clients-not-set-up');
  assert.equal(w.said[0]?.modal, undefined, 'a toast, not a question');
  assert.equal(w.said[0]?.action, undefined);
  assert.equal(w.said[0]?.actions, undefined);
  assert.ok((w.said[0]?.title ?? '').includes(`claude (refused: ${STDIN_REASON})`), w.said[0]?.title);
});

test('the notification is once per activation: the mode runs again after an install, finds the same refusal, and says nothing a second time', async () => {
  let installed: () => void = () => undefined;
  const w = world({ code: 78, output: STILL_REFUSED, offer: (taken) => { installed = taken; return Promise.resolve(); } });

  await w.work.start();
  installed();
  await w.work.serverInstalled();
  await w.work.serverInstalled();

  assert.equal(w.events.filter((one) => one.startsWith('spawn')).length, 4, 'the mode did run again each time: only the telling is once');
  assert.equal(w.said.length, 1, 'one notification for the whole activation');
});

test('a reload is a new activation and tells once more', async () => {
  const w = world({ code: 78, output: STILL_REFUSED });
  await w.work.start();
  const again = world({ code: 78, output: STILL_REFUSED });

  await again.work.start();

  assert.equal(w.said.length, 1);
  assert.equal(again.said.length, 1, 'a new activation is a new work: the person is told once more, as the notice\'s cure says');
});

/** The server answers one report, then the next, and the last one for every run after (a refusal that stands, then a different one). */
function sequence(...reports: readonly string[]): SpawnServer {
  let at = 0;

  return () => { const output = reports[Math.min(at, reports.length - 1)] ?? ''; at += 1; return Promise.resolve({ code: /"result":"(refused|failed)"/u.test(output) ? 78 : 0, output }); };
}

test('a NEW notification when the set of failed clients or a reason changes within one activation; none for the same failure again', async () => {
  const other = report({ cli: 'claude', result: 'refused', reason: STDIN_REASON }, { cli: 'codex', result: 'failed', reason: '`codex mcp add` exited 1: boom' });
  const reworded = report({ cli: 'claude', result: 'failed', reason: 'a different sentence' }, { cli: 'codex', result: 'changed' });
  const w = world({ spawns: sequence(STILL_REFUSED, STILL_REFUSED, other, other, reworded) });

  await w.work.start();
  await w.work.serverInstalled();
  assert.equal(w.said.length, 1, 'the same failure twice is one notice');
  await w.work.serverInstalled();
  assert.equal(w.said.length, 2, 'another client failed: a new notice');
  await w.work.serverInstalled();
  assert.equal(w.said.length, 2, 'unchanged again: silent');
  await w.work.serverInstalled();
  assert.equal(w.said.length, 3, 'the reason text changed: a new notice');
  assert.match(w.said[2]?.title ?? '', /a different sentence/u);
});

test('a failure that went away and came back is news again', async () => {
  const clean = report({ cli: 'claude', result: 'changed' });
  const w = world({ spawns: sequence(STILL_REFUSED, clean, STILL_REFUSED) });

  await w.work.start();
  await w.work.serverInstalled();
  await w.work.serverInstalled();

  assert.equal(w.said.length, 2);
});
