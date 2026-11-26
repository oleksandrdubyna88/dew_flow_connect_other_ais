import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { safeText, VENDOR_KEY_SHAPE } from '../notifications';
import {
  ACCOUNTS_GRAMMAR, ACCOUNTS_LINES_MAX, ACCOUNTS_LINE_BYTES_MAX, ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_UNREADABLE_SAID, AccountsRead,
  MANAGER_WORDS, credentialShaped, plainValue, readAccounts, withoutControls,
} from '../remoteAccountsReply';
import { AccountsMode } from '../remoteAccountsScript';
import { sourceOf } from './sourceReading';
import {
  ACCOUNT_LINES, FILE_TIME, GOOD, GOOD_LINES, LINES, NOW, RECORD_TIME, USAGE_GOOD, USAGE_GOOD_LINES, USAGE_READ_LINES, claudeOnlyOf, frame, ok, readOf,
} from './accountsFrames';

/**
 * The accounts parser (E1.S2, spec § A2.11.4): the closed vocabulary, each fault by its own word, no secret-shaped value, absent is
 * not zero, one arm for each outcome, the mode and the `COAI-JQ` line. `GOOD` is asserted ONCE to be read, so every negative row
 * changes one thing of a reply the parser accepts.
 */

const MODE: AccountsMode = 'no-claude-usage';
const USAGE_MODE: AccountsMode = 'claude-usage';
const run = (stdout: string, mode: AccountsMode = MODE): AccountsRead => readAccounts(ok(stdout), mode, NOW);
const text = (...lines: readonly string[]): string => frame(...lines);

/** The unreadable words the tests of this file reached: the last test compares it to `ACCOUNTS_UNREADABLE_SAID`. */
const reached = new Set<string>();

function unreadableWith(answer: AccountsRead, why: string, what: string): void {
  assert.deepEqual(answer, { kind: 'unreadable', why }, what);
  reached.add(why);
}

const goodRead = (): Extract<AccountsRead, { kind: 'read' }> => readOf(run(GOOD));

test('A2-T15: GOOD is read — Claude found with three windows, Codex read, agy found', () => {
  const answer = readOf(run(GOOD));
  assert.equal(answer.claude.state, 'found');
  const usage = answer.claude.state === 'found' ? answer.claude.usage : undefined;
  assert.equal(usage?.state, 'skipped');
  assert.equal(answer.codex.state === 'found' && answer.codex.usage.state, 'read');
  assert.equal(answer.agy.state, 'found');
  assert.equal(answer.jqMissing, undefined);
});

test('A2-T15 / E3S2-T16: USAGE_GOOD is read by the usage mode — Claude found with three windows, and nothing else', () => {
  const answer = claudeOnlyOf(run(USAGE_GOOD, USAGE_MODE));
  assert.equal(answer.claude.state, 'found');
  const usage = answer.claude.state === 'found' ? answer.claude.usage : undefined;
  assert.equal(usage?.state, 'read');
  assert.equal(usage?.state === 'read' && usage.windows.length, 3);
  assert.equal(answer.jqMissing, undefined);
  assert.deepEqual(Object.keys(answer).sort(), ['claude', 'jqMissing', 'kind']);
});

// ───────────────── the frame built from the grammar: every marker, every key ─────────────────

const SAMPLE: Readonly<Record<string, string>> = {
  version: '2.1.326',
  email: 'someone@example.com',
  org: 'Example Org',
  plan: 'team',
  turns: '0',
  line: LINES[0] as string,
  unmatched: '0',
  'sign-in-file': 'present',
  window: '14 10080 1795518850',
  'as-of': `${RECORD_TIME} ${FILE_TIME}`,
  path: '/home/a/.local/bin',
};

const TEXT_KEYS = ['email', 'org', 'plan', 'version', 'line'];

const sampleOf = (key: string): string => {
  const sample = SAMPLE[key];
  assert.ok(sample !== undefined, `the grammar has a key "${key}" and this test has no sample for it`);

  return sample;
};

interface Parts { jq: readonly string[]; claude: readonly string[]; codex: readonly string[]; agy: readonly string[] }
const DEFAULT: Parts = { jq: GOOD_LINES.slice(0, 1), claude: GOOD_LINES.slice(1, 7), codex: GOOD_LINES.slice(7, 13), agy: GOOD_LINES.slice(13) };
const USAGE_AFTER: Readonly<Record<string, readonly string[]>> = {
  claude: ['COAI-USAGE claude skipped'],
  codex: GOOD_LINES.slice(10, 13),
  agy: ['COAI-USAGE agy none'],
};
/** The rows of the usage mode: a reply with the Claude part and nothing else. */
const USAGE_ROWS = ['COAI-USAGE claude read', 'COAI-USAGE claude failed timeout', 'COAI-USAGE claude failed error', 'COAI-USAGE claude skipped account-changed'];
const FOUND_DATA: Readonly<Record<string, readonly string[]>> = { claude: [], codex: [':sign-in-file present'], agy: [':sign-in-file present'] };

const frameOf = (parts: Parts): string => text(...parts.jq, ...parts.claude, ...parts.codex, ...parts.agy);
const required = (row: string): readonly string[] => Object.entries(ACCOUNTS_GRAMMAR[row] ?? {}).filter(([, [min]]) => min > 0).map(([key]) => `:${key} ${sampleOf(key)}`);

/** The parts of a reply in which the marker line `row` stands in its place, with these data lines under it. */
function placed(row: string, data: readonly string[]): { readonly parts: Parts; readonly mode: AccountsMode } {
  const [kind, vendor = '', state = ''] = row.split(' ');
  if (kind === 'COAI-JQ') {
    const missing = vendor === 'missing';
    return { parts: { ...DEFAULT, jq: [row], claude: missing ? ['COAI-VENDOR claude absent'] : DEFAULT.claude, codex: missing ? ['COAI-VENDOR codex absent'] : DEFAULT.codex }, mode: MODE };
  }
  if (kind === 'COAI-BAD') {
    return { parts: { jq: [row, ...data], claude: [], codex: [], agy: [] }, mode: MODE };
  }
  if (USAGE_ROWS.includes(row)) {
    return { parts: { ...DEFAULT, claude: [...GOOD_LINES.slice(1, 6), row, ...data], codex: [], agy: [] }, mode: USAGE_MODE };
  }
  const block = kind === 'COAI-VENDOR'
    ? [row, ...data, ...(state === 'found' ? USAGE_AFTER[vendor] ?? [] : [])]
    : [`COAI-VENDOR ${vendor} found`, ...(FOUND_DATA[vendor] ?? []), row, ...data];

  // A vendor that could not run `jq` goes with a `COAI-JQ missing` line, and then the other vendor of the two that need it is absent.
  const noJq = row.endsWith('no-jq') ? { jq: ['COAI-JQ missing apt-get'], claude: ['COAI-VENDOR claude absent'], codex: ['COAI-VENDOR codex absent'] } : {};

  return { parts: { ...DEFAULT, ...noJq, [vendor]: block }, mode: MODE };
}

const rows = Object.keys(ACCOUNTS_GRAMMAR);

test('A2-T15 derived: every marker the grammar knows, in its place, with its required keys, is read', () => {
  assert.ok(rows.length >= 34, `the grammar has ${rows.length} rows`);
  for (const row of rows) {
    const { parts, mode } = placed(row, required(row));
    const answer = run(frameOf(parts), mode);
    if (row.startsWith('COAI-BAD')) {
      assert.equal(answer.kind, 'unreadable', row);
      assert.ok(answer.kind === 'unreadable' && ['operands', 'home', 'folder', 'tool', 'unsafe'].includes(answer.why), row);
    } else {
      assert.equal(answer.kind, mode === USAGE_MODE ? 'claude-only' : 'read', `${row}: ${JSON.stringify(answer)}`);
    }
  }
});

test('A2-T15 derived: every key of every marker, alone, is read', () => {
  let keys = 0;
  for (const [row, spec] of Object.entries(ACCOUNTS_GRAMMAR)) {
    for (const key of Object.keys(spec)) {
      keys += 1;
      const data = [...required(row).filter((line) => !line.startsWith(`:${key} `)), `:${key} ${sampleOf(key)}`];
      const { parts, mode } = placed(row, data);
      const answer = run(frameOf(parts), mode);
      assert.equal(answer.kind, row.startsWith('COAI-BAD') ? 'unreadable' : mode === USAGE_MODE ? 'claude-only' : 'read', `${row} ${key}: ${JSON.stringify(answer)}`);
    }
  }
  assert.ok(keys >= 16, `the grammar has ${keys} keys in all`);
});

test('A2-T15: each fault by its own word', () => {
  const swap = (from: string, to: string): readonly string[] => GOOD_LINES.map((line) => (line === from ? to : line));
  unreadableWith(readAccounts({ code: 3, stdout: GOOD }, MODE, NOW), 'exit', 'code 3');
  unreadableWith(run(GOOD.replace('COAI-ACCOUNTS 1\n', '')), 'no-sentinel', 'no begin line');
  unreadableWith(run(GOOD.replace('COAI-ACCOUNTS 1\n', 'COAI-ACCOUNTS 2\n')), 'version', 'another version');
  unreadableWith(run(GOOD.replace('COAI-ACCOUNTS-END\n', '')), 'cut', 'no end line');
  unreadableWith(run(text(...GOOD_LINES, ...Array.from({ length: 48 }, () => ':org x'))), 'too-many', 'a 65th line');
  unreadableWith(run(text(...GOOD_LINES.slice(0, 4), `:org ${'é'.repeat(254)}`, ...GOOD_LINES.slice(4))), 'too-long', '513 bytes made of 259 characters');
  assert.equal(run(text(...GOOD_LINES.slice(0, 3), `:org ${'é'.repeat(253)}`, ...GOOD_LINES.slice(4))).kind, 'read', '511 bytes are read');
  unreadableWith(run(text(...GOOD_LINES, 'hello')), 'line', 'a line that is neither');
  unreadableWith(run(text(':org x', ...GOOD_LINES)), 'line', 'data before any marker');
  unreadableWith(run(text(...GOOD_LINES, 'COAI-NEW x')), 'marker', 'an unknown marker');
  unreadableWith(run(text(...swap('COAI-VENDOR agy found', 'COAI-VENDOR claude maybe'))), 'marker', 'a word the marker does not take');
  unreadableWith(run(text(...swap('COAI-VENDOR agy found', 'COAI-VENDOR gemini found'))), 'marker', 'an unknown vendor');
  unreadableWith(run(text(...swap(':org Example Org', ':token x'))), 'key', 'an unknown key');
  const swapUsage = (from: string, to: string): readonly string[] => USAGE_GOOD_LINES.map((line) => (line === from ? to : line));
  unreadableWith(run(text(...swapUsage(':turns 0', ':email x')), USAGE_MODE), 'key', ':email under a usage marker');
  unreadableWith(run(text(...swap(':org Example Org', ':email x'))), 'key', ':email twice');
  const at = USAGE_GOOD_LINES.indexOf(`:line ${LINES[0]}`);
  unreadableWith(run(text(...USAGE_GOOD_LINES.slice(0, at), ...Array.from({ length: 6 }, () => `:line ${LINES[0]}`), ...USAGE_GOOD_LINES.slice(at)), USAGE_MODE), 'key', 'nine :line');
  for (const [from, to] of [[':sign-in-file present', ':sign-in-file maybe'],
    [':window 14 10080 1795518850', ':window 1 2'], [':as-of 2026-11-17T19:32:36.474Z 1794938000', ':as-of 1'], [':as-of 2026-11-17T19:32:36.474Z 1794938000', ':as-of - soon']] as const) {
    const lines = GOOD_LINES.map((line) => (line === from ? to : line));
    unreadableWith(run(text(...lines)), 'value', to);
  }
  for (const [from, to] of [[':turns 0', ':turns x'], [':turns 0', ':turns 1234567'], [':unmatched 0', ':unmatched -1']] as const) {
    unreadableWith(run(text(...swapUsage(from, to)), USAGE_MODE), 'value', to);
  }
});

test('A2-T15: the grammar is the one table the checks walk, and the caps are exported', () => {
  assert.equal(ACCOUNTS_LINES_MAX, 64);
  assert.equal(ACCOUNTS_LINE_BYTES_MAX, 512);
  assert.equal(ACCOUNTS_REPLY_BYTES_MAX, 33858);
  assert.deepEqual([...MANAGER_WORDS], ['apt-get', 'dnf', 'yum', 'apk', 'none']);
});

// ───────────────── A2-T16: no secret-shaped value passes ─────────────────

const TOKENS: readonly string[] = [
  `sk-ant-oat01-${'MARK'.repeat(15)}`,
  'M4RK'.repeat(10),
  `eyJ${'a'.repeat(20)}.MARK.MARK`,
  `ya29.${'MARK'.repeat(5)}`,
];

/** The console calls a function makes, counted. */
function consoleCalls(body: () => void): number {
  const names = ['log', 'info', 'warn', 'error', 'debug'] as const;
  const originals = names.map((name) => console[name]);
  let calls = 0;
  for (const name of names) {
    console[name] = (): void => {
      calls += 1;
    };
  }
  try {
    body();
  } finally {
    names.forEach((name, index) => {
      console[name] = originals[index] as typeof console.log;
    });
  }

  return calls;
}

const leaks = (answer: AccountsRead): boolean => /MARK|M4RK/u.test(JSON.stringify(answer));
const vendorOf = (row: string): 'claude' | 'codex' | 'agy' => row.split(' ')[1] as 'claude' | 'codex' | 'agy';

test('A2-T16: the marker-token test — no token in any value of any marker reaches the typed result', () => {
  let cases = 0;
  for (const token of TOKENS) {
    for (const [row, spec] of Object.entries(ACCOUNTS_GRAMMAR)) {
      for (const key of Object.keys(spec)) {
        cases += 1;
        const data = [...required(row).filter((line) => !line.startsWith(`:${key} `)), `:${key} ${token}`];
        const { parts, mode } = placed(row, data);
        let answer: AccountsRead | undefined;
        assert.equal(consoleCalls(() => { answer = run(frameOf(parts), mode); }), 0, `${row} ${key}: the parser wrote to the console`);
        assert.ok(answer !== undefined && !leaks(answer), `${row} ${key}: a token is in the typed result`);
        if (TEXT_KEYS.includes(key)) {
          const got = answer as AccountsRead;
          const sides = got.kind === 'claude-only' ? { claude: got.claude } : readOf(got);
          assert.deepEqual((sides as Record<string, unknown>)[vendorOf(row)], { state: 'unreadable', why: 'credential-shape' }, `${row} ${key}: the vendor is unreadable`);
          for (const other of ['claude', 'codex', 'agy'] as const) {
            if (other !== vendorOf(row) && other in sides) {
              assert.deepEqual((sides as Record<string, unknown>)[other], goodRead()[other], `${row} ${key}: the ${other} side must stand`);
            }
          }
        } else {
          reached.add('value');
          assert.deepEqual(answer, { kind: 'unreadable', why: 'value' }, `${row} ${key}: a closed key holds a token`);
        }
      }
    }
  }
  assert.ok(cases >= 4 * 13, `the derived walk ran ${cases} cases`);
});

test('A2-T16: a token as the record\'s time before a file time makes Codex unreadable and nothing else', () => {
  for (const token of TOKENS) {
    const answer = run(text(...GOOD_LINES.map((line) => (line.startsWith(':as-of ') ? `:as-of ${token} ${FILE_TIME}` : line))));
    assert.ok(!leaks(answer));
    assert.deepEqual(readOf(answer).codex, { state: 'unreadable', why: 'credential-shape' });
    assert.deepEqual(readOf(answer).claude, goodRead().claude);
    assert.deepEqual(readOf(answer).agy, goodRead().agy);
  }
});

test('A2-T16: a token as one word of a marker is a marker the grammar does not know', () => {
  for (const token of TOKENS) {
    for (const row of rows) {
      const words = row.split(' ');
      const bad = [...words.slice(0, -1), token].join(' ');
      const { parts, mode } = placed(row, []);
      const lines = frameOf(parts).split('\n').map((line) => (line === row ? bad : line)).join('\n');
      const answer = run(lines, mode);
      assert.ok(!leaks(answer), `${row}: a token is in the answer`);
      assert.deepEqual(answer, { kind: 'unreadable', why: 'marker' }, row);
    }
  }
});

test('A2-T16 companion: an email and an organization are given back as they are', () => {
  const claude = goodRead().claude;
  assert.ok(claude.state === 'found' && claude.email === 'someone@example.com' && claude.org === 'Example Org');
});

test('A2-T16: the values are plain — controls out, $( broken, the length cut', () => {
  const bell = String.fromCodePoint(7);
  const esc = String.fromCodePoint(27);
  const rlo = String.fromCodePoint(0x202e);
  const zwsp = String.fromCodePoint(0x200b);
  const orgOf = (value: string): string | null => {
    const claude = readOf(run(text(...GOOD_LINES.map((line) => (line.startsWith(':org ') ? `:org ${value}` : line))))).claude;

    return claude.state === 'found' ? claude.org : 'NOT FOUND';
  };
  assert.equal(orgOf(`A${bell}B${esc}C${rlo}D${zwsp}E`), 'ABCDE');
  assert.equal(orgOf('Team $(zap)'), 'Team $ (zap)');
  assert.equal(orgOf('ab '.repeat(100))?.length, 200);
  unreadableWith(run(text(...GOOD_LINES.slice(0, 3), `:org ${'a b '.repeat(2500)}`, ...GOOD_LINES.slice(4))), 'too-long', 'a value of 10 KB');
  const pathOf = (value: string): string | null | undefined => {
    const answer = run(text('COAI-JQ found', 'COAI-VENDOR claude unsafe', `:path ${value}`, ...GOOD_LINES.slice(7)));
    const claude = readOf(answer).claude;

    return claude.state === 'unsafe' ? claude.path : undefined;
  };
  assert.equal(pathOf('/home/a/.local/bin'), '/home/a/.local/bin');
  assert.equal(pathOf('-'), null);
});

test('A2-T16: percents 101, -1, 1e3 and NaN in a :window are not known, and the window is kept', () => {
  for (const used of ['101', '-1', '1e3', 'NaN']) {
    const codex = readOf(run(text(...GOOD_LINES.map((line) => (line.startsWith(':window ') ? `:window ${used} 10080 1795518850` : line))))).codex;
    assert.ok(codex.state === 'found' && codex.usage.state === 'read', used);
    const windows = codex.state === 'found' && codex.usage.state === 'read' ? codex.usage.windows : [];
    assert.deepEqual(windows.map((one) => one.usedPercent), [null], used);
  }
});

test('A2-T16: a manager word outside the list is a marker the parser does not know', () => {
  for (const word of ['brew', 'apt-get;id', 'APT-GET']) {
    const lines = GOOD_LINES.map((line) => (line === 'COAI-JQ found' ? `COAI-JQ missing ${word}` : line));
    assert.deepEqual(run(text(...lines)), { kind: 'unreadable', why: 'marker' }, word);
  }
});

// ───────────────── A2-T17: absent is not zero ─────────────────

/** The numbers of a result: every number leaf with the path that holds it, except a count (`unmatched`). */
function numbers(value: unknown, path = ''): readonly (readonly [string, number])[] {
  if (typeof value === 'number') {
    return [[path, value]];
  }
  if (typeof value !== 'object' || value === null) {
    return [];
  }

  return Object.entries(value).flatMap(([key, inner]) => (key === 'unmatched' ? [] : numbers(inner, `${path}.${key}`)));
}

test('A2-T17: each number of GOOD taken out is null, and no field of the result is 0', () => {
  const without = (match: (line: string) => boolean, replace?: string): AccountsRead => run(text(...GOOD_LINES.flatMap((line) => (match(line) ? (replace === undefined ? [] : [replace]) : [line]))));
  const variants: readonly (readonly [string, AccountsRead])[] = [
    ['no :window', without((line) => line.startsWith(':window '))],
    ['- for used', without((line) => line.startsWith(':window '), ':window - 10080 1795518850')],
    ['- for reset', without((line) => line.startsWith(':window '), ':window 14 10080 -')],
    ['no reset zone', run(text(...USAGE_GOOD_LINES.flatMap((line) => (line.startsWith(':line Current session') ? [`:line Current session: 56% used · resets Nov 19, 3pm`] : [line]))), USAGE_MODE)],
    ['no :as-of time', without((line) => line.startsWith(':as-of '), ':as-of - -')],
    ['no :version', without((line) => line === ':version 2.1.326')],
  ];
  for (const [name, answer] of variants) {
    const answered = answer.kind === 'claude-only' ? answer : readOf(answer);
    assert.deepEqual(numbers(answered).filter(([, value]) => value === 0), [], `${name}: a field of the result is 0`);
  }
  const used = readOf(variants[1]?.[1] as AccountsRead).codex;
  assert.ok(used.state === 'found' && used.usage.state === 'read' && used.usage.windows[0]?.usedPercent === null);
  const reset = readOf(variants[2]?.[1] as AccountsRead).codex;
  assert.ok(reset.state === 'found' && reset.usage.state === 'read' && reset.usage.windows[0]?.resetsAtMs === null);
  const asOf = readOf(variants[4]?.[1] as AccountsRead).codex;
  assert.ok(asOf.state === 'found' && asOf.usage.state === 'read' && asOf.usage.asOf === null);
  const version = readOf(variants[5]?.[1] as AccountsRead).claude;
  assert.ok(version.state === 'found' && version.version === null);
  const zone = claudeOnlyOf(variants[3]?.[1] as AccountsRead).claude;
  assert.ok(zone.state === 'found' && zone.usage.state === 'read' && zone.usage.windows.length === 2 && zone.usage.unmatched === 1);
});

/** A seeded generator, so a failure can be run again. */
function seeded(seed: number): () => number {
  let state = seed;

  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;

    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

test('A2-T17: 300 replies from a seeded generator — the result equals the one built from the model, null wherever the model has no number', () => {
  const next = seeded(20261119);
  const pick = <T>(items: readonly T[]): T => items[Math.floor(next() * items.length)] as T;
  for (let n = 0; n < 300; n += 1) {
    const used = pick(['-', '0', '49.5', '100']);
    const reset = pick(['-', '1795518850']);
    const minutes = pick(['300', '10080']);
    const recordTime = pick(['-', RECORD_TIME]);
    const fileTime = pick(['-', String(FILE_TIME)]);
    const version = pick(['', ':version 1.5.12']);
    const lines = GOOD_LINES.flatMap((line) => {
      if (line.startsWith(':window ')) {
        return [`:window ${used} ${minutes} ${reset}`];
      }
      if (line.startsWith(':as-of ')) {
        return [`:as-of ${recordTime} ${fileTime}`];
      }

      return version === '' && line === ':version 1.5.12' ? [] : [line];
    });
    const codex = readOf(run(text(...lines))).codex;
    const agy = readOf(run(text(...lines))).agy;
    const windows = [{ minutes: Number(minutes), usedPercent: used === '-' ? null : Number(used), resetsAtMs: reset === '-' ? null : Number(reset) * 1000 }];
    const asOf = recordTime !== '-'
      ? { ms: Date.UTC(2026, 10, 17, 19, 32, 36, 474), from: 'record' }
      : (fileTime === '-' ? null : { ms: FILE_TIME * 1000, from: 'file' });
    assert.deepEqual(codex, { state: 'found', signInFile: true, plan: 'prolite', usage: { state: 'read', windows, asOf, statMissing: false } }, `case ${n}`);
    assert.deepEqual(agy, { state: 'found', version: version === '' ? null : '1.5.12', signInFile: true, usage: { state: 'no-source' } }, `case ${n}`);
  }
});

const PARSER_FILES = ['remoteAccountsReply.ts', 'remoteAccountsUsage.ts'];
const zeroFallbacks = (source: string): readonly string[] => [...source.matchAll(/\?\? 0|\|\| 0|100 -/gu)].map((one) => one[0]);

test('A2-T17: the parser files hold no ?? 0, no || 0 and no 100 -, and the scan finds them in a sample', () => {
  for (const file of PARSER_FILES) {
    assert.deepEqual(zeroFallbacks(sourceOf(file)), [], `${file} turns an absent number into 0, or makes "left"`);
  }
  assert.deepEqual(zeroFallbacks('a ?? 0; b || 0; 100 - used'), ['?? 0', '|| 0', '100 -']);
});

// ───────────────── A2-T20: one arm for each outcome, and the parser never throws ─────────────────

const swapJq = (jq: string, claude: readonly string[], codex: readonly string[]): string => text(jq, ...claude, ...codex, ...GOOD_LINES.slice(13));

test('A2-T20: one frame for each outcome, all different, no key holds undefined, four absent-like values differ', () => {
  // The usage mode's replies hold the Claude part and nothing else (E3.S2, § R2.7).
  const claudeUsage = (...lines: readonly string[]): AccountsRead => run(text('COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude ' + lines[0], ...lines.slice(1)), USAGE_MODE);
  const outcomes: Readonly<Record<string, AccountsRead>> = {
    'claude absent': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude absent'], DEFAULT.codex)),
    'claude signed-out': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude signed-out'], DEFAULT.codex)),
    'claude unreadable no-jq': run(swapJq('COAI-JQ missing apt-get', ['COAI-VENDOR claude unreadable no-jq'], ['COAI-VENDOR codex absent'])),
    'claude unreadable auth': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude unreadable auth'], DEFAULT.codex)),
    'claude unreadable timeout': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude unreadable timeout'], DEFAULT.codex)),
    'claude unreadable credential-shape': run(swapJq('COAI-JQ found', [...DEFAULT.claude.slice(0, 1), `:email ${TOKENS[1]}`, ...DEFAULT.claude.slice(5)], DEFAULT.codex)),
    'claude found, usage read': run(USAGE_GOOD, USAGE_MODE),
    'claude found, usage skipped': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude found', 'COAI-USAGE claude skipped'], DEFAULT.codex), 'no-claude-usage'),
    'claude found, usage skipped account-changed': claudeUsage('skipped account-changed'),
    'claude found, usage failed timeout': claudeUsage('failed timeout'),
    'claude found, usage failed error': claudeUsage('failed error'),
    'claude found, usage failed format': claudeUsage('read', ':turns 0', ':unmatched 0'),
    'claude found, usage failed model-call': claudeUsage('read', ':turns 2', `:line ${LINES[0]}`, ':unmatched 0'),
    'claude unsafe': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude unsafe', ':path /home/a/.local/bin'], DEFAULT.codex)),
    'claude unsafe, no path': run(swapJq('COAI-JQ found', ['COAI-VENDOR claude unsafe', ':path -'], DEFAULT.codex)),
    'agy unsafe': run(text(...GOOD_LINES.slice(0, 13), 'COAI-VENDOR agy unsafe', ':path /root/.local/bin')),
    'codex absent': run(swapJq('COAI-JQ found', DEFAULT.claude, ['COAI-VENDOR codex absent'])),
    'codex unreadable no-jq': run(swapJq('COAI-JQ missing yum', ['COAI-VENDOR claude absent'], ['COAI-VENDOR codex unreadable no-jq'])),
    'codex unreadable credential-shape': run(swapJq('COAI-JQ found', DEFAULT.claude, ['COAI-VENDOR codex found', ':sign-in-file present', `:plan ${TOKENS[1]}`, ...USAGE_AFTER.codex as readonly string[]])),
    'codex failed no-sessions': run(swapJq('COAI-JQ found', DEFAULT.claude, ['COAI-VENDOR codex found', ':sign-in-file present', 'COAI-USAGE codex failed no-sessions'])),
    'codex failed none': run(swapJq('COAI-JQ found', DEFAULT.claude, ['COAI-VENDOR codex found', ':sign-in-file present', 'COAI-USAGE codex failed none'])),
    'codex failed error': run(swapJq('COAI-JQ found', DEFAULT.claude, ['COAI-VENDOR codex found', ':sign-in-file absent', 'COAI-USAGE codex failed error'])),
    'agy absent': run(text(...GOOD_LINES.slice(0, 13), 'COAI-VENDOR agy absent')),
    'agy unreadable credential-shape': run(text(...GOOD_LINES.slice(0, 13), 'COAI-VENDOR agy found', `:version ${TOKENS[1]}`, ':sign-in-file present', 'COAI-USAGE agy none')),
  };
  const seen = new Map<string, string>();
  for (const [name, answer] of Object.entries(outcomes)) {
    assert.ok(answer.kind === 'read' || answer.kind === 'claude-only', `${name}: ${JSON.stringify(answer)}`);
    const json = JSON.stringify(answer);
    const clash = seen.get(json);
    assert.equal(clash, undefined, `${name} and ${clash ?? ''} are the same value as JSON`);
    seen.set(json, name);
    const arms = answer.kind === 'read' ? [answer.claude, answer.codex, answer.agy] : answer.kind === 'claude-only' ? [answer.claude] : [];
    for (const arm of arms) {
      assert.ok(!hasUndefined(arm), `${name}: an arm holds a key whose value is undefined`);
    }
  }
  const claudeOf = (name: string): unknown => (outcomes[name] as Extract<AccountsRead, { kind: 'read' }>).claude;
  assert.equal(JSON.stringify(claudeOf('claude found, usage skipped')).includes('"usage":{"state":"skipped"}'), true);
  assert.equal(JSON.stringify(claudeOf('claude found, usage skipped account-changed')).includes('"usage":{"state":"skipped","why":"account-changed"}'), true);
  assert.deepEqual(claudeOf('claude unsafe'), { state: 'unsafe', path: '/home/a/.local/bin' });
  assert.deepEqual(claudeOf('claude unsafe, no path'), { state: 'unsafe', path: null });
  assert.deepEqual((outcomes['agy unsafe'] as Extract<AccountsRead, { kind: 'read' }>).agy, { state: 'unsafe', path: '/root/.local/bin' });
  const kinds = new Set([
    JSON.stringify({ state: 'no-source' }),
    JSON.stringify({ state: 'skipped' }),
    JSON.stringify({ state: 'failed', why: 'error' }),
    JSON.stringify({ state: 'unreadable', why: 'auth' }),
  ]);
  assert.equal(kinds.size, 4, 'no usage source, skipped, failed and unreadable are four different values');
});

function hasUndefined(value: unknown): boolean {
  if (value === undefined) {
    return true;
  }

  return typeof value === 'object' && value !== null && Object.values(value).some((inner) => hasUndefined(inner));
}

test('A2-T20: the parser never throws — 1000 inputs from the seeded generator', () => {
  const next = seeded(7);
  const pickLine = (): string => GOOD_LINES[Math.floor(next() * GOOD_LINES.length)] as string;
  const bytes = (n: number): string => Buffer.from(Array.from({ length: n }, () => Math.floor(next() * 256))).toString('utf8');
  const known = new Set(Object.keys(ACCOUNTS_UNREADABLE_SAID));
  for (let n = 0; n < 1000; n += 1) {
    const shape = n % 5;
    const body = ((): string => {
      if (shape === 0) {
        return text(...Array.from({ length: 1 + Math.floor(next() * 30) }, pickLine));
      }
      if (shape === 1) {
        return bytes(Math.floor(next() * 600));
      }
      if (shape === 2) {
        return text(...GOOD_LINES.slice(0, 3), `:org ${bytes(40)}\ud800`, ...GOOD_LINES.slice(4));
      }
      if (shape === 3) {
        return text(...GOOD_LINES.slice(0, 3), `:org ${'x'.repeat(1 << 20)}`);
      }

      return text(...Array.from({ length: 10000 }, pickLine));
    })();
    const answer = readAccounts({ code: n % 7 === 0 ? -1 : 0, stdout: body }, n % 2 === 0 ? 'claude-usage' : 'no-claude-usage', NOW);
    assert.ok(answer.kind === 'read' || answer.kind === 'unreadable' || answer.kind === 'claude-only');
    if (answer.kind === 'unreadable') {
      assert.ok(known.has(answer.why), `${answer.why} is not a key of ACCOUNTS_UNREADABLE_SAID`);
    }
    const json = JSON.stringify(answer);
    for (const at of [0, 100, 300]) {
      const piece = body.slice(at, at + 201);
      assert.ok(piece.length < 201 || !json.includes(piece), 'the typed result holds a piece of the input longer than 200 characters');
    }
  }
});

// ───────────────── A2-T27: the mode and the COAI-JQ line ─────────────────

test('E3S2-T15: the parser by mode — the reply is held to the mode that was SENT', () => {
  unreadableWith(run(GOOD, USAGE_MODE), 'shape', 'a first-mode reply sent as claude-usage');
  unreadableWith(run(USAGE_GOOD, 'no-claude-usage'), 'shape', 'a usage reply sent as no-claude-usage');
  // a usage reply with a Codex or an agy record
  unreadableWith(run(text(...USAGE_GOOD_LINES, ...DEFAULT.codex), USAGE_MODE), 'shape', 'a usage reply with a Codex record');
  unreadableWith(run(text(...USAGE_GOOD_LINES, ...DEFAULT.agy), USAGE_MODE), 'shape', 'a usage reply with an agy record');
  unreadableWith(run(text(...USAGE_GOOD_LINES, 'COAI-VENDOR codex absent', 'COAI-VENDOR agy absent'), USAGE_MODE), 'shape', 'a usage reply with absent Codex and agy records');
  // a plain `skipped` in the usage mode, and `skipped account-changed` in the first mode
  const plainSkipped = ['COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude skipped'];
  unreadableWith(run(text(...plainSkipped), USAGE_MODE), 'shape', 'skipped for claude-usage');
  assert.equal(readOf(run(text(...plainSkipped, ...DEFAULT.codex, ...DEFAULT.agy))).claude.state, 'found');
  const changed = ['COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude skipped account-changed'];
  unreadableWith(run(text(...changed, ...DEFAULT.codex, ...DEFAULT.agy)), 'shape', 'skipped account-changed for no-claude-usage');
  assert.equal(claudeOnlyOf(run(text(...changed), USAGE_MODE)).claude.state, 'found');
  // the usage mode, a usage reply whose part is missing a key
  unreadableWith(run(text(...USAGE_GOOD_LINES.filter((line) => !line.startsWith(':turns '))), USAGE_MODE), 'shape', 'a required key missing: no :turns');
});

test('E3S2-T16: claude-only for each legal usage reply, and `read` is never answered for the usage mode', () => {
  const legal: readonly (readonly string[])[] = [
    ['COAI-JQ found', ...ACCOUNT_LINES, ':version 2.1.326', ...USAGE_READ_LINES],
    ['COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude failed timeout'],
    ['COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude failed error'],
    ['COAI-JQ found', ...ACCOUNT_LINES, 'COAI-USAGE claude skipped account-changed'],
    ['COAI-JQ found', 'COAI-VENDOR claude absent'],
    ['COAI-JQ found', 'COAI-VENDOR claude signed-out'],
    ['COAI-JQ found', 'COAI-VENDOR claude unreadable auth'],
    ['COAI-JQ found', 'COAI-VENDOR claude unreadable timeout'],
    ['COAI-JQ found', 'COAI-VENDOR claude unsafe', ':path /root/.local/bin'],
    ['COAI-JQ missing apt-get', 'COAI-VENDOR claude absent'],
    ['COAI-JQ missing yum', 'COAI-VENDOR claude unreadable no-jq'],
  ];
  for (const lines of legal) {
    const answer = run(text(...lines), USAGE_MODE);
    assert.equal(answer.kind, 'claude-only', `${lines.join(' | ')}: ${JSON.stringify(answer)}`);
    assert.equal(answer.kind === 'claude-only' && answer.jqMissing, lines[0]?.startsWith('COAI-JQ missing') ? lines[0].split(' ')[2] : undefined);
    // the same frame in the first mode is never `claude-only`
    assert.notEqual(run(text(...lines), 'no-claude-usage').kind, 'claude-only');
  }
  // a reply to the first mode is `read` and never `claude-only`
  assert.equal(run(GOOD, 'no-claude-usage').kind, 'read');
  // COAI-BAD is unreadable in both modes, by its own word
  for (const mode of ['no-claude-usage', 'claude-usage'] as const) {
    assert.deepEqual(run(text('COAI-BAD tool'), mode), { kind: 'unreadable', why: 'tool' });
  }
});

test('E3S2-T14: the grammar\'s four rows — `unsafe` with a closed `path` in three places, and `skipped account-changed`', () => {
  for (const row of ['COAI-BAD unsafe', 'COAI-VENDOR claude unsafe', 'COAI-VENDOR agy unsafe']) {
    assert.deepEqual(Object.keys(ACCOUNTS_GRAMMAR[row] ?? {}), ['path'], row);
  }
  assert.deepEqual(ACCOUNTS_GRAMMAR['COAI-USAGE claude skipped account-changed'], {});
  const bad = (path: string | undefined): AccountsRead => run(text('COAI-BAD unsafe', ...(path === undefined ? [] : [`:path ${path}`])));
  // a legal path and the dash
  assert.deepEqual(bad('/home/a/.local/bin'), { kind: 'unreadable', why: 'unsafe', path: '/home/a/.local/bin' });
  assert.deepEqual(bad('-'), { kind: 'unreadable', why: 'unsafe', path: null });
  reached.add('unsafe');
  // a path with a space, of 201 characters, not absolute, with a character outside the class → `value`
  for (const path of ['/a b', `/${'a'.repeat(200)}`, 'relative/path', '/a;b', '/aé', '/a$(x)', '']) {
    unreadableWith(bad(path), 'value', JSON.stringify(path));
  }
  const longest = `/${'abcd/'.repeat(39)}abcd`;
  assert.equal(longest.length, 200);
  assert.deepEqual(bad(longest), { kind: 'unreadable', why: 'unsafe', path: longest }, '200 characters fit');
  // no path, and a path twice
  unreadableWith(run(text('COAI-BAD unsafe')), 'shape', 'no path');
  unreadableWith(run(text('COAI-BAD unsafe', ':path /a', ':path /b')), 'key', 'two paths');
  // `unsafe` in the three places gives the three typed results
  const claude = readOf(run(text('COAI-JQ found', 'COAI-VENDOR claude unsafe', ':path /root/.local/bin', ...DEFAULT.codex, ...DEFAULT.agy))).claude;
  assert.deepEqual(claude, { state: 'unsafe', path: '/root/.local/bin' });
  const agy = readOf(run(text(...GOOD_LINES.slice(0, 13), 'COAI-VENDOR agy unsafe', ':path -'))).agy;
  assert.deepEqual(agy, { state: 'unsafe', path: null });
  // a path in the shape of a credential is not shown, and the vendor stays unsafe
  const shaped = readOf(run(text('COAI-JQ found', 'COAI-VENDOR claude unsafe', `:path /${'M4RK'.repeat(10)}`, ...DEFAULT.codex, ...DEFAULT.agy))).claude;
  assert.deepEqual(shaped, { state: 'unsafe', path: null });
  // the words of the table
  assert.ok(Object.keys(ACCOUNTS_UNREADABLE_SAID).includes('unsafe'));
});

test('A2-T27: the COAI-JQ line and the vendors agree', () => {
  const missingFound = GOOD_LINES.map((line) => (line === 'COAI-JQ found' ? 'COAI-JQ missing apt-get' : line));
  unreadableWith(run(text(...missingFound)), 'shape', 'jq missing with Claude found');
  const foundNoJq = ['COAI-JQ found', 'COAI-VENDOR claude unreadable no-jq', ...GOOD_LINES.slice(7)];
  unreadableWith(run(text(...foundNoJq)), 'shape', 'jq found with unreadable no-jq');
  for (const word of ['dnf', 'none']) {
    const lines = [`COAI-JQ missing ${word}`, 'COAI-VENDOR claude unreadable no-jq', 'COAI-VENDOR codex absent', ...GOOD_LINES.slice(13)];
    const answer = readOf(run(text(...lines)));
    assert.equal(answer.jqMissing, word);
    assert.deepEqual(answer.claude, { state: 'unreadable', why: 'no-jq' });
    assert.deepEqual(answer.codex, { state: 'absent' });
    assert.equal(answer.agy.state, 'found');
  }
  assert.equal(readOf(run(GOOD)).jqMissing, undefined);
});

test('A2-T27: the arrangement — order, a missing or a doubled vendor, the USAGE that follows a found one, the JQ line', () => {
  const [jq, claude, codex, agy] = [DEFAULT.jq, DEFAULT.claude, DEFAULT.codex, DEFAULT.agy];
  const shapes: readonly (readonly [string, readonly string[]])[] = [
    ['codex before claude', [...jq, ...codex, ...claude, ...agy]],
    ['agy missing', [...jq, ...claude, ...codex]],
    ['claude twice', [...jq, ...claude, ...claude, ...codex, ...agy]],
    ['a USAGE after a state that is not found', [...jq, 'COAI-VENDOR claude absent', 'COAI-USAGE claude skipped', ...codex, ...agy]],
    ['a found with no USAGE', [...jq, 'COAI-VENDOR claude found', ...codex, ...agy]],
    ['no COAI-JQ', [...claude, ...codex, ...agy]],
    ['two COAI-JQ', [...jq, ...jq, ...claude, ...codex, ...agy]],
    ['a USAGE of another vendor after a found one', [...jq, ...claude.slice(0, 5), 'COAI-USAGE codex failed none', ...codex, ...agy]],
    ['a required key missing: no sign-in-file', [...jq, ...claude, ...codex.filter((line) => !line.startsWith(':sign-in-file ')), ...agy]],
  ];
  for (const [name, lines] of shapes) {
    unreadableWith(run(text(...lines)), 'shape', name);
  }
});

test('A2-T27: COAI-BAD is the only record of its frame, by its own word', () => {
  for (const word of ['tool', 'operands', 'home', 'folder']) {
    unreadableWith(run(text(`COAI-BAD ${word}`)), word, word);
    unreadableWith(run(text(`COAI-BAD ${word}`, ...GOOD_LINES.slice(13))), 'shape', `${word} with a vendor after it`);
  }
  unreadableWith(run(text('COAI-BAD nope')), 'marker', 'an unknown word');
});

// ───────────────── A2-T28: the credential net knows every key shape the redactor knows ─────────────────

function alternatives(): readonly string[] {
  const group = /\\b\(([^)]*)\)/u.exec(VENDOR_KEY_SHAPE.source)?.[1] ?? '';

  return group.split('|');
}

test('A2-T28: every alternative of the redactor\'s vendor key shapes is a credential to the net and to the redactor', () => {
  const alts = alternatives();
  assert.ok(alts.length >= 7, `found ${alts.length} alternatives`);
  for (const alt of alts) {
    const sample = `${alt.replace('[baprs]', 'b')}${'A1b2'.repeat(4)}`;
    assert.equal(credentialShaped(sample), true, `the net does not know ${alt}`);
    // Twice in a row, BEFORE anything else touches the pattern: a shared `g` regex would start the second call at the end of the sample.
    assert.equal(credentialShaped(sample), true, `the same call twice in a row gives another answer for ${alt} (the g flag)`);
    assert.notEqual(safeText(sample, 400), sample, `the redactor does not change ${alt}, so the sample is no key to it`);
  }
});

test('A2-T28: what is a credential and what is not', () => {
  for (const yes of ['a'.repeat(40), `eyJ${'a'.repeat(16)}`, `ya29.${'a'.repeat(16)}`, '-----BEGIN OPENSSH PRIVATE KEY-----']) {
    assert.equal(credentialShaped(yes), true, yes);
  }
  for (const no of ['someone@example.com', 'Example Org', 'Basic Industries', 'Token Engineering Ltd', 'team', 'prolite', '2.1.326', 'a'.repeat(39), ...LINES]) {
    assert.equal(credentialShaped(no), false, no);
  }
});

// ───────────────── A2-T29: plainValue is one rule ─────────────────

const CONTROL = /[\p{Cc}\p{Cf}]/u;

test('A2-T29: plainValue takes controls out, breaks $(, cuts at 200, and is idempotent; withoutControls is the first step alone', () => {
  const next = seeded(11);
  const alphabet = ['a', 'b', ' ', '$', '(', '$(', '$$((', String.fromCodePoint(7), String.fromCodePoint(0x200b), String.fromCodePoint(0x202e), 'é', '\n'];
  for (let n = 0; n < 200; n += 1) {
    const input = Array.from({ length: Math.floor(next() * 300) }, () => alphabet[Math.floor(next() * alphabet.length)]).join('');
    const plain = plainValue(input);
    assert.ok(!CONTROL.test(plain), 'a control or format character is left');
    assert.ok(!plain.includes('$('), 'a $( is left');
    assert.ok([...plain].length <= 200, 'longer than 200 characters');
    assert.equal(plainValue(plain), plain, 'plainValue is not idempotent');
    const stripped = input.replace(/[\p{Cc}\p{Cf}]/gu, '');
    assert.equal(withoutControls(input), stripped, 'withoutControls does more or less than take the characters out');
  }
  assert.equal(withoutControls('a$(b'), 'a$(b', 'a $( stays in the first step');
});

test('A2-T29: the class of the plain rule stands once in remoteAccountsReply.ts, and plainValue calls withoutControls', () => {
  const source = sourceOf('remoteAccountsReply.ts');
  assert.equal(source.split('\\p{Cc}').length - 1, 1, 'the character class of the plain rule stands twice in remoteAccountsReply.ts');
  assert.ok(/function plainValue[\s\S]*?withoutControls\(/u.test(source), 'plainValue does not call withoutControls(');
  assert.equal('x \\p{Cc} y \\p{Cc}'.split('\\p{Cc}').length - 1, 2, 'the scan counts a second copy in a sample');
});

// The last test of the file: every other test has run, and each has added the faults it reached.
test('A2-T15 derived: every word of ACCOUNTS_UNREADABLE_SAID was reached by a test of this file, and the clauses are whole and different', () => {
  for (const why of ['operands', 'home', 'folder', 'tool']) {
    unreadableWith(run(text(`COAI-BAD ${why}`)), why, `COAI-BAD ${why}`);
  }
  assert.deepEqual([...reached].sort(), Object.keys(ACCOUNTS_UNREADABLE_SAID).sort(), 'a word of the closed list has no test, or a test reached a word that is not in it');
  const clauses = Object.values(ACCOUNTS_UNREADABLE_SAID);
  assert.ok(clauses.every((clause) => clause.length > 0));
  assert.equal(new Set(clauses).size, clauses.length, 'two clauses are the same');
});
