import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { textCopier } from '../copyText';
import type { CopyPorts } from '../copyText';
import type { AccountsRead } from '../remoteAccountsReply';
import type { HostMemory } from '../remoteAccountsRound';
import { accountsRows, choose, copyDecisionFor } from '../remoteAccountsRows';
import type { AccountsView, ChooseSeams, Row } from '../remoteAccountsRows';
import type { Committed, HostStanding } from '../remoteHosts';
import { bodyOf, sourceFiles, sourceOf } from './sourceReading';

/**
 * What choosing a row does, the copy button, and the scans of the accounts modules (E1.S4, A4-T9's button and A4-T20), over fake seams.
 * The scans of the host module that has no other way in are at the bottom, each with its companion. Every host here is made up.
 */

const NOW = Date.UTC(2026, 10, 24, 12, 0, 0);
const at = (hours: number): string => new Date(NOW + (hours * 3_600_000)).toISOString();

type Reading = Extract<AccountsRead, { kind: 'read' }>;

const view = (over: Partial<AccountsView> = {}): AccountsView =>
  ({ live: true, stopped: false, busy: false, memory: new Map(), standings: [], hosts: [], ignored: undefined, allowNew: true, ...over });

const reading = (over: Partial<Reading> = {}): Reading => ({ kind: 'read', claude: { state: 'absent' }, codex: { state: 'absent' }, agy: { state: 'absent' }, jqMissing: undefined, ...over });

const hostRowOf = (rows: readonly Row[], host: string): Row => {
  const found = rows.find((row) => row.kind === 'host' && row.host === host);
  assert.ok(found !== undefined, `no row for ${host}`);

  return found;
};

interface Calls {
  readonly seams: ChooseSeams;
  readonly log: string[];
}

function callsOver(saved: Committed = { kind: 'written', row: undefined, evicted: [] }, started = true): Calls {
  const log: string[] = [];
  const seams: ChooseSeams = {
    change: (host) => { log.push(`change ${host}`); return Promise.resolve(); },
    ask: (host) => { log.push(`ask ${host}`); return Promise.resolve(); },
    save: () => { log.push('save'); return Promise.resolve(saved); },
    start: () => { log.push('start'); return Promise.resolve(started); },
    refresh: () => { log.push('refresh'); return Promise.resolve('start'); },
  };

  return { seams, log };
}

// ───────────────── A4-T20: a row that shows W12 saves on a press ─────────────────

const waiting = (): Row => hostRowOf(accountsRows(view({ hosts: ['hW'], memory: new Map<string, HostMemory>([['hW', { admission: 'unwritten' }]]) }), NOW), 'hW');

test('A4-T20: choosing a W12 row calls saveWaiting once, and neither the change question nor the shared question', async () => {
  const calls = callsOver();
  await choose(waiting(), calls.seams);

  assert.deepEqual(calls.log, ['save', 'refresh'], 'a refusal that was not saved could not be saved from the list');
});

test('A4-T20: only a saved answer asks for a round; any other answer leaves the row as it is', async () => {
  for (const answer of [{ kind: 'kept', row: undefined }, { kind: 'full' }, { kind: 'damaged' }, { kind: 'unwritten', why: 'busy' }] as const) {
    const calls = callsOver(answer);
    await choose(waiting(), calls.seams);

    assert.deepEqual(calls.log, ['save'], `a ${answer.kind} answer asked for a round`);
  }
});

test('A4-T8 (press): a host row that is not asked yet opens the shared question, any other host row the change question', async () => {
  const asked = callsOver();
  await choose(hostRowOf(accountsRows(view({ hosts: ['hA'], allowNew: false }), NOW), 'hA'), asked.seams);
  assert.deepEqual(asked.log, ['ask hA']);

  const changed = callsOver();
  const standings: readonly HostStanding[] = [{ host: 'hB', answer: 'yes', how: 'person', askedUtc: at(-9) }];
  await choose(hostRowOf(accountsRows(view({ hosts: ['hB'], standings }), NOW), 'hB'), changed.seams);
  assert.deepEqual(changed.log, ['change hB']);
});

test('A4-T14 (press): the start row starts the round, and a start that was not saved is told to the list; any other row does nothing', async () => {
  const ok = callsOver(undefined, true);
  assert.equal(await choose({ kind: 'info', label: 'x', action: 'start' }, ok.seams), undefined);
  assert.deepEqual(ok.log, ['start']);

  const no = callsOver(undefined, false);
  assert.equal(await choose({ kind: 'info', label: 'x', action: 'start' }, no.seams), 'start-failed');

  const none = callsOver();
  for (const row of [{ kind: 'info', label: 'x' }, { kind: 'separator', label: 'x' }, { kind: 'usage', label: 'x', description: 'y', detail: 'z' }] as const) {
    assert.equal(await choose(row, none.seams), undefined);
  }
  assert.deepEqual(none.log, [], 'a row that is not a host row and not the start row did something');
});

// ───────────────── A4-T9: the copy button ─────────────────

function copierOver(): { readonly written: string[]; readonly said: string[]; readonly ports: CopyPorts } {
  const written: string[] = [];
  const said: string[] = [];
  const ports: CopyPorts = {
    writeText: (text) => { written.push(text); return Promise.resolve(); },
    say: (message) => { said.push(message); return { dispose: () => undefined }; },
  };

  return { written, said, ports };
}

test('A4-T9: the copy button hands the copier exactly the row\'s copy, and a row with none copies nothing', async () => {
  const withJq = hostRowOf(accountsRows(view({ hosts: ['hJ'], memory: new Map<string, HostMemory>([['hJ', { reading: { read: reading({ jqMissing: 'dnf' }), atMs: NOW } }]]) }), NOW), 'hJ');
  const over = copierOver();
  const report = await textCopier(over.ports).copy(() => copyDecisionFor(withJq));

  assert.equal(report.copied, true);
  assert.deepEqual(over.written, ['sudo dnf install -y jq'], 'the button copied something other than the row\'s copy');

  const none = copierOver();
  const refused = await textCopier(none.ports).copy(() => copyDecisionFor({ kind: 'info', label: 'x' }));
  assert.equal(refused.copied, false);
  assert.deepEqual(none.written, [], 'a row with no copy wrote to the clipboard');
});

// ───────────────── scans of the host modules ─────────────────

const accountsFiles = (): readonly string[] => sourceFiles().filter((file) => /^remoteAccounts.*\.ts$/u.test(file));

/** Every `saveWaiting(` call of the accounts modules, by file. */
const saveCalls = (): Readonly<Record<string, number>> =>
  Object.fromEntries(accountsFiles().map((file) => [file, [...sourceOf(file).matchAll(/\bsaveWaiting\(/gu)].length]).filter(([, count]) => (count as number) > 0));

test('A4-T20 (scan): saveWaiting( has one call site in the accounts modules, in the list\'s choice binding, and none in the round or the timer', () => {
  assert.deepEqual(saveCalls(), { 'remoteAccountsHost.ts': 1 }, 'a lock was taken over with no press');
  const host = sourceOf('remoteAccountsHost.ts');
  assert.ok(bodyOf(host, 'function chooseSeamsOf(').includes('saveWaiting('), 'the one call is not in the list\'s choice binding');
  const window = bodyOf(host, 'class AccountsWindow');
  assert.ok(!window.includes('saveWaiting('), 'the round or the timer can take a lock over');
});

test('A4-T20 (scan companions): the scan finds a call in a sample, and the accounts modules are the ones named', () => {
  assert.equal([...'x.saveWaiting();'.matchAll(/\bsaveWaiting\(/gu)].length, 1, 'the pattern misses a call');
  for (const wanted of ['remoteAccountsHost.ts', 'remoteAccountsRows.ts', 'remoteAccountsRound.ts']) {
    assert.ok(accountsFiles().includes(wanted), `the scan does not look at ${wanted}`);
  }
});
