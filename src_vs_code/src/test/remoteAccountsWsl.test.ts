import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { compareRows } from '../pageTables';
import { accountsCommandOf } from '../remoteAccountsMessages';
import { viewOf } from '../remoteAccountsGroups';
import type { Compare, ViewState } from '../remoteAccountsGroups';
import { INSTALL_JQ } from '../remoteAccountsRows';
import { liveOnly, runRound, withRefusals } from '../remoteAccountsRound';
import type { HostMemory, Memory, RoundResult } from '../remoteAccountsRound';
import { WSL_ELSEWHERE_SAID, WSL_HEADING, WSL_LOGIN_SAID, blockOf, tableModel } from '../remoteAccountsTable';
import type { TableModel } from '../remoteAccountsTable';
import { accountsHtml } from '../remoteAccountsPage';
import { memoryKeyOf } from '../remotePlaces';
import type { WslPlace } from '../remotePlaces';
import { LEFT_SAID, NOT_RUNNING_SAID } from '../remoteWslRound';
import type { WslFacts, WslPort } from '../remoteWslRound';
import type { WslRun } from '../remoteWslRun';
import { runAccountsPage } from './accountsPageHarness';
import { NOW, claude, codexOf, inputOf, memoryOf, min, reading, seen } from './accountsTableFixtures';
import { OFF, accounts, world } from './roundFakes';
import type { World } from './roundFakes';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Places (E3.S3; E3S3-T1 to T12): the round over WSL places, its memory by key, the model's `wsl` block and the groupings. The WSL calls are a FAKE
 * port that records what it was asked: no test starts `wsl.exe`.
 */

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });
const u16 = (text: string): string => Buffer.from(text, 'utf16le').toString('utf8');
const answered = (stdout: string): WslRun => ({ kind: 'answered', code: 0, stdout });
const usersText = (...users: readonly string[]): string => `COAI-USERS 1\n${users.map((one) => `COAI-USER ${one}`).join('\n')}\nCOAI-USERS-END\n`;

interface Spec {
  names: readonly string[];
  running: readonly string[];
  users: Readonly<Record<string, readonly string[]>>;
  /** An accounts call's answer for the place key; the default answers. */
  fail?: (key: string) => boolean;
  events?: string[];
}

/** A port over a made-up PC: the list, the running list, the users of each distro and an accounts call that names its place. */
function portOf(spec: Spec): { readonly port: WslPort; readonly record: string[] } {
  const record: string[] = [];
  const events = spec.events ?? [];
  const port: WslPort = {
    list: (running) => {
      record.push(running ? 'running' : 'list');

      return Promise.resolve(answered(u16(`${(running ? spec.running : spec.names).join('\r\n')}\r\n`)));
    },
    users: (distro) => {
      record.push(`users ${distro}`);

      return Promise.resolve(answered(usersText(...(spec.users[distro] ?? ['root 0']))));
    },
    accounts: async (place: WslPlace, mode) => {
      const key = memoryKeyOf(place);
      record.push(`accounts ${key} ${mode}`);
      events.push(`start ${key}`);
      await tick();
      events.push(`end ${key}`);

      return spec.fail?.(key) === true
        ? { kind: 'refused', refusal: `“${place.distro} · ${place.user}” did not answer through WSL within 30 s.`, end: 'timeout' }
        : answered(JSON.stringify({ host: key, nth: 0 }));
    },
  };

  return { port, record };
}

const ROUND_EMAILS: Readonly<Record<string, string>> = {
  'wsl:Ubuntu/root': 'root@example.test', 'wsl:Ubuntu/alice': 'alice@example.test', 'wsl:Ubuntu/bob': 'bob@example.test', 'wsl:Debian/root': 'droot@example.test', a: 'a@example.test', b: 'b@example.test',
};

const UBUNTU: Spec = { names: ['Ubuntu'], running: ['Ubuntu'], users: { Ubuntu: ['root 0', 'alice 1000', 'bob 1001'] } };

async function round(w: World, port: WslPort, memory: Memory = new Map(), index = 0): Promise<RoundResult> {
  return runRound({ ...w.seams, wsl: port }, memory, index, 'timer', OFF);
}

const keysOf = (memory: Memory): readonly string[] => [...memory.keys()].sort();

// ───────────────── T1, T3, T4: the round over places ─────────────────

test('E3S3-T1: two users of one distro are two readings and neither overwrites the other; root\'s is a third', async () => {
  const made = portOf(UBUNTU);
  const w = world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) });
  const result = await round(w, made.port);
  assert.deepEqual(keysOf(result.memory).filter((key) => key.startsWith('wsl:')), ['wsl:Ubuntu/alice', 'wsl:Ubuntu/bob', 'wsl:Ubuntu/root']);
  const emails = ['wsl:Ubuntu/root', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/bob'].map((key) => {
    const read = result.memory.get(key)?.reading?.read;

    return read?.kind === 'read' && read.claude.state === 'found' ? read.claude.email : 'none';
  });
  assert.deepEqual(emails, ['root@example.test', 'alice@example.test', 'bob@example.test']);
  assert.equal(result.asked, 3, 'the summary counts the places');
  assert.equal(result.read, 3);
  assert.ok(result.memory.get('wsl')?.wsl !== undefined, 'the scope host holds its own facts');
});

test('E3S3-T3: wsl refused before the round — NO call to wsl.exe at all through the round, the list calls among them; in flight the place\'s result is dropped', async () => {
  const made = portOf(UBUNTU);
  const w = world({ hosts: ['wsl'], door: () => 'no', reply: accounts(ROUND_EMAILS) });
  const result = await round(w, made.port);
  assert.deepEqual(made.record, [], 'the port was never called');
  assert.deepEqual(w.admits, ['wsl']);
  assert.equal(result.memory.get('wsl')?.admission, 'refused');
  assert.deepEqual(keysOf(result.memory).filter((key) => key.startsWith('wsl:')), []);
});

test('E3S3-T3: a refusal in flight — a generation that moved while a place was read drops that result, and withRefusals drops every wsl: entry', async () => {
  const made = portOf(UBUNTU);
  const w = world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) });
  const inner = made.port.accounts;
  const port: WslPort = { ...made.port, accounts: async (place, mode, digest) => { const run = await inner(place, mode, digest); w.control.generation += 1; return run; } };
  const result = await round(w, port);
  assert.equal(result.abandoned, true);
  assert.deepEqual(keysOf(result.memory).filter((key) => key.startsWith('wsl:') && result.memory.get(key)?.reading !== undefined), [], 'nothing was published after the move');
  assert.ok(w.dropped.length > 0, 'the dropped result is logged once');
  const held: Memory = memoryOf(['wsl', { admission: undefined }], ['wsl:Ubuntu/alice', { failure: { sentence: 'x', atMs: 1 } }], ['h', {}]);
  assert.deepEqual(keysOf(withRefusals(held, new Set(['wsl']))), ['h', 'wsl']);
  assert.equal(withRefusals(held, new Set(['wsl'])).get('wsl')?.admission, 'refused');
});

test('E3S3-T4: wsl takes one turn in the rotation; its calls are one at a time and awaited; the summary counts places', async () => {
  const events: string[] = [];
  const made = portOf({ ...UBUNTU, events });
  const w = world({ hosts: ['a', 'b', 'wsl'], reply: accounts(ROUND_EMAILS) });
  const result = await round(w, made.port, new Map(), 2);
  assert.deepEqual(w.events.concat(events).filter((one) => one.startsWith('start')).length, 5);
  assert.deepEqual(events, ['start wsl:Ubuntu/root', 'end wsl:Ubuntu/root', 'start wsl:Ubuntu/alice', 'end wsl:Ubuntu/alice', 'start wsl:Ubuntu/bob', 'end wsl:Ubuntu/bob'], 'one at a time, in order');
  assert.deepEqual(w.events, ['start a', 'end a', 'start b', 'end b'], 'the ssh hosts come after the wsl turn of round 2 (rotated), each awaited');
  assert.equal(result.asked, 5);
  assert.equal(result.read, 5);
  const first = await round(world({ hosts: ['a', 'b', 'wsl'], reply: accounts(ROUND_EMAILS) }), portOf(UBUNTU).port, new Map(), 0);
  assert.equal(first.asked, 5, 'in round 0 wsl is last');
});

test('E3S3-T4: with no WSL port (any window that is not Windows) the scope host wsl is never called or asked', async () => {
  const w = world({ hosts: ['wsl', 'a'], reply: accounts(ROUND_EMAILS) });
  const result = await runRound(w.seams, new Map(), 0, 'timer', OFF);
  assert.deepEqual(w.admits.filter((host) => host === 'wsl'), []);
  assert.equal(result.asked, 1);
});

// ───────────────── T5: retirement ─────────────────

test('E3S3-T5: each cause of retirement — a closed scope host, a list that no longer names the distro, a users reply that no longer names the user, a distro that is not running', async () => {
  const both: Spec = { names: ['Ubuntu', 'Debian'], running: ['Ubuntu', 'Debian'], users: { Ubuntu: ['root 0', 'alice 1000', 'bob 1001'], Debian: ['root 0'] } };
  const first = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), portOf(both).port);
  assert.deepEqual(keysOf(first.memory).filter((key) => key.startsWith('wsl:')), ['wsl:Debian/root', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/bob', 'wsl:Ubuntu/root']);
  // the scope host's last terminal closes
  assert.deepEqual(keysOf(liveOnly(first.memory, ['a'])), [], 'a closed scope host takes its places with it');
  // the list no longer names Debian
  const noDebian = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), portOf({ ...both, names: ['Ubuntu'], running: ['Ubuntu'] }).port, first.memory);
  assert.deepEqual(keysOf(noDebian.memory).filter((key) => key.startsWith('wsl:Debian')), []);
  // the users reply no longer names bob
  const noBob = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), portOf({ ...both, users: { Ubuntu: ['root 0', 'alice 1000'], Debian: ['root 0'] } }).port, first.memory);
  assert.deepEqual(keysOf(noBob.memory).filter((key) => key.startsWith('wsl:')), ['wsl:Debian/root', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/root']);
  // a distro that is only not running keeps nothing and is said so
  const asleep = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), portOf({ ...both, running: ['Ubuntu'] }).port, first.memory);
  assert.deepEqual(keysOf(asleep.memory).filter((key) => key.startsWith('wsl:Debian')), []);
  const lines = asleep.memory.get('wsl')?.wsl?.distros.map((one) => `${one.distro}:${one.state}`);
  assert.deepEqual(lines, ['Ubuntu:read', 'Debian:not-running']);
  // a list that failed keeps what it had (older readings, with their time)
  const broken: WslPort = { list: () => Promise.resolve({ kind: 'refused', refusal: 'x', end: 'timeout' }), users: () => Promise.reject(new Error('never')), accounts: () => Promise.reject(new Error('never')) };
  const kept = await round(world({ hosts: ['wsl'] }), broken, first.memory);
  assert.deepEqual(keysOf(kept.memory).filter((key) => key.startsWith('wsl:')), ['wsl:Debian/root', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/bob', 'wsl:Ubuntu/root']);
  assert.equal(kept.memory.get('wsl')?.wsl?.failed, 'The list of WSL distros could not be read: WSL did not answer within 30 s.');
});

test('E3S3-T5: a place whose call failed keeps its older reading with that read\'s time; a distro that stopped in the round keeps no table', async () => {
  const first = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), portOf(UBUNTU).port);
  const olderAt = first.memory.get('wsl:Ubuntu/alice')?.reading?.atMs;
  const w = world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) });
  w.control.now += 60_000;
  const second = await round(w, portOf({ ...UBUNTU, fail: (key) => key === 'wsl:Ubuntu/alice' }).port, first.memory);
  const alice = second.memory.get('wsl:Ubuntu/alice');
  assert.equal(alice?.reading?.atMs, olderAt, 'the older reading stays, with its own time');
  assert.ok(alice?.failure !== undefined, 'and the failure is said');
  // the distro leaves the running list after the round began: no further call, no table
  let reads = 0;
  const stopping: WslPort = {
    ...portOf(UBUNTU).port,
    list: (running) => {
      reads += running ? 1 : 0;

      return Promise.resolve(answered(u16(running && reads >= 3 ? '' : 'Ubuntu\r\n')));
    },
  };
  const left = await round(world({ hosts: ['wsl'], reply: accounts(ROUND_EMAILS) }), stopping, first.memory);
  assert.deepEqual(keysOf(left.memory).filter((key) => key.startsWith('wsl:')), [], 'a distro that stopped keeps no table');
  assert.equal(left.memory.get('wsl')?.wsl?.distros.find((one) => one.distro === 'Ubuntu')?.said, LEFT_SAID);
});

// ───────────────── T6, T7: the wsl block of the model ─────────────────

const facts = (over: Partial<WslFacts> = {}): WslFacts => ({ distros: [], moreDistros: 0, moreUsers: 0, ignored: 0, failed: '', strikes: 0, ended: 'complete', ...over });
const wslEntry = (over: Partial<WslFacts> = {}): HostMemory => ({ wsl: facts(over) });
const modelOf = (memory: Memory, windows = true, hosts: readonly string[] = ['wsl']): TableModel => tableModel(inputOf({ hosts, memory, windows }));
const wslBlockOf = (model: TableModel): NonNullable<ReturnType<typeof blockOf>> => {
  const block = blockOf(model, 'wsl');
  assert.ok(block !== undefined);

  return block as NonNullable<typeof block>;
};

test('E3S3-T6: the wsl block — the heading with its count, S13 always, a line for each distro (S4, S5, S6, users), the notes (S11, S12)', () => {
  const memory = memoryOf(['wsl', wslEntry({
    distros: [
      { distro: 'Ubuntu', state: 'read', said: '', users: 2 },
      { distro: 'Debian', state: 'not-running', said: NOT_RUNNING_SAID, users: 0 },
      { distro: 'Old', state: 'left', said: LEFT_SAID, users: 0 },
      { distro: 'Bad', state: 'users-failed', said: 'The Linux users of “Bad” could not be read: the answer was cut.', users: 0 },
    ],
    moreDistros: 2, moreUsers: 3, ignored: 4,
  })]);
  const part = wslBlockOf(modelOf(memory)).wsl;
  assert.ok(part !== null);
  assert.equal(part?.heading, WSL_HEADING);
  assert.equal(part?.count, '6 distros');
  assert.equal(part?.login, WSL_LOGIN_SAID);
  assert.deepEqual(part?.distros.map((one) => `${one.name}: ${one.said}`), [
    'Ubuntu: 2 users', 'Debian: not running, so not read', 'Old: stopped since the round began, so not read', 'Bad: The Linux users of “Bad” could not be read: the answer was cut.',
  ]);
  assert.deepEqual(part?.notes, [
    'and 2 more distros are not read (the limit is 16)',
    'and 3 more users are not read (the limit is 16 for a distro, 32 in all)',
    '4 lines of the list were not understood and are not read.',
  ]);
  assert.equal(WSL_LOGIN_SAID, 'On a distro with systemd, WSL opens a login session for each user that is read. It runs that user’s profile — root’s as root — and stays until the distro stops.');
});

test('E3S3-T6: S13 stands with nothing read yet; S3 and S2 say the scope host\'s failure; off Windows S1 and nothing else', () => {
  assert.equal(wslBlockOf(modelOf(new Map())).wsl?.login, WSL_LOGIN_SAID);
  assert.equal(wslBlockOf(modelOf(new Map())).wsl?.count, 'not read yet');
  const failed = 'The list of WSL distros could not be read: wsl.exe ended with exit 3.';
  const block = wslBlockOf(modelOf(memoryOf(['wsl', wslEntry({ failed })])));
  assert.equal(block.head.said, failed);
  assert.deepEqual(block.wsl?.notes, [failed]);
  const missing = 'wsl.exe was not found at “C:\\Windows\\System32\\wsl.exe”, so WSL on this PC cannot be read.';
  assert.equal(wslBlockOf(modelOf(memoryOf(['wsl', wslEntry({ failed: missing })]))).head.said, missing);
  const off = wslBlockOf(modelOf(memoryOf(['wsl', wslEntry()]), false));
  assert.equal(off.head.said, WSL_ELSEWHERE_SAID);
  assert.equal(off.wsl, null);
  const placed = modelOf(memoryOf(['wsl', wslEntry()], ['wsl:Ubuntu/alice', seen(reading({ claude: claude() }), 1)]), false);
  assert.deepEqual(placed.blocks.map((one) => one.host), ['wsl'], 'off Windows no place is drawn');
});

test('E3S3-T7: root — no tool has no line; a failed read has one; every other place with no tool keeps its line', () => {
  const memory = memoryOf(
    ['wsl', wslEntry()],
    ['wsl:Ubuntu/root', seen(reading(), 1)],
    ['wsl:Ubuntu/alice', seen(reading(), 1)],
    ['wsl:Debian/root', { ...seen(reading(), 9), failure: { sentence: 'it did not answer', atMs: NOW - min(1) } }],
  );
  const model = modelOf(memory);
  assert.equal(blockOf(model, 'wsl:Ubuntu/root')?.line, null, 'root with no tool: no line');
  assert.equal(blockOf(model, 'wsl:Ubuntu/alice')?.line, 'no Claude Code, Codex or Antigravity found');
  const failedRoot = blockOf(model, 'wsl:Debian/root');
  assert.equal(failedRoot?.head.kind, 'failed');
  assert.match(failedRoot?.head.said ?? '', /the last read failed/u);
  assert.equal(failedRoot?.label, 'Debian · root');
  assert.equal(blockOf(model, 'wsl:Ubuntu/alice')?.canRefuse, false);
});

test('E3S3-T7 (the unsafe row): an unsafe tool is its own row with the path', { skip: 'waits for E3.S2: the parser\'s state `unsafe` and its text do not exist until revision 2 is merged' }, () => undefined);

test('E3S3-T2: an ssh host and a WSL user with one account — ONE usage call in the round; both say shared across 2 hosts', { skip: 'waits for E3.S2: the digest of the first reply and the join that the usage call needs' }, () => undefined);

// ───────────────── T8: the three groupings ─────────────────

const compare = compareRows as unknown as Compare;
const state = (groupBy: ViewState['groupBy']): ViewState => ({ v: 1, groupBy, sort: { key: 'name', dir: 'asc', tool: 'claude-code' } });

const MIXED = memoryOf(
  ['h1', seen(reading({ claude: claude() }), 5)],
  ['wsl', wslEntry({ distros: [{ distro: 'Ubuntu', state: 'read', said: '', users: 2 }] })],
  ['wsl:Ubuntu/alice', seen(reading({ claude: claude() }), 2)],
  ['wsl:Ubuntu/bob', seen(reading({ codex: codexOf() }), 3)],
  ['wsl:Debian/root', seen(reading(), 4)],
);

test('E3S3-T8: every place and every tool reading of an input that mixes ssh hosts and WSL places is in each of the three groupings; a WSL place is `<distro> · <user>` in User', () => {
  const model = modelOf(MIXED, true, ['h1', 'wsl']);
  const server = viewOf(model, state('server'), compare);
  assert.deepEqual(server.groupBy === 'server' ? server.blocks.map((one) => one.host) : [], ['h1', 'wsl', 'wsl:Debian/root', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/bob']);
  const account = viewOf(model, state('account'), compare);
  assert.equal(account.groupBy, 'account');
  if (account.groupBy === 'account') {
    assert.equal(account.accounts.length, 1, 'one account in two kinds of place');
    assert.deepEqual(account.accounts[0]?.places.map((one) => one.label).sort(), ['Ubuntu · alice', 'h1']);
    assert.deepEqual(account.codex.map((one) => one.label), ['Ubuntu · bob']);
    assert.deepEqual(account.none.map((one) => one.label).sort(), ['Debian · root', 'Ubuntu · bob', 'wsl']);
    assert.equal(account.accounts[0]?.places.every((one) => one.place === 'h1' || one.place.startsWith('wsl:')), true, 'the id stays the key');
  }
  const user = viewOf(model, state('user'), compare);
  assert.equal(user.groupBy, 'user');
  if (user.groupBy === 'user') {
    assert.deepEqual(user.rows.map((one) => one.label).sort(), ['Debian · root', 'Ubuntu · alice', 'Ubuntu · bob', 'h1', 'wsl']);
    assert.equal(user.rows.find((one) => one.label === 'Ubuntu · bob')?.cells.codex !== null, true);
    assert.equal(user.rows.find((one) => one.label === 'Ubuntu · alice')?.cells['claude-code']?.sharedAcross, 2, 'shared across 2 places');
  }
});

// ───────────────── T9, T11: the page, run ─────────────────

const HTML = accountsHtml('n0nce');

function pageOf(model: TableModel): ReturnType<typeof runAccountsPage> {
  const run = runAccountsPage(HTML, { now: NOW });
  assert.equal(run.failure(), '');
  run.message({ type: 'model', model });
  assert.equal(run.failure(), '', 'the page stopped on an error');

  return run;
}

test('E3S3-T9: the page draws the wsl block — its heading, S13, a line for each distro — and a place\'s update changes that place\'s table only', () => {
  const run = pageOf(modelOf(MIXED, true, ['h1', 'wsl']));
  const text = run.text();
  assert.ok(text.includes(WSL_HEADING));
  assert.ok(text.includes(WSL_LOGIN_SAID));
  assert.ok(text.includes('Ubuntu'));
  assert.ok(text.includes('Ubuntu · alice'));
  const before = run.document.querySelectorAll('section.block').map((one) => one.outerHTML);
  const next = modelOf(memoryOf(...MIXED, ['wsl:Ubuntu/alice', seen(reading({ claude: claude({ email: 'other@example.test' }) }), 1)]), true, ['h1', 'wsl']);
  const block = blockOf(next, 'wsl:Ubuntu/alice');
  run.message({ type: 'block', block, toolbar: next.toolbar });
  assert.equal(run.failure(), '');
  const after = run.document.querySelectorAll('section.block').map((one) => one.outerHTML);
  assert.equal(after.length, before.length, 'no place was added or lost');
  assert.deepEqual(after.filter((one, at) => one !== before[at]).length, 1, 'only that place\'s block changed');
  assert.ok(run.text().includes('other@example.test'));
});

test('E3S3-T9: a distro\'s or a user\'s text with markup in it is the text of a node', () => {
  const bad = '<img src=x onerror=alert(1)><b>b</b>';
  const model = modelOf(memoryOf(['wsl', wslEntry({ distros: [{ distro: 'Ubuntu', state: 'users-failed', said: `The Linux users of “${bad}” could not be read.`, users: 0 }], failed: bad })]));
  const run = pageOf(model);
  assert.ok(run.text().includes(bad), 'the text is there, as text');
  assert.equal(run.document.querySelectorAll('img').length, 0);
  assert.equal(run.document.querySelectorAll('b').length, 0);
});

test('E3S3-T11: the hover of an age says the exact time and how long the read took', () => {
  const run = pageOf(modelOf(memoryOf(['wsl', wslEntry()], ['wsl:Ubuntu/alice', seen(reading({ claude: claude() }), 2, { tookMs: 2_500 })], ['wsl:Ubuntu/bob', seen(reading({ claude: claude() }), 2, { tookMs: 420 })])));
  const titles = run.document.querySelectorAll('.age').map((one) => one.getAttribute('title') ?? '');
  assert.ok(titles.some((title) => title.includes('the read took 2.5 s')), JSON.stringify(titles));
  assert.ok(titles.some((title) => title.includes('the read took 420 ms')), JSON.stringify(titles));
  const plain = pageOf(modelOf(memoryOf(['h1', seen(reading({ claude: claude() }), 2)]), true, ['h1']));
  assert.equal(plain.document.querySelectorAll('.age').map((one) => one.getAttribute('title') ?? '').some((title) => title.includes('took')), false, 'a place with no timed call says no time');
});

// ───────────────── T10: copy-install ─────────────────

test('E3S3-T10: copy-install for a WSL place copies the fixed string of its manager word; a key the model does not hold is dropped', () => {
  const model = modelOf(memoryOf(['wsl', wslEntry()], ['wsl:Ubuntu/alice', seen(reading({ jqMissing: 'apt-get' }), 1)]));
  const command = accountsCommandOf({ type: 'copy-install', place: 'wsl:Ubuntu/alice' }, model);
  assert.equal(command?.kind, 'copy-install');
  assert.equal(command?.kind === 'copy-install' && command.decision.kind === 'copy' ? command.decision.text : '', INSTALL_JQ['apt-get']);
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'wsl:Ubuntu/mallory' }, model), undefined);
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'Ubuntu · alice' }, model), undefined, 'the label is not a key');
  assert.equal(accountsCommandOf({ type: 'refuse', host: 'wsl:Ubuntu/alice' }, model), undefined, 'a WSL place cannot be refused by itself');
});

// ───────────────── T12: the sentence is gone, and a test run has no WSL calls ─────────────────

test('E3S3-T12: the sentence "WSL on this PC is not read by this build" is in no source file; in a test run the WSL port is the replaced one or none', () => {
  const root = join(__dirname, '..', '..', 'src');
  const holders = readdirSync(root).filter((file) => file.endsWith('.ts') && readFileSync(join(root, file), 'utf8').includes('is not read by this build'));
  assert.deepEqual(holders, []);
  const host = readFileSync(join(root, 'remoteAccountsHost.ts'), 'utf8');
  const body = host.slice(host.indexOf('wslPort(log'), host.indexOf('rearm = '));
  assert.ok(body.includes('this.test') && body.indexOf('this.test') < body.indexOf('wslPortOf('), 'a test run returns before the real port is made');
  assert.equal(body.includes('hostPlatform() !== \'win32\''), true, 'and so does any window that is not Windows');
});
