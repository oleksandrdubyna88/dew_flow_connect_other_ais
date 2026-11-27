import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { compareRows } from '../pageTables';
import { viewOf, viewState } from '../remoteAccountsGroups';
import type { AccountBlock, Compare, ViewState } from '../remoteAccountsGroups';
import { tableModel } from '../remoteAccountsTable';
import type { TableInput, TableModel } from '../remoteAccountsTable';
import type { HostMemory } from '../remoteAccountsRound';
import {
  NOW, agyOf, bigInput, claude, codexOf, codexUsage, inputOf, memoryOf, min, reading, refused, seen, usageOf,
} from './accountsTableFixtures';

/**
 * The three groupings and the order rule (E2.S2, E2S2-T14 … T21): all in node, no browser, over a model that `tableModel` made.
 * Every email, organization and host is made up.
 */

const compare = compareRows as unknown as Compare;
const state = (groupBy: ViewState['groupBy'] = 'server', key: ViewState['sort']['key'] = 'name', dir: ViewState['sort']['dir'] = 'asc', tool: ViewState['sort']['tool'] = 'claude-code'): ViewState =>
  ({ v: 1, groupBy, sort: { key, dir, tool } });

const BIG: TableModel = tableModel(bigInput());

const accountsOf = (view: ReturnType<typeof viewOf>): readonly AccountBlock[] => {
  assert.equal(view.groupBy, 'account');

  return view.groupBy === 'account' ? view.accounts : [];
};

// ───────────────── T14 ─────────────────

test('E2S2-T14: server — the view\'s blocks are C4\'s blocks, in their order, unchanged', () => {
  const view = viewOf(BIG, state('server'), compare);

  assert.equal(view.groupBy, 'server');
  assert.deepEqual(view.groupBy === 'server' ? view.blocks : [], BIG.blocks);
});

// ───────────────── T15 ─────────────────

test('E2S2-T15: account — two places of one known account are ONE block, its usage once, both places as rows', () => {
  const accounts = accountsOf(viewOf(BIG, state('account'), compare));
  const ann = accounts.filter((one) => one.text.startsWith('ann@'));

  assert.equal(ann.length, 1);
  assert.deepEqual(ann[0]?.places.map((one) => one.place).sort(), ['host-a', 'host-b', 'host-e']);
  assert.equal(ann[0]?.usage.five.left, 44);
  assert.equal(ann[0]?.usage.span, null);
  assert.equal(JSON.stringify(viewOf(BIG, state('account'), compare)).split('"usage"').length - 1, accounts.length, 'one usage part for each account block');
});

test('E2S2-T15: an unknown account is a block of its own, and two accounts with equal numbers are two blocks', () => {
  const accounts = accountsOf(viewOf(BIG, state('account'), compare));
  const unknown = accounts.filter((one) => one.key === null);

  assert.deepEqual(unknown.map((one) => one.places.map((place) => place.place)), [['host-g']]);
  assert.equal(accounts.length, 3, 'ann, other, and the place whose account is not known');
  const twin = tableModel(inputOf({
    hosts: ['x', 'y'],
    memory: memoryOf(['x', seen(reading({ claude: claude({ email: 'x@example.test', usage: usageOf(1, 1, 1) }) }), 1)], ['y', seen(reading({ claude: claude({ email: 'y@example.test', usage: usageOf(1, 1, 1) }) }), 1)]),
  }));
  assert.equal(accountsOf(viewOf(twin, state('account'), compare)).length, 2, 'the same numbers do not make one account');
});

// ───────────────── T16 ─────────────────

test('E2S2-T16: account — a failed place stays under its older account with the failure and the older instant', () => {
  const ann = accountsOf(viewOf(BIG, state('account'), compare)).find((one) => one.text.startsWith('ann@'));
  const failed = ann?.places.find((one) => one.place === 'host-b');

  assert.equal(failed?.readAt, NOW - min(25));
  assert.deepEqual(failed?.failure, { said: 'the last read failed: “host-b” did not answer over ssh within 30 s.', at: NOW - min(3) });
  assert.equal(ann?.places.find((one) => one.place === 'host-a')?.failure, null);
});

test('E2S2-T16: Codex and Antigravity join nothing, and the places in no Claude block are in the last section with their sentence', () => {
  const view = viewOf(BIG, state('account'), compare);
  assert.equal(view.groupBy, 'account');
  if (view.groupBy !== 'account') {
    return;
  }

  assert.deepEqual(view.codex.map((one) => one.place), ['host-a', 'host-g', 'host-i'], 'two places of one Codex plan are two rows');
  assert.deepEqual(view.agy.map((one) => one.place), ['host-a']);
  const none = new Map(view.none.map((one) => [one.place, one]));
  assert.deepEqual([...none.keys()].sort(), ['host-c', 'host-d', 'host-h', 'host-i', 'wsl']);
  assert.match(none.get('host-c')?.said ?? '', /Not this host/u);
  assert.equal(none.get('host-c')?.action, 'read-again');
  assert.equal(none.get('host-d')?.action, 'copy-install');
  assert.equal(none.get('host-d')?.manager, 'apt-get');
  assert.equal(none.get('host-h')?.said, 'signed out');
  assert.equal(none.get('host-i')?.said, 'no Claude Code found');
  assert.match(none.get('wsl')?.said ?? '', /wsl — this PC/u);
  assert.equal(none.get('wsl')?.action, null);
});

// ───────────────── T17 ─────────────────

test('E2S2-T17: user — one row for each place, the three tool cells, an absent tool\'s cell is null', () => {
  const view = viewOf(BIG, state('user'), compare);
  assert.equal(view.groupBy, 'user');
  const rows = view.groupBy === 'user' ? view.rows : [];

  assert.deepEqual(rows.map((one) => one.place), BIG.blocks.map((one) => one.host));
  const a = rows.find((one) => one.place === 'host-a');
  assert.equal(a?.cells['claude-code']?.account.text, `ann@example.test · Example Org`);
  assert.notEqual(a?.cells.codex, null);
  assert.notEqual(a?.cells.agy, null);
  const i = rows.find((one) => one.place === 'host-i');
  assert.equal(i?.cells['claude-code'], null);
  assert.equal(i?.cells.agy, null);
  assert.equal(rows.find((one) => one.place === 'host-c')?.cells.codex, null);
  assert.equal(rows.find((one) => one.place === 'host-b')?.failure?.at, NOW - min(3));
  assert.equal(rows.find((one) => one.place === 'host-b')?.cells['claude-code']?.readAt, NOW - min(25));
});

// ───────────────── T18 ─────────────────

const generator = (seed: number): (() => number) => {
  let at = seed;

  return () => { at = (at * 1_103_515_245 + 12_345) % 2_147_483_648; return at / 2_147_483_648; };
};

function randomInput(next: () => number): TableInput {
  const pick = <T,>(items: readonly T[]): T => items[Math.floor(next() * items.length)] as T;
  const hosts = ['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'].filter(() => next() < 0.8);
  const entries = hosts.map((host): readonly [string, HostMemory] => {
    const email = pick(['', 'a@example.test', 'b@example.test']);
    const read = reading({
      claude: pick([
        { state: 'absent' }, { state: 'signed-out' }, { state: 'unreadable', why: 'auth' },
        claude({ email, org: pick([null, 'O']), usage: pick([{ state: 'skipped' }, usageOf(pick([null, 0, 50, 100]), pick([null, 20]), pick([null, 5])), { state: 'failed', why: 'timeout' }]) }),
      ] as const),
      codex: pick([{ state: 'absent' }, codexOf(), codexOf(codexUsage({ windows: [] })), { state: 'found', signInFile: false, plan: null, usage: { state: 'failed', why: 'none' } }] as const),
      agy: pick([{ state: 'absent' }, agyOf()] as const),
      jqMissing: pick([undefined, undefined, 'dnf'] as const),
    });
    const memory: HostMemory = pick([seen(read, 1 + Math.floor(next() * 50)), { failure: { sentence: 'x', atMs: NOW }, ...seen(read, 20) }, {}]);

    return [host, memory];
  });

  return inputOf({ hosts: [...hosts, ...(next() < 0.5 ? ['wsl'] : [])], memory: new Map(entries), standings: next() < 0.4 ? [refused('p2')] : [], permission: pick([{ given: '2026-11-26T12:00:00.000Z' }, { given: '2026-11-26T12:00:00.000Z' }, 'none'] as const) });
}

const leaves = (value: unknown, into: Set<unknown> = new Set()): Set<unknown> => {
  if (typeof value === 'object' && value !== null) {
    for (const one of Object.values(value)) {
      leaves(one, into);
    }
  } else {
    into.add(value);
  }

  return into;
};

/** The words a view adds that are the structure of the page and not a value of the model. */
const STRUCTURE = new Set(['server', 'account', 'user', 'read-again', 'copy-install']);

test('E2S2-T18: the count — every place and every tool reading of the model is in each of the three views, and the view holds no value the model does not', () => {
  const next = generator(11);
  const sorts: readonly ViewState['sort']['key'][] = ['name', 'account', 'left5h', 'reset5h', 'leftWeek', 'resetWeek', 'age'];
  for (let round = 0; round < 150; round += 1) {
    const model = tableModel(randomInput(next));
    const rowsOf = (tool: string): number => model.blocks.flatMap((one) => one.rows).filter((one) => one.tool === tool).length;
    const found = model.blocks.flatMap((one) => one.rows).filter((one) => one.tool === 'claude-code' && one.found);
    const keys = new Set(found.flatMap((one) => (one.accountKey === null ? [] : [one.accountKey])));
    const key = sorts[round % sorts.length] as ViewState['sort']['key'];
    const dir = round % 2 === 0 ? 'asc' : 'desc';
    const modelLeaves = leaves(model);

    const server = viewOf(model, state('server', key, dir), compare);
    assert.equal(server.groupBy === 'server' ? server.blocks.length : -1, model.blocks.length);
    assert.equal(server.groupBy === 'server' ? server.blocks.flatMap((one) => one.rows).length : -1, model.blocks.flatMap((one) => one.rows).length);

    const account = viewOf(model, state('account', key, dir), compare);
    assert.equal(account.groupBy, 'account');
    if (account.groupBy === 'account') {
      assert.equal(account.accounts.flatMap((one) => one.places).length, found.length, 'every Claude reading is in an account block');
      assert.equal(account.accounts.length, keys.size + found.filter((one) => one.accountKey === null).length, 'one usage for each account block');
      assert.equal(account.codex.length, rowsOf('codex'));
      assert.equal(account.agy.length, rowsOf('agy'));
      const inClaude = new Set(account.accounts.flatMap((one) => one.places.map((place) => place.place)));
      const none = account.none.map((one) => one.place);
      assert.equal(new Set(none).size, none.length, 'a place is in the last section once');
      for (const block of model.blocks) {
        const claudeFound = block.rows.some((one) => one.tool === 'claude-code' && one.found);
        assert.equal(inClaude.has(block.host), claudeFound);
        assert.equal(none.includes(block.host), !claudeFound, `${block.host} is in a Claude block or in the last section, never both and never neither`);
      }
    }

    const user = viewOf(model, state('user', key, dir, 'codex'), compare);
    assert.equal(user.groupBy === 'user' ? user.rows.length : -1, model.blocks.length);
    assert.equal(user.groupBy === 'user' ? user.rows.flatMap((one) => Object.values(one.cells)).filter((one) => one !== null).length : -1, model.blocks.flatMap((one) => one.rows).length);

    for (const view of [server, account, user]) {
      for (const leaf of leaves(view)) {
        assert.ok(modelLeaves.has(leaf) || STRUCTURE.has(leaf as string), `the view holds ${JSON.stringify(leaf)}, which the model does not`);
      }
    }
  }
});

// ───────────────── T19 ─────────────────

const HOSTS = ['h1', 'h2', 'h3', 'h4'];

/** h1 80% left, read 1 min ago; h2 20% left, 10 min; h3 never read; h4 0% left (a real zero), 5 min; the instants of the reset differ too. */
function orderModel(): TableModel {
  const usage = (used: number, resetMin: number) => ({ state: 'read' as const, unmatched: 0, windows: [{ name: '5h' as const, usedPercent: used, resetsAtMs: NOW + min(resetMin) }, { name: 'week' as const, usedPercent: used, resetsAtMs: NOW + min(resetMin * 10) }] });
  const memory = memoryOf(
    ['h1', seen(reading({ claude: claude({ email: 'one@example.test', usage: usage(20, 90) }) }), 1)],
    ['h2', seen(reading({ claude: claude({ email: 'two@example.test', usage: usage(80, 30) }) }), 10)],
    ['h4', seen(reading({ claude: claude({ email: 'four@example.test', usage: usage(100, 60) }) }), 5)],
  );

  return tableModel(inputOf({ hosts: HOSTS, memory }));
}

const hostsOf = (view: ReturnType<typeof viewOf>): readonly string[] => (view.groupBy === 'server' ? view.blocks.map((one) => one.host) : view.groupBy === 'user' ? view.rows.map((one) => one.place) : []);

test('E2S2-T19: blocks by their own value — name and age; a dash (a place never read) is last both ways; equal keys fall back to the name', () => {
  const model = orderModel();
  const of = (key: ViewState['sort']['key'], dir: ViewState['sort']['dir'], groupBy: ViewState['groupBy'] = 'server') => hostsOf(viewOf(model, state(groupBy, key, dir), compare));

  assert.deepEqual(of('name', 'asc'), ['h1', 'h2', 'h3', 'h4']);
  assert.deepEqual(of('name', 'desc'), ['h4', 'h3', 'h2', 'h1']);
  assert.deepEqual(of('age', 'asc'), ['h1', 'h4', 'h2', 'h3'], 'age ascending is the newest read first, and never read is last');
  assert.deepEqual(of('age', 'desc'), ['h2', 'h4', 'h1', 'h3'], 'a dash is last in both directions');
  assert.deepEqual(of('age', 'asc', 'user'), ['h1', 'h4', 'h2', 'h3']);
});

test('E2S2-T19: by their first row for the other keys; a real 0% sorts as zero and a dash after every value', () => {
  const model = orderModel();
  const of = (key: ViewState['sort']['key'], dir: ViewState['sort']['dir'], groupBy: ViewState['groupBy'] = 'server') => hostsOf(viewOf(model, state(groupBy, key, dir), compare));

  assert.deepEqual(of('left5h', 'asc'), ['h4', 'h2', 'h1', 'h3'], 'a real 0% is before 20%, and the place with no row is last');
  assert.deepEqual(of('left5h', 'desc'), ['h1', 'h2', 'h4', 'h3'], 'and the dash stays last when the direction turns');
  assert.deepEqual(of('reset5h', 'asc'), ['h2', 'h4', 'h1', 'h3']);
  assert.deepEqual(of('resetWeek', 'desc'), ['h1', 'h4', 'h2', 'h3']);
  assert.deepEqual(of('leftWeek', 'asc', 'user'), ['h4', 'h2', 'h1', 'h3']);
  assert.deepEqual(of('account', 'asc'), ['h4', 'h1', 'h2', 'h3'], 'by the account text of the first row');
});

test('E2S2-T19: rows inside a block — every key both ways, a dash last, equal keys by the name ascending in both directions', () => {
  const memory = memoryOf(['h', seen(reading({ claude: claude({ usage: usageOf(100, 50, 5) }), codex: codexOf(), agy: agyOf() }), 1)]);
  const block = (key: ViewState['sort']['key'], dir: ViewState['sort']['dir']) => {
    const view = viewOf(tableModel(inputOf({ hosts: ['h'], memory })), state('server', key, dir), compare);

    return (view.groupBy === 'server' ? view.blocks[0]?.rows ?? [] : []).map((one) => one.tool);
  };

  assert.deepEqual(block('name', 'asc'), ['claude-code', 'codex', 'agy']);
  assert.deepEqual(block('name', 'desc'), ['agy', 'codex', 'claude-code']);
  // claude 0% left, codex 90%, agy a dash (no figures)
  assert.deepEqual(block('left5h', 'asc'), ['claude-code', 'codex', 'agy']);
  assert.deepEqual(block('left5h', 'desc'), ['codex', 'claude-code', 'agy']);
  // claude week 50 used → 50 left; codex 86; agy dash
  assert.deepEqual(block('leftWeek', 'asc'), ['claude-code', 'codex', 'agy']);
  assert.deepEqual(block('reset5h', 'asc'), ['codex', 'claude-code', 'agy']);
  assert.deepEqual(block('reset5h', 'desc'), ['claude-code', 'codex', 'agy']);
  assert.deepEqual(block('account', 'asc'), ['codex', 'claude-code', 'agy']);
  // all three read at the same instant: equal ages fall back to the name, ascending, in both directions
  assert.deepEqual(block('age', 'asc'), ['claude-code', 'codex', 'agy']);
  assert.deepEqual(block('age', 'desc'), ['claude-code', 'codex', 'agy']);
});

test('E2S2-T19: account view — blocks by their own value, the sections keep their places, and the lines inside a section are sorted', () => {
  const sorted = (key: ViewState['sort']['key'], dir: ViewState['sort']['dir']) => viewOf(BIG, state('account', key, dir), compare);
  const asc = sorted('left5h', 'asc');
  const desc = sorted('left5h', 'desc');

  assert.equal(asc.groupBy, 'account');
  if (asc.groupBy !== 'account' || desc.groupBy !== 'account') {
    return;
  }
  assert.deepEqual(asc.accounts.map((one) => one.usage.five.left), [44, 90, null], 'by the usage of the block, a dash last');
  assert.deepEqual(desc.accounts.map((one) => one.usage.five.left), [90, 44, null]);
  assert.deepEqual(Object.keys(asc), ['groupBy', 'accounts', 'codex', 'agy', 'none'], 'the sections are in their places');
  assert.deepEqual(Object.keys(desc), Object.keys(asc));
  const byName = sorted('name', 'desc');
  assert.deepEqual(byName.groupBy === 'account' ? byName.codex.map((one) => one.place) : [], ['host-i', 'host-g', 'host-a']);
  assert.deepEqual(byName.groupBy === 'account' ? byName.none.map((one) => one.place) : [], ['wsl', 'host-i', 'host-h', 'host-d', 'host-c']);
  const places = sorted('age', 'asc');
  const ann = places.groupBy === 'account' ? places.accounts.find((one) => one.text.startsWith('ann@')) : undefined;
  assert.deepEqual(ann?.places.map((one) => one.place), ['host-a', 'host-e', 'host-b'], 'a row\'s age: the newest read first');
});

test('E2S2-T19: user — the window and account keys read the tool of the state', () => {
  const rowsFor = (tool: ViewState['sort']['tool']) => {
    const view = viewOf(BIG, state('user', 'left5h', 'asc', tool), compare);

    return view.groupBy === 'user' ? view.rows.map((one) => one.place) : [];
  };

  assert.deepEqual(rowsFor('codex').slice(0, 3), ['host-i', 'host-a', 'host-g'], 'host-i has 60% left, the others 90%, and the equal ones are in name order');
  assert.notDeepEqual(rowsFor('codex'), rowsFor('claude-code'));
  assert.deepEqual(rowsFor('agy').slice(0, 1), ['host-a'], 'only host-a has Antigravity, and agy has no figures: every cell is a dash, so the order is by the name');
});

// ───────────────── T20 ─────────────────

test('E2S2-T20: viewState — anything that is not exactly a known state is server, name, asc, claude-code', () => {
  const FALLBACK: ViewState = state('server', 'name', 'asc', 'claude-code');
  const bad: readonly unknown[] = [
    undefined, null, 'server', 7, [], [state('user')], {}, { v: 1 }, { ...state('user'), v: 2 },
    { ...state('user'), groupBy: 'host' }, { ...state('user'), sort: { key: 'size', dir: 'asc', tool: 'codex' } },
    { ...state('user'), sort: { key: 'name', dir: 'up', tool: 'codex' } }, { ...state('user'), sort: { key: 'name', dir: 'asc', tool: 'bash' } },
    { ...state('user'), sort: null }, { ...state('user'), sort: 'name' }, { ...state('user'), sort: { key: 'name', dir: 'asc' } },
  ];
  for (const one of bad) {
    assert.deepEqual(viewState(one), FALLBACK, `for ${JSON.stringify(one)}`);
  }
});

test('E2S2-T20: a known state comes back equal and viewState never adds a field', () => {
  for (const groupBy of ['server', 'account', 'user'] as const) {
    for (const tool of ['claude-code', 'codex', 'agy'] as const) {
      const known = state(groupBy, 'leftWeek', 'desc', tool);

      assert.deepEqual(viewState(known), known);
      assert.deepEqual(viewState(JSON.parse(JSON.stringify(known))), known);
    }
  }
  const extra = viewState({ ...state('user'), model: 'secret', sort: { ...state('user').sort, host: 'h' } });
  assert.deepEqual(Object.keys(extra).sort(), ['groupBy', 'sort', 'v']);
  assert.deepEqual(Object.keys(extra.sort).sort(), ['dir', 'key', 'tool']);
});

// ───────────────── T21 ─────────────────

/** A function from its own source text, as the page makes it: `var viewOf = <source>`. */
const fromSource = <F,>(fn: unknown): F => new Function(`return (${String(fn)});`)() as F;

test('E2S2-T21: viewOf and viewState run from their own source text, with compareRows passed, and give the same view', () => {
  const embeddedView = fromSource<typeof viewOf>(viewOf);
  const embeddedState = fromSource<typeof viewState>(viewState);
  const embeddedCompare = fromSource<Compare>(compareRows);

  for (const groupBy of ['server', 'account', 'user'] as const) {
    for (const key of ['name', 'account', 'left5h', 'reset5h', 'leftWeek', 'resetWeek', 'age'] as const) {
      for (const dir of ['asc', 'desc'] as const) {
        const one = state(groupBy, key, dir, 'codex');
        assert.deepEqual(embeddedView(BIG, one, embeddedCompare), viewOf(BIG, one, compare), `${groupBy} ${key} ${dir}`);
        assert.deepEqual(embeddedState(one), viewState(one));
      }
    }
  }
  assert.deepEqual(embeddedView(orderModel(), state('server', 'age', 'desc'), embeddedCompare), viewOf(orderModel(), state('server', 'age', 'desc'), compare));
});

test('E2S2-T21: neither body holds a backtick', () => {
  assert.ok(!String(viewOf).includes('`'), 'viewOf holds a backtick');
  assert.ok(!String(viewState).includes('`'), 'viewState holds a backtick');
});

test('E2S2-T19: equal keys fall back to the name ascending in both directions, whatever order the model came in', () => {
  const model = tableModel(inputOf({
    hosts: ['host-a', 'host-b'],
    memory: memoryOf(
      ['host-a', seen(reading({ claude: claude({ email: 'zed@example.test', usage: { state: 'skipped' } }) }), 1)],
      ['host-b', seen(reading({ claude: claude({ email: 'abe@example.test', usage: { state: 'skipped' } }) }), 1)],
    ),
  }));
  for (const dir of ['asc', 'desc'] as const) {
    const view = viewOf(model, state('account', 'left5h', dir), compare);

    assert.deepEqual(view.groupBy === 'account' ? view.accounts.map((one) => one.text.slice(0, 3)) : [], ['abe', 'zed'], `both are dashes, so the name decides (${dir})`);
  }
});

// ───────────────── the Server view sorts a host by the SELECTED tool's row ─────────────────

test('E2S2-T15 (review): a host block in Server sorts by the row of the selected tool, and a host without that tool is a dash that sorts last', () => {
  const claudeAt = (usedFive: number, email = 'ann@example.test') => claude({ email, usage: usageOf(usedFive, 10, null) });
  const model = tableModel(inputOf({
    hosts: ['ha', 'hb', 'hc'],
    memory: memoryOf(
      ['ha', seen(reading({ claude: claudeAt(20), codex: codexOf(codexUsage({ windows: [{ minutes: 300, usedPercent: 90, resetsAtMs: NOW + min(65) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(6_000) }] })) }), 1)],
      ['hb', seen(reading({ claude: claudeAt(50, 'bob@example.test') }), 1)],
      ['hc', seen(reading({ codex: codexOf(codexUsage({ windows: [{ minutes: 300, usedPercent: 95, resetsAtMs: NOW + min(65) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(6_000) }] })) }), 1)],
    ),
  }));
  const order = (tool: ViewState['sort']['tool'], dir: ViewState['sort']['dir']): readonly string[] => {
    const view = viewOf(model, state('server', 'left5h', dir, tool), compare);

    return view.groupBy === 'server' ? view.blocks.map((one) => one.host) : [];
  };
  // Claude: ha 80 left, hb 50 left, hc has no Claude (a dash). Ascending: 50, 80, dash. Codex's 10 % of ha must not put ha first.
  assert.deepEqual(order('claude-code', 'asc'), ['hb', 'ha', 'hc']);
  assert.deepEqual(order('claude-code', 'desc'), ['ha', 'hb', 'hc'], 'a dash is last in both directions');
  // Codex: hc 5 left, ha 10 left, hb has no Codex (a dash).
  assert.deepEqual(order('codex', 'asc'), ['hc', 'ha', 'hb']);
  assert.deepEqual(order('codex', 'desc'), ['ha', 'hc', 'hb']);
});
