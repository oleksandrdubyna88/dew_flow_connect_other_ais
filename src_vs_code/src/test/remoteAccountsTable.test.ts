import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { MANAGER_WORDS } from '../remoteAccountsReply';
import type { ManagerWord } from '../remoteAccountsReply';
import { INSTALL_JQ } from '../remoteAccountsRows';
import { NO_TERMINAL_SAID, WSL_ELSEWHERE_SAID, WSL_WINDOWS_SAID, blockOf, changedBlocks, tableModel } from '../remoteAccountsTable';
import type { Block, TableModel } from '../remoteAccountsTable';
import { HOSTS_SAID } from '../remoteHostsSaid';
import {
  ANN, NOW, agyOf, claude, codexOf, codexUsage, hour, inputOf, memoryOf, min, reading, refused, seen, usageOf,
} from './accountsTableFixtures';

/**
 * The table's model (E2.S2, E2S2-T1 … T13): pure, over a window's memory and a clock-free input. Every email, organization and host is made up.
 */

const blockFor = (model: TableModel, host: string): Block => {
  const found = blockOf(model, host);
  assert.ok(found !== undefined, `no block for ${host}`);

  return found;
};

// ───────────────── T1, T2 ─────────────────

test('E2S2-T1: one block for each scope host of the live terminals, in code-unit order; none for a host with no live terminal', () => {
  const memory = memoryOf(['gone', seen(reading(), 1)], ['b', seen(reading(), 1)]);
  const model = tableModel(inputOf({ hosts: ['b', 'B', 'a', 'b'], memory }));

  assert.deepEqual(model.blocks.map((one) => one.host), ['B', 'a', 'b']);
  assert.equal(model.empty, null);
});

test('E2S2-T1: no Creds terminal is the model\'s one sentence', () => {
  const model = tableModel(inputOf({ hosts: [] }));

  assert.deepEqual(model.blocks, []);
  assert.equal(model.empty, NO_TERMINAL_SAID);
});

test('E2S2-T2: each heading kind from its memory and standing, with its instant', () => {
  const memory = memoryOf(
    ['read', seen(reading({ claude: claude() }), 3)],
    ['failed', { failure: { sentence: '“failed” did not answer over ssh within 30 s.', atMs: NOW - min(3) }, ...seen(reading({ claude: claude() }), 25) }],
    ['unread', { unreadable: { why: 'cut', atMs: NOW - min(2) } }],
    ['jq', seen(reading({ jqMissing: 'apt-get' }), 4)],
  );
  const model = tableModel(inputOf({ hosts: ['read', 'failed', 'unread', 'jq', 'never'], memory }));

  assert.deepEqual(blockFor(model, 'read').head, { kind: 'read', at: NOW - min(3), said: '', manager: null });
  assert.equal(blockFor(model, 'never').head.kind, 'never');
  assert.equal(blockFor(model, 'never').head.at, null);
  const failed = blockFor(model, 'failed');
  assert.equal(failed.head.kind, 'failed');
  assert.equal(failed.head.at, NOW - min(3), 'the instant of the FAILURE is on the heading');
  assert.equal(failed.head.said, 'the last read failed: “failed” did not answer over ssh within 30 s.', 'the runner\'s own sentence, whole');
  assert.equal(blockFor(model, 'unread').head.kind, 'failed');
  assert.deepEqual(blockFor(model, 'jq').head, { kind: 'jq-missing', at: NOW - min(4), said: 'jq is missing on this host, so nothing can be read there.', manager: 'apt-get' });
});

test('E2S2-T2: a refused host is not allowed with its reason and has no rows; the reasons follow the door\'s order', () => {
  const memory = memoryOf(['r', seen(reading({ claude: claude() }), 1)], ['d', seen(reading({ claude: claude() }), 1)]);
  const model = tableModel(inputOf({ hosts: ['r', 'd'], memory, standings: [refused('r')] }));
  const refusedBlock = blockFor(model, 'r');

  assert.equal(refusedBlock.head.kind, 'not-allowed');
  assert.match(refusedBlock.head.said, /Not this host/u);
  assert.deepEqual(refusedBlock.rows, [], 'a refused host shows no rows');
  assert.equal(refusedBlock.canReadAgain, true);
  assert.equal(refusedBlock.canRefuse, false);
  assert.equal(blockFor(model, 'd').canRefuse, true);

  const damaged = tableModel(inputOf({ hosts: ['r', 'd'], memory, standings: [refused('r')], damaged: 'file' }));
  assert.match(blockFor(damaged, 'r').head.said, /Not this host/u, 'a refusal is the first answer of the door');
  assert.equal(blockFor(damaged, 'd').head.said, HOSTS_SAID.rowDamagedFile);
  assert.equal(blockFor(tableModel(inputOf({ hosts: ['d'], memory, damaged: 'list' })), 'd').head.said, HOSTS_SAID.rowDamagedList);
  assert.equal(blockFor(tableModel(inputOf({ hosts: ['d'], memory, permission: 'waiting' })), 'd').head.said, HOSTS_SAID.rowWaiting);
  const none = tableModel(inputOf({ hosts: ['d'], memory, permission: 'none' }));
  assert.equal(blockFor(none, 'd').head.kind, 'not-allowed');
  assert.deepEqual(blockFor(none, 'd').rows, [], 'without the permission nothing of a memory is shown');
});

// ───────────────── T3 ─────────────────

test('E2S2-T3: a tool that is absent has no row; a place with no tool has one line; the version under the tool, or not known', () => {
  const memory = memoryOf(
    ['full', seen(reading({ claude: claude({ version: null }), codex: codexOf(), agy: agyOf() }), 1)],
    ['only', seen(reading({ codex: codexOf() }), 1)],
    ['empty', seen(reading(), 1)],
  );
  const model = tableModel(inputOf({ hosts: ['full', 'only', 'empty'], memory }));

  assert.deepEqual(blockFor(model, 'full').rows.map((one) => one.tool), ['claude-code', 'codex', 'agy']);
  assert.equal(blockFor(model, 'full').rows[0]?.version, 'not known');
  assert.equal(blockFor(model, 'full').rows[2]?.version, '1.5.12');
  assert.deepEqual(blockFor(model, 'only').rows.map((one) => one.tool), ['codex']);
  assert.equal(blockFor(model, 'only').line, null);
  assert.deepEqual(blockFor(model, 'empty').rows, []);
  assert.equal(blockFor(model, 'empty').line, 'no Claude Code, Codex or Antigravity found');
});

// ───────────────── T4 ─────────────────

/** A small deterministic generator, so the property test is the same on every run. */
const generator = (seed: number): (() => number) => {
  let state = seed;

  return () => { state = (state * 1_103_515_245 + 12_345) % 2_147_483_648; return state / 2_147_483_648; };
};

const NUMBERS = [null, null, 0, 100, 37.5, Number.NaN, 55] as const;

test('E2S2-T4: absent is not zero — no path of the model makes 0 from null, NaN or a missing window', () => {
  const next = generator(7);
  const pick = <T,>(items: readonly T[]): T => items[Math.floor(next() * items.length)] as T;
  for (let round = 0; round < 200; round += 1) {
    const windows = [
      ...(next() < 0.7 ? [{ name: '5h' as const, usedPercent: pick(NUMBERS), resetsAtMs: pick([null, NOW + min(5), Number.NaN]) }] : []),
      ...(next() < 0.7 ? [{ name: 'week' as const, usedPercent: pick(NUMBERS), resetsAtMs: pick([null, NOW + min(5)]) }] : []),
      ...(next() < 0.5 ? [{ name: { model: 'Fable' }, usedPercent: pick(NUMBERS), resetsAtMs: null }] : []),
    ];
    const codexWindows = [300, 10_080, 120].flatMap((minutes) => (next() < 0.6 ? [{ minutes, usedPercent: pick(NUMBERS), resetsAtMs: pick([null, NOW + min(9)]) }] : []));
    const read = reading({
      claude: claude({ usage: { state: 'read', windows, unmatched: 0 } }),
      codex: codexOf(codexUsage({ windows: codexWindows })),
    });
    const model = tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(read, 1)]) }));
    for (const row of blockFor(model, 'h').rows) {
      const cells = [row.five, row.week, ...row.models, ...row.others];
      for (const cell of cells) {
        assert.ok(cell.left === null || (Number.isFinite(cell.left) && cell.left >= 0 && cell.left <= 100), `left is ${String(cell.left)}`);
      }
      if (row.tool === 'claude-code') {
        const five = windows.find((one) => one.name === '5h');
        const want = five === undefined || typeof five.usedPercent !== 'number' || Number.isNaN(five.usedPercent) ? null : Math.max(0, Math.min(100, Math.round(100 - five.usedPercent)));
        assert.equal(row.five.left, want, `5-hour window of ${JSON.stringify(five)}`);
        if (five === undefined || five.resetsAtMs === null || Number.isNaN(five.resetsAtMs)) {
          assert.equal(row.five.resetsAt, null);
        }
      }
    }
  }
});

test('E2S2-T4: a real 100% used is 0 left, and a window that is not there is null — never the same', () => {
  const read = reading({ claude: claude({ usage: { state: 'read', unmatched: 0, windows: [{ name: '5h', usedPercent: 100, resetsAtMs: null }] } }) });
  const row = blockFor(tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(read, 1)]) })), 'h').rows[0];

  assert.equal(row?.five.left, 0);
  assert.equal(row?.week.left, null);
  assert.equal(row?.week.resetsAt, null);
});

// ───────────────── T5 ─────────────────

test('E2S2-T5: a Codex window is placed by its length, the session file\'s instant is on Codex\'s row, and Claude\'s model windows are sub-lines', () => {
  const windows = [{ minutes: 300, usedPercent: 10, resetsAtMs: NOW + min(60) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(7_000) }, { minutes: 120, usedPercent: 20, resetsAtMs: null }];
  const memory = memoryOf(['h', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }), codex: codexOf(codexUsage({ windows })) }), 1)]);
  const [claudeRow, codexRow] = blockFor(tableModel(inputOf({ hosts: ['h'], memory })), 'h').rows;

  assert.deepEqual(codexRow?.five, { left: 90, resetsAt: NOW + min(60) });
  assert.deepEqual(codexRow?.week, { left: 86, resetsAt: NOW + min(7_000) });
  assert.deepEqual(codexRow?.others, [{ name: '2h', left: 80, resetsAt: null }]);
  assert.equal(codexRow?.session, NOW - hour(48));
  assert.equal(codexRow?.account.text, 'account name not shown by Codex');
  assert.equal(codexRow?.account.plan, 'prolite');
  assert.deepEqual(claudeRow?.models, [{ name: 'Fable', left: 51, resetsAt: null }]);
  assert.deepEqual(claudeRow?.five, { left: 44, resetsAt: NOW + min(130) });

  const swapped = [{ minutes: 10_080, usedPercent: 14, resetsAtMs: null }, { minutes: 300, usedPercent: 10, resetsAtMs: null }];
  const other = blockFor(tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ codex: codexOf(codexUsage({ windows: swapped })) }), 1)]) })), 'h').rows[0];
  assert.equal(other?.five.left, 90, 'a window is placed by its length, never by the slot it came in');
  assert.equal(other?.week.left, 86);
});

// ───────────────── T6 ─────────────────

test('E2S2-T6: shared across N — two places of one known account both say 2 and show the same figures', () => {
  const memory = memoryOf(
    ['a', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 3)],
    ['b', seen(reading({ claude: claude({ usage: { state: 'skipped' } }) }), 2)],
    ['c', seen(reading({ claude: claude({ email: 'other@example.test', usage: usageOf(56, 77, 49) }) }), 1)],
  );
  const model = tableModel(inputOf({ hosts: ['a', 'b', 'c'], memory }));
  const [a, b, c] = ['a', 'b', 'c'].map((host) => blockFor(model, host).rows[0]);

  assert.equal(a?.sharedAcross, 2);
  assert.equal(b?.sharedAcross, 2);
  assert.deepEqual(b?.five, a?.five, 'the skipped place shows the figures of the one usage read');
  assert.deepEqual(b?.week, a?.week);
  assert.equal(b?.span, null);
  assert.equal(c?.sharedAcross, null, 'two accounts with equal numbers are not one');
  assert.notEqual(c?.accountKey, a?.accountKey);
  assert.equal(a?.accountKey, b?.accountKey);
});

test('E2S2-T6: an account that is not known is never shared; every row carries its place and tool, a Claude row its accountKey', () => {
  const memory = memoryOf(
    ['a', seen(reading({ claude: claude({ email: '', org: null, usage: usageOf(10, 10, 10) }), codex: codexOf() }), 3)],
    ['b', seen(reading({ claude: claude({ email: '', org: null, usage: usageOf(10, 10, 10) }) }), 2)],
  );
  const model = tableModel(inputOf({ hosts: ['a', 'b'], memory }));

  for (const block of model.blocks) {
    for (const row of block.rows) {
      assert.equal(row.place, block.host);
      assert.ok(['claude-code', 'codex', 'agy'].includes(row.tool));
    }
  }
  const [a, b] = ['a', 'b'].map((host) => blockFor(model, host).rows[0]);
  assert.equal(a?.accountKey, null);
  assert.equal(b?.accountKey, null);
  assert.equal(a?.sharedAcross, null);
  assert.equal(a?.account.text, 'account not known');
  assert.equal(blockFor(model, 'a').rows[1]?.accountKey, null, 'a Codex row has no Claude key');
});

test('E2S2-T6: the same plan text on two accounts, or the same email in two organizations, is not one account', () => {
  const memory = memoryOf(
    ['a', seen(reading({ claude: claude({ org: 'One', usage: usageOf(1, 1, 1) }) }), 3)],
    ['b', seen(reading({ claude: claude({ org: 'Two', usage: usageOf(1, 1, 1) }) }), 2)],
  );
  const model = tableModel(inputOf({ hosts: ['a', 'b'], memory }));

  assert.notEqual(blockFor(model, 'a').rows[0]?.accountKey, blockFor(model, 'b').rows[0]?.accountKey);
  assert.equal(blockFor(model, 'a').rows[0]?.sharedAcross, null);
});

// ───────────────── T7 ─────────────────

test('E2S2-T7: every reason spans the two cells with its own sentence, and no tool row has two empty cells without one', () => {
  const memory = memoryOf(
    ['off', seen(reading({ claude: claude({ usage: { state: 'skipped' } }), codex: codexOf(codexUsage({ windows: [] })) }), 1)],
    ['fmt', seen(reading({ claude: claude({ email: 'fmt@example.test', usage: { state: 'failed', why: 'format' } }), agy: agyOf() }), 1)],
    ['out', seen(reading({ claude: { state: 'signed-out' } }), 1)],
    ['cdx', seen(reading({ claude: { state: 'unreadable', why: 'timeout' }, codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'failed', why: 'none' } } }), 1)],
    ['cred', seen(reading({ claude: { state: 'unreadable', why: 'credential-shape' }, codex: { state: 'unreadable', why: 'credential-shape' }, agy: { state: 'unreadable', why: 'credential-shape' } }), 1)],
  );
  const model = tableModel(inputOf({ hosts: ['off', 'fmt', 'out', 'cdx', 'cred'], memory }));

  assert.equal(blockFor(model, 'off').rows[0]?.span, 'Usage not read: it was not asked for in this round');
  assert.match(blockFor(model, 'fmt').rows[0]?.span ?? '', /^Usage unavailable: Claude Code 2\.1\.5 printed no usage line this version understands$/u);
  assert.equal(blockFor(model, 'fmt').rows[1]?.span, 'agy states no usage');
  assert.equal(blockFor(model, 'out').rows[0]?.span, 'signed out');
  assert.match(blockFor(model, 'cdx').rows[1]?.span ?? '', /no usage record is near the end/u);
  for (const block of model.blocks) {
    for (const row of block.rows) {
      const empty = row.five.left === null && row.week.left === null && row.five.resetsAt === null && row.week.resetsAt === null;
      assert.ok(!empty || row.span !== null || row.models.length > 0 || row.others.length > 0, `${row.place} ${row.tool} has two empty cells and no reason`);
    }
  }
  assert.equal(blockFor(model, 'cred').rows.length, 3);
});

// ───────────────── T8 ─────────────────

test('E2S2-T8: a failed host keeps its older table with that read\'s instant, marked older; its failure sentence is whole', () => {
  const memory = memoryOf(['h', { failure: { sentence: 'x'.repeat(150), atMs: NOW - min(3) }, ...seen(reading({ claude: claude({ usage: usageOf(1, 2, 3) }) }), 25) }]);
  const block = blockFor(tableModel(inputOf({ hosts: ['h'], memory })), 'h');

  assert.equal(block.head.kind, 'failed');
  assert.equal(block.olderAt, NOW - min(25));
  assert.equal(block.rows[0]?.readAt, NOW - min(25));
  assert.equal(block.rows[0]?.five.left, 99);
  assert.equal(block.head.said, `the last read failed: ${'x'.repeat(150)}`);
});

test('E2S2-T8: a failure with no older table has no rows and no older instant', () => {
  const block = blockFor(tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', { failure: { sentence: '', atMs: NOW } }]) })), 'h');

  assert.equal(block.olderAt, null);
  assert.deepEqual(block.rows, []);
  assert.equal(block.head.said, 'the last read failed');
});

// ───────────────── T9 ─────────────────

test('E2S2-T9: the toolbar — the last round\'s end and k of n, the running round, the permission and the Stop', () => {
  const ended = tableModel(inputOf({ round: { kind: 'ended', endedMs: NOW - min(3), read: 14, asked: 16 }, stopped: true }));
  assert.deepEqual(ended.toolbar, { stopped: true, round: { kind: 'ended', at: NOW - min(3), read: 14, asked: 16 } });
  assert.deepEqual(ended.permission, { kind: 'given', at: Date.parse('2026-11-26T12:00:00.000Z'), utc: '2026-11-26T12:00:00.000Z' });

  const running = tableModel(inputOf({ round: { kind: 'running', host: 'host-c', index: 5, total: 16 }, permission: 'none' }));
  assert.deepEqual(running.toolbar.round, { kind: 'running', host: 'host-c', index: 5, total: 16 });
  assert.equal(running.permission.kind, 'none');
  assert.equal(tableModel(inputOf({ permission: 'waiting' })).permission.kind, 'waiting');
  assert.equal(tableModel(inputOf()).toolbar.round.kind, 'idle');
});

// ───────────────── T10 ─────────────────

test('E2S2-T10: the jq case carries the manager word only, and no text of a reply is part of any command', () => {
  const MARKER = 'REPLYTEXTMARKER';
  const commands = Object.values(INSTALL_JQ).flatMap((one) => (one === undefined ? [] : [one]));
  for (const word of MANAGER_WORDS) {
    const memory = memoryOf(['h', seen(reading({ jqMissing: word, claude: claude({ email: `${MARKER}@example.test`, org: MARKER, plan: MARKER, version: '9.9' }) }), 1)]);
    const block = blockFor(tableModel(inputOf({ hosts: ['h'], memory })), 'h');
    const text = JSON.stringify(block);

    assert.equal(block.head.kind, 'jq-missing');
    assert.equal(block.head.manager, word);
    for (const command of commands) {
      assert.ok(!text.includes(command), `the model holds the command "${command}"`);
      assert.ok(!command.includes(MARKER), 'a command holds a text of a reply');
    }
  }
});

test('E2S2-T10: a host with no manager says so, with no command', () => {
  const none: ManagerWord = 'none';
  const block = blockFor(tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ jqMissing: none }), 1)]) })), 'h');

  assert.equal(block.head.manager, 'none');
  assert.match(block.head.said, /Install `jq` on this host/u);
});

// ───────────────── T11 ─────────────────

test('E2S2-T11: every text of a VM went through forList once — controls out, cut at its length, nothing becomes markup', () => {
  const hostile = '<img src=x onerror=alert(1)>$(account)\u0007​';
  const memory = memoryOf(['h', {
    ...seen(reading({ claude: claude({ email: hostile, org: 'o'.repeat(10_000), plan: 'team', version: '1.2.3', usage: { state: 'read', unmatched: 0, windows: [{ name: { model: hostile + 'm'.repeat(500) }, usedPercent: 1, resetsAtMs: null }] } }), codex: codexOf() }), 3),
    failure: { sentence: `${hostile}${'f'.repeat(10_000)}`, atMs: NOW },
  }]);
  const model = tableModel(inputOf({ hosts: ['h'], memory }));
  const text = JSON.stringify(model);

  assert.ok(!text.includes('\u0007') && !text.includes('\\u0007'), 'a control character reached the model');
  assert.ok(!text.includes('​'), 'a format character reached the model');
  assert.ok(!text.includes('$('), 'a `$(` reached the model unbroken');
  assert.ok(text.includes('$ (account)'));
  const block = blockFor(model, 'h');
  assert.ok(block.head.said.length < 260, 'the failure sentence was not cut');
  assert.ok([...(block.rows[0]?.account.text ?? '')].length <= 254 + 3 + 60, 'the account text was not cut');
  assert.ok([...(block.rows[0]?.models[0]?.name ?? '')].length <= 60);
  assert.ok(text.includes('<img src=x onerror=alert(1)>'.slice(0, 5)), 'it arrives as plain text — the page draws it by textContent');
  assert.ok(text.length < 4_000, `10 KB texts arrived whole: ${text.length}`);
});

// ───────────────── T12 ─────────────────

test('E2S2-T12: wsl has no rows in E2, and says the sentence of each platform', () => {
  const memory = memoryOf(['wsl', seen(reading({ claude: claude() }), 1)]);
  const windows = blockFor(tableModel(inputOf({ hosts: ['wsl', 'h'], memory, windows: true })), 'wsl');
  const other = blockFor(tableModel(inputOf({ hosts: ['wsl'], memory, windows: false })), 'wsl');

  assert.deepEqual(windows.rows, []);
  assert.equal(windows.head.said, WSL_WINDOWS_SAID);
  assert.equal(other.head.said, WSL_ELSEWHERE_SAID);
  assert.equal(windows.canRefuse, false);
  assert.equal(windows.head.kind, 'wsl');
});

// ───────────────── T13 ─────────────────

test('E2S2-T13: the model survives JSON, blockOf equals the block in it, and the same input gives the same model', () => {
  const memory = memoryOf(['a', seen(reading({ claude: claude({ usage: usageOf(1, 2, 3) }), codex: codexOf(), agy: agyOf() }), 1)], ['b', { failure: { sentence: 's', atMs: NOW } }]);
  const input = inputOf({ hosts: ['a', 'b', 'wsl'], memory, round: { kind: 'ended', endedMs: NOW, read: 1, asked: 2 } });
  const model = tableModel(input);

  assert.deepEqual(JSON.parse(JSON.stringify(model)), model);
  assert.deepEqual(tableModel(input), model);
  for (const block of model.blocks) {
    assert.deepEqual(blockOf(model, block.host), block);
  }
  assert.equal(blockOf(model, 'nobody'), undefined);
});

test('E2S2-T13: changedBlocks names the landed place and the place whose shared figures moved with it', () => {
  const first = tableModel(inputOf({ hosts: ['a', 'b'], memory: memoryOf(['a', seen(reading({ claude: claude({ usage: usageOf(1, 1, 1) }) }), 5)]) }));
  const memory = memoryOf(['a', seen(reading({ claude: claude({ usage: usageOf(1, 1, 1) }) }), 5)], ['b', seen(reading({ claude: claude() }), 1)]);
  const next = tableModel(inputOf({ hosts: ['a', 'b'], memory }));

  assert.deepEqual(changedBlocks(first, next).map((one) => one.host), ['a', 'b'], 'a joined the account b landed in: its "shared across" moved');
  assert.deepEqual(changedBlocks(next, next), []);
  assert.equal(changedBlocks(undefined, next).length, 2);
  assert.equal(ANN.length > 0, true);
});

test('E2S2-F1: a host of 250 characters is a block under its WHOLE name; its drawn name is cut, and two hosts that share 200 characters are two blocks', () => {
  const long = (tail: string): string => `${'a'.repeat(240)}${tail}`;
  const one = long('0123456789');
  const two = long('9876543210');
  const model = tableModel(inputOf({ hosts: [one, two], memory: memoryOf([one, seen(reading({ claude: claude() }), 1)], [two, seen(reading({ claude: claude() }), 1)]) }));

  assert.equal(model.blocks.length, 2, 'two long hosts became one block');
  assert.deepEqual(model.blocks.map((block) => block.host), [one, two].sort(), 'a block is not named by the whole host');
  assert.ok(model.blocks.every((block) => [...block.shown].length <= 200 && block.shown !== block.host && block.host.startsWith(block.shown.replace(/…$/u, ''))), 'the drawn name is not cut');
  assert.ok(model.blocks.every((block) => block.rows.every((row) => row.place === block.host)), 'a row names the cut host');
  assert.equal(blockOf(model, one)?.host, one);
});
