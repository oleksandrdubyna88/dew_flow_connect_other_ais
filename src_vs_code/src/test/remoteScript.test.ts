import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { SCAN_BUDGET } from '../claudeSessions';
import {
  REGISTRY_MAX,
  REMOTE_PROTOCOL,
  REMOTE_SCRIPT,
  REMOTE_TOOLS,
  RemoteCall,
  TAIL_MAX,
  callOf,
  operandsOf,
  shQuoted,
} from '../remoteScript';

/**
 * The operands (C2a), their quoting for the remote login shell, and the text of the script that is sent (E2.S2).
 *
 * <p>The behaviour of the script is `remoteScriptRuns.test.ts`, which runs it with a real `sh`. This file holds the three things
 * that need no process but one `sh -c`: the grammar of what is asked, the quoting that makes each operand ONE word for a
 * shell, and the shape of the script's own text (a scan, each rule with a companion that proves the scan sees what it scans).</p>
 */

const S = 'small-tasks';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';

/** A real `sh`, or the reason there is none. On the Linux runner of CI a missing `sh` is a failure, never a skip (S2-T44). */
function shAvailable(): string | undefined {
  const probe = spawnSync('sh', ['-c', 'exit 0']);

  return probe.error === undefined ? undefined : 'no POSIX sh on this machine';
}

const needsSh = (): string | undefined => {
  const missing = shAvailable();
  if (missing !== undefined && process.env['CI'] === 'true' && process.platform === 'linux') {
    assert.fail(`${missing}: on the Linux runner of CI the sh tests MUST run (S2-T44)`);
  }

  return missing;
};

/** What `sh -c "printf '%s\n' <quoted words>"` prints: each word on a line, if and only if each was ONE literal word. */
function printedByShell(words: readonly string[]): string {
  const run = spawnSync('sh', ['-c', `printf '%s\\n' ${words.map(shQuoted).join(' ')}`], { encoding: 'utf8' });
  assert.equal(run.status, 0, `the shell did not even parse the command: ${run.stderr}`);

  return run.stdout;
}

const HOSTILE: readonly string[] = [
  '$(id)', '`id`', ';', '|', '&', '*', '~', ' ', '\t', '"', "'", '\\', '--', '-', '$HOME', '${HOME}', '$((1+1))', '!', '#', '(', ')',
  '{a,b}', '<x', '>x', "it's", "'; id; '", '"; id; "', 'a b  c', '*/*', '', 'é', '‮',
];

test('S2-T4: every operand reaches the remote shell as ONE literal word (a real sh -c)', (context) => {
  const missing = needsSh();
  if (missing !== undefined) {
    context.skip(missing);

    return;
  }
  const calls: readonly RemoteCall[] = [
    { kind: 'session', session: S },
    { kind: 'id', session: S, id: ID },
    { kind: 'titles', session: S, folders: ['-home-git-kb', 'C--Users-x', 'a.b_c-d'] },
  ];
  for (const call of calls) {
    const words = operandsOf(call);
    assert.equal(printedByShell(words), words.map((word) => `${word}\n`).join(''), `${call.kind}: an operand was not one literal word`);
  }
  assert.ok(HOSTILE.length >= 25, 'the fixture has fewer than the 25 hostile strings of M-Q1');
  assert.equal(printedByShell(HOSTILE), HOSTILE.map((word) => `${word}\n`).join(''), 'a hostile string was not printed exactly: $(id) would be run');
});

test('S2-T4: a NUL, a CR or an LF cannot be quoted — it throws', () => {
  for (const word of ['a\0b', 'a\rb', 'a\nb', '\n']) {
    assert.throws(() => shQuoted(word), Error, `a word with a control line character was quoted: ${JSON.stringify(word)}`);
  }
  // NUL on its own, because the second line of defence (the alphabets) is not the one under test here.
  assert.throws(() => shQuoted('\0'), 'a NUL was quoted');
  assert.equal(shQuoted("a'b"), "'a'\\''b'", 'the quote is closed, escaped and opened again');
  assert.equal(shQuoted(S), `'${S}'`);
});

test('S2-T16: the operands have one grammar', () => {
  assert.deepEqual(operandsOf({ kind: 'session', session: S }), [S]);
  assert.deepEqual(operandsOf({ kind: 'id', session: S, id: ID }), [S, 'id', ID]);
  assert.deepEqual(operandsOf({ kind: 'titles', session: S, folders: ['-home-git-kb', 'b'] }), [S, 'titles', '-home-git-kb', 'b']);
  const roundTrip: readonly RemoteCall[] = [
    { kind: 'session', session: S },
    { kind: 'id', session: S, id: ID },
    { kind: 'id', session: S, id: ID.toUpperCase() },
    { kind: 'titles', session: S, folders: ['-home-git-kb'] },
    { kind: 'titles', session: S, folders: ['a', 'b', 'c'] },
  ];
  for (const call of roundTrip) {
    assert.deepEqual(callOf(operandsOf(call)), call, `${call.kind} did not survive the round trip`);
  }

  const refused: ReadonlyArray<readonly string[]> = [
    [], ['-s'], ['a.b'], [S, 'id'], [S, 'id', 'nope'], [S, 'titles'], [S, 'titles', 'a', 'b', 'c', 'd'], [S, 'titles', '..'],
    [S, 'titles', '.x'], [S, 'titles', 'a/b'], [S, 'titles', 'a b'], [S, 'titles', 'a\tb'], [S, 'titles', 'a"b'],
    [S, 'titles', 'a'.repeat(256)], [S, 'titles', 'same', 'same'], [S, 'tail', 'x'], [S, 'extra'], [S, 'id', ID, 'more'],
    ['-x', 'id', ID], ['a b'], [''], [S, ''], [S, 'titles', ''], [S, 'ID', ID],
  ];
  for (const operands of refused) {
    assert.equal(callOf(operands), undefined, `a call that must be refused was accepted: ${JSON.stringify(operands)}`);
  }
  for (const folder of ['-home-git-kb', 'C--Users-x', 'a.b_c-d', 'a'.repeat(255), '_x', '0']) {
    assert.notEqual(callOf([S, 'titles', folder]), undefined, `the legal folder ${folder} was refused`);
  }
  assert.throws(() => operandsOf({ kind: 'titles', session: S, folders: ['..'] }), Error, 'operandsOf must throw for a call its own grammar refuses');
  assert.throws(() => operandsOf({ kind: 'id', session: S, id: 'nope' }), Error);
  assert.throws(() => operandsOf({ kind: 'session', session: 'a b' }), Error);
});

const lines = REMOTE_SCRIPT.split('\n');

test('S2-T17: the script keeps its own rules (a scan of its text)', () => {
  assert.equal(lines[0], 'coai_main() {', 'line 1 is the function head');
  assert.equal(lines[lines.length - 1], '', 'the text ends with a line end (the last split element is empty)');
  assert.equal(lines[lines.length - 2], "printf 'COAI-END\\n'", 'the last line prints the end marker');
  assert.equal(lines[lines.length - 3], 'coai_main "$@" </dev/null 2>/dev/null', 'the second-last line is the one call line');
  assert.ok(!REMOTE_SCRIPT.includes('\r'), 'a CR is in the script');

  // Every other line is inside the function: two spaces, the closing brace, a blank line, or the line that closes the
  // newline literal (`nl='` and then `'`). The spec's sentence said only "two spaces or `}`"; the text it prints has those two.
  const inside = lines.slice(1, -3);
  assert.deepEqual(inside.filter((line) => !/^( {2}|\}$|$|'$)/u.test(line)), [], 'a line outside the function');
  assert.equal(inside.filter((line) => line === '}').length, 1, 'exactly one closing brace');
  assert.equal(lines.indexOf('}'), lines.length - 4, 'the closing brace is right before the call line');
});

test('S2-T17: nothing the script must never contain is there, and the scan sees what it scans', () => {
  const offences: ReadonlyArray<readonly [string, RegExp, string]> = [
    ['a here-document', /<</u, 'a<<b'],
    ['a backtick', /`/u, 'a`b'],
    ['eval', /\beval\b/u, 'eval x'],
    ['trap', /\btrap\b/u, 'trap x'],
    ['a line that starts with a tilde', /^\s*~/mu, '  ~x'],
    ['a background job', /(?<!&)&(?!&)/u, 'a & b'],
    ['a redirect that is not to /dev/null', />(?!\/dev\/null)/u, 'a > b'],
  ];
  for (const [what, pattern, sample] of offences) {
    assert.ok(pattern.test(sample), `the scan for ${what} does not see ${JSON.stringify(sample)}`);
    assert.ok(!pattern.test(REMOTE_SCRIPT), `the script contains ${what}`);
  }
  assert.ok(!/(?<!&)&(?!&)/u.test('a && b'), 'the background-job scan must not take && for it');
  assert.ok(/>\/dev\/null/u.test(REMOTE_SCRIPT), '`>/dev/null` is in the text, so the redirect scan has something to be silent about');
});

test('S2-T17 companions: the one call line, set -f, the guarded printf and the numbers', () => {
  assert.equal(REMOTE_SCRIPT.split('</dev/null 2>/dev/null').length - 1, 1, 'the one call line holds the redirects, once');
  const set = lines.filter((line) => /^\s*set [+-]f\s*$/u.test(line));
  assert.equal(set[0]?.trim(), 'set -f', 'the first set of the function is set -f');
  const at = (index: number): string => lines[index]?.trim() ?? '';
  lines.forEach((line, index) => {
    if (line.trim() === 'set +f') {
      assert.equal(at(index + 2), 'set -f', `the glob at line ${index + 1} is not closed by set -f two lines after it`);
    }
  });
  assert.equal(set.filter((line) => line.trim() === 'set +f').length, 2, 'two globs');
  assert.equal(REMOTE_SCRIPT.split('|| return 0').length - 1, 10, 'the closed-stdout guard is on ten printf lines');

  for (const [text, what] of [
    [`tail -c ${TAIL_MAX}`, 'TAIL_MAX'],
    [`head -c ${REGISTRY_MAX + 1}`, 'REGISTRY_MAX + 1'],
    [`-ge ${SCAN_BUDGET.most}`, 'SCAN_BUDGET.most'],
    [`-gt ${SCAN_BUDGET.mostBytes}`, 'SCAN_BUDGET.mostBytes'],
    [`COAI-BEGIN ${REMOTE_PROTOCOL}`, 'REMOTE_PROTOCOL'],
  ] as const) {
    assert.ok(REMOTE_SCRIPT.includes(text), `the script does not hold ${what}: ${text}`);
  }
  assert.ok(8 * 1000 < SCAN_BUDGET.withinMs, 'the title scan of the script (8 s) is under the extension\'s own time budget');
});

test('S2-T17 companions: every tool of REMOTE_TOOLS is checked, and no other', () => {
  const checked = lines
    .filter((line) => /^\s*for tool in /u.test(line))
    .flatMap((line) => /for tool in ([^;]*);/u.exec(line)?.[1]?.split(' ') ?? []);
  assert.equal(lines.filter((line) => /^\s*for tool in /u.test(line)).length, 2, 'two tool checks');
  assert.deepEqual([...checked].sort(), [...REMOTE_TOOLS].sort(), 'the checked tools are not the names of REMOTE_TOOLS');
  assert.equal(REMOTE_TOOLS.length, 11);
});
