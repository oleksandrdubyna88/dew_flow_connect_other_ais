import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import {
  ACCOUNTS_CEILING_SECONDS, ACCOUNTS_LINES_MAX, ACCOUNTS_LINE_BYTES_MAX, ACCOUNTS_MODES, ACCOUNTS_PROTOCOL, ACCOUNTS_READ_SECONDS,
  ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_SCRIPT, ACCOUNTS_SECONDS, ACCOUNTS_TOOLS, ACCOUNT_JSON_MAX, CODEX_FILES_MAX, CODEX_TAIL_MAX,
  MANAGER_WORDS, USAGE_JSON_MAX, USAGE_LINES_MAX, isAccountsMode,
} from '../remoteAccountsScript';
import { SSH_OPTIONS } from '../remoteSsh';

/**
 * The accounts script as a TEXT (E1.S2, spec § A2.3 and § A2.11.2): that it is the spec's block, pinned; that its two operands are a
 * closed list in TypeScript and again in `sh`; that it keeps its own rules (one function, called last by ONE brace group, no write,
 * no package manager); and that every number written into it is a constant of the module. What it DOES when a shell runs it is
 * `remoteAccountsRuns.test.ts`'s.
 *
 * <p>Every scan here has its companion: the same scanner applied to a text with the one thing it looks for changed, so a scan that
 * stopped matching would be red here and not pass for ever.</p>
 */

/** The sha256 of `ACCOUNTS_SCRIPT`, as pinned by the spec's procedure (§ A2.3). A later change of the text moves the block, the pin and this literal in one commit. */
const PINNED = 'e42bae875254ba509d2be0ed0c1cc079999e08d1be0f82bfb2edb35ac55d8ebd';

const HERE = join(__dirname, '..', '..');
const SPEC_NAME = 'PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md';

/** The spec, from `todo/` or from `research/` once it is promoted: found in both, or in neither, is a failure. */
function specText(): string {
  const places = ['todo', 'research'].map((folder) => join(HERE, '..', folder, SPEC_NAME)).filter((file) => existsSync(file));
  assert.equal(places.length, 1, `the spec must be in exactly one of todo/ and research/, found in ${places.length}`);

  return readFileSync(places[0] as string, 'utf8');
}

/** The first `sh` block after the heading that starts `## A2.3`, with its last line end, as the spec's own step 0 takes it. */
function specBlock(spec: string): string {
  const heading = spec.indexOf('\n## A2.3');
  assert.ok(heading >= 0, 'the spec has no heading that starts "## A2.3"');
  const open = spec.indexOf('```sh\n', heading);
  assert.ok(open >= 0, 'no sh block after the heading');
  const start = open + '```sh\n'.length;

  return spec.slice(start, spec.indexOf('\n```', start) + 1);
}

function firstDifference(left: string, right: string): string {
  const a = left.split('\n');
  const b = right.split('\n');
  const at = a.findIndex((line, index) => line !== b[index]);
  const line = at < 0 ? Math.min(a.length, b.length) : at;

  return `line ${line + 1}: spec ${JSON.stringify(a[line] ?? '(none)')} vs constant ${JSON.stringify(b[line] ?? '(none)')}`;
}

const sha256 = (text: string): string => createHash('sha256').update(text).digest('hex');

test('A2-T2: the sha256 of ACCOUNTS_SCRIPT is the pinned value, and the text is 222 lines of ASCII ending with LF', () => {
  assert.match(PINNED, /^[0-9a-f]{64}$/u, 'the test still holds a pin that is not a sha256');
  assert.equal(sha256(ACCOUNTS_SCRIPT), PINNED, 'the text of ACCOUNTS_SCRIPT changed without the pin');
  assert.equal(ACCOUNTS_SCRIPT.split('\n').length - 1, 222, 'the text is 222 lines');
  assert.ok(ACCOUNTS_SCRIPT.endsWith('\n'), 'the text ends with a line end');
});

test('A2-T2: the spec has the same pin on its own line, and its sh block IS the constant', () => {
  const spec = specText();
  const pin = /^Pinned sha256: (.*)$/mu.exec(spec);
  assert.ok(pin !== null, 'the spec has no line "Pinned sha256:"');
  const value = /^`([0-9a-f]{64})`/u.exec(pin?.[1] ?? '');
  assert.ok(value !== null, `the spec's pin line still says ${JSON.stringify((pin?.[1] ?? '').slice(0, 20))}`);
  assert.equal(value?.[1], PINNED, 'the spec and the test pin different values');
  const block = specBlock(spec);
  assert.equal(block === ACCOUNTS_SCRIPT, true, `the spec and the constant differ at ${firstDifference(block, ACCOUNTS_SCRIPT)}`);
});

test('A2-T2 companions: a difference of one space is named by its line, and a hash of another text differs', () => {
  assert.match(firstDifference('a\nb c\nd', 'a\nb  c\nd'), /^line 2: /u);
  assert.notEqual(sha256(`${ACCOUNTS_SCRIPT} `), sha256(ACCOUNTS_SCRIPT));
  assert.equal(specBlock('x\n## A2.3 — t\n```sh\nline one\nline two\n```\nmore').endsWith('line two\n'), true);
});

// ───────────────── A2-T3 (the TypeScript half): the modes ─────────────────

test('A2-T3: the modes are two words by a closed list, and the script holds the case line built from the same list', () => {
  assert.deepEqual([...ACCOUNTS_MODES], ['claude-usage', 'no-claude-usage']);
  for (const word of ACCOUNTS_MODES) {
    assert.equal(isAccountsMode(word), true, word);
  }
  for (const word of ['', 'usage', 'Claude-usage', 'claude-usage ', 'claude-usage;id', 'constructor']) {
    assert.equal(isAccountsMode(word), false, JSON.stringify(word));
  }
  const caseLine = `    ${ACCOUNTS_MODES.map((mode) => `1:${mode}`).join('|')}) ;;`;
  assert.equal(ACCOUNTS_SCRIPT.split('\n').filter((line) => line === caseLine).length, 1, `the script has no line ${JSON.stringify(caseLine)}`);
  assert.ok(ACCOUNTS_SCRIPT.includes('case $#:${1-} in\n'), 'the script does not count the operands with the case');
});

// ───────────────── A2-T25: the script keeps its own rules ─────────────────

const CALL = '{ </dev/null 2>/dev/null coai_accounts "$@"; printf \'COAI-ACCOUNTS-END\\n\'; }';
const count = (text: string, needle: string): number => text.split(needle).length - 1;
const matches = (text: string, pattern: RegExp): number => (text.match(pattern) ?? []).length;
const linesOf = (text: string): readonly string[] => text.split('\n');

/** The eight lines of `coai_manager`: its function body, from its first line to its closing one. */
const managerLines = (text: string): readonly string[] => {
  const lines = linesOf(text);
  const from = lines.indexOf('  coai_manager() {');
  const to = lines.indexOf('  }', from);

  return from < 0 || to < 0 ? [] : lines.slice(from + 1, to);
};

/** Each rule is a name and a test that is true when the text BREAKS it. */
type Rule = readonly [string, (text: string) => boolean];

const SHAPE_RULES: readonly Rule[] = [
  ['the first line is coai_accounts() {', (t) => linesOf(t)[0] !== 'coai_accounts() {'],
  ['the last line is the one brace group', (t) => linesOf(t).at(-2) !== CALL],
  ['the call stands in no other line', (t) => count(t, 'coai_accounts "$@"') !== 1 || count(t, 'COAI-ACCOUNTS-END') !== 1],
  ['every other line is empty, } or starts with two spaces', (t) => {
    const inner = linesOf(t).slice(1, -2);

    // The one exception is the line that closes the quote of `nl='` (a literal line feed), and it follows that line.
    return inner.some((line, index) => line !== '' && line !== '}' && !line.startsWith('  ') && !(line === '\'' && inner[index - 1] === '  nl=\''));
  }],
  ['the text ends with a line end and holds no CR and no character over 127', (t) => !t.endsWith('\n') || t.includes('\r') || /[^\n\u0020-\u007e]/u.test(t)],
];

const NO_WRITE_RULES: readonly Rule[] = [
  ['no here-document', (t) => t.includes('<<')],
  ['no backtick', (t) => t.includes('`')],
  ['no eval and no trap', (t) => /\b(eval|trap)\b/u.test(t)],
  ['no line that starts with ~', (t) => linesOf(t).some((line) => line.trimStart().startsWith('~'))],
  ['every > is the start of >/dev/null', (t) => /(?<![0-9])>(?!\/dev\/null)/u.test(t.replaceAll('2>/dev/null', ''))],
  ['no & that is not &&', (t) => t.replaceAll('&&', '').includes('&')],
  ['one cd, and it is cd /', (t) => matches(t, /\bcd /gu) !== 1 || !t.includes('  cd / || ')],
  ['one export, and it is export PATH', (t) => matches(t, /\bexport\b/gu) !== 1 || !t.includes('  export PATH\n')],
  ['one TZ=, on the usage line', (t) => count(t, 'TZ=') !== 1 || !t.includes('TZ=UTC timeout -k 1 8 claude -p "/usage"')],
  ['every LC_ALL=C stands before grep or sed, or before timeout and then sed or ls', (t) => matches(t, /LC_ALL=C/gu) !== matches(t, /LC_ALL=C (?:grep|sed|timeout -k 1 \d+ (?:sed|ls))\b/gu)],
  ['no sudo, install, upgrade or add as a word', (t) => /\b(sudo|install|upgrade|add)\b/u.test(t)],
  ['the package managers stand only in the eight lines of coai_manager', (t) => {
    const named = linesOf(t).filter((line) => /\b(?:apt-get|dnf|yum|apk)\b/u.test(line));
    const tests = managerLines(t).filter((line) => /^ {4}(?:if|elif) \[ -x \/(?:usr\/bin|sbin)\/(?:apt-get|dnf|yum|apk) \]; then$/u.test(line));
    const prints = managerLines(t).filter((line) => /^ {6}printf '(?:apt-get|dnf|yum|apk)'$/u.test(line));

    return named.length !== 8 || tests.length !== 4 || prints.length !== 4;
  }],
];

const COMMAND_LINES = [
  'timeout -k 1 3 claude auth status --json',
  'timeout -k 1 2 claude --version',
  'TZ=UTC timeout -k 1 8 claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""',
  'timeout -k 1 4 agy --version',
] as const;

const VENDOR_RULES: readonly Rule[] = [
  ['the four vendor command lines are there, each once, word for word', (t) => COMMAND_LINES.some((line) => count(t, line) !== 1)],
  ['claude follows timeout -k 1 <n> three times and agy once', (t) => matches(t, /timeout -k 1 \d+ claude /gu) !== 3 || matches(t, /timeout -k 1 \d+ agy /gu) !== 1],
  ['no command is claude, agy or codex at the start of a line', (t) => linesOf(t).some((line) => /^\s*(?:claude|agy|codex)\s/u.test(line))],
  ['the only codex in a command is command -v codex', (t) => /codex/u.test(t.replaceAll('command -v codex', '').replaceAll(/COAI-(?:VENDOR|USAGE) codex/gu, '').replaceAll('.codex', '').replaceAll('coai_codex', '').replaceAll('jq_codex', ''))],
  ['no credential file of Claude or agy is named', (t) => ['.credentials.json', '.claude.json', 'cli.log'].some((name) => t.includes(name))],
  ['auth.json and the agy token are named once each, as the argument of coai_sign_in', (t) => count(t, 'auth.json') !== 1 || count(t, 'antigravity-oauth-token') !== 1
    || !t.includes('coai_sign_in "$HOME/.codex/auth.json"') || !t.includes('coai_sign_in "$HOME/.gemini/antigravity-cli/antigravity-oauth-token"')],
  ['no jq program holds a word that prints a whole value, a key list or the environment', (t) => /\b(?:tojson|to_entries|input_filename|debug|keys|env)\b|@json|@text|\$ENV/u.test(t)],
];

const COMPANION_RULES: readonly Rule[] = [
  ['</dev/null 2>/dev/null is there once', (t) => count(t, '</dev/null 2>/dev/null') !== 1],
  ['|| return 0 is there seven times', (t) => count(t, '|| return 0') !== 7],
  ['the first set is set -f, and each set +f has a set -f two lines after it', (t) => {
    const lines = linesOf(t);
    const first = lines.find((line) => /^\s*set /u.test(line));
    const opens = lines.flatMap((line, index) => (line.trim() === 'set +f' ? [index] : []));

    return first?.trim() !== 'set -f' || opens.length !== 2 || opens.some((index) => lines[index + 2]?.trim() !== 'set -f');
  }],
];

const RULES: readonly Rule[] = [...SHAPE_RULES, ...NO_WRITE_RULES, ...VENDOR_RULES, ...COMPANION_RULES];
const broken = (text: string): readonly string[] => RULES.filter(([, rule]) => rule(text)).map(([name]) => name);

test('A2-T25: the script keeps its own rules', () => {
  assert.deepEqual(broken(ACCOUNTS_SCRIPT), []);
});

/** The companion of each rule: ONE change to the real text, and the scan must name exactly that rule. */
const MUTATIONS: readonly (readonly [string, string, (text: string) => string])[] = [
  ['the first line is coai_accounts() {', 'the first line', (t) => t.replace('coai_accounts() {', 'coai_accounts () {')],
  ['the last line is the one brace group', 'the last line as two lines (the form before gate G2)', (t) => t.replace(CALL, '</dev/null 2>/dev/null coai_accounts "$@"\nprintf \'COAI-ACCOUNTS-END\\n\'')],
  ['the call stands in no other line', 'a second call', (t) => t.replace('  return 0\n}\n', '  return 0\n}\n  coai_accounts "$@"\n')],
  ['every other line is empty, } or starts with two spaces', 'a line at column 0', (t) => t.replace('  unset IFS\n', 'unset IFS\n')],
  ['the text ends with a line end and holds no CR and no character over 127', 'a CR', (t) => t.replace('  unset IFS\n', '  unset IFS\r\n')],
  ['no here-document', 'a here-document', (t) => t.replace('  unset IFS\n', '  unset IFS\n  cat <<X\n')],
  ['no backtick', 'a backtick', (t) => t.replace('  unset IFS\n', '  unset IFS\n  x=`id`\n')],
  ['no eval and no trap', 'a trap', (t) => t.replace('  unset IFS\n', '  unset IFS\n  trap true EXIT\n')],
  ['no line that starts with ~', 'a ~ line', (t) => t.replace('  unset IFS\n', '  unset IFS\n  ~/x\n')],
  ['every > is the start of >/dev/null', 'a write', (t) => t.replace('  unset IFS\n', '  unset IFS\n  printf x >"$HOME/x"\n')],
  ['no & that is not &&', 'a background job', (t) => t.replace('  unset IFS\n', '  unset IFS\n  sleep 1 &\n')],
  ['one cd, and it is cd /', 'a second cd', (t) => t.replace('  unset IFS\n', '  unset IFS\n  cd /tmp\n')],
  ['one export, and it is export PATH', 'a second export', (t) => t.replace('  unset IFS\n', '  unset IFS\n  export LC_ALL\n')],
  ['one TZ=, on the usage line', 'TZ on another command', (t) => t.replace('timeout -k 1 2 claude --version', 'TZ=UTC timeout -k 1 2 claude --version')],
  ['every LC_ALL=C stands before grep or sed, or before timeout and then sed or ls', 'LC_ALL before cat', (t) => t.replace('  unset IFS\n', '  unset IFS\n  LC_ALL=C cat\n')],
  ['no sudo, install, upgrade or add as a word', 'a sudo line', (t) => t.replace("      printf 'none'\n", "      printf 'none'\n      sudo apt-get install -y jq\n")],
  ['the package managers stand only in the eight lines of coai_manager', 'a package manager outside coai_manager', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : dnf\n')],
  ['the four vendor command lines are there, each once, word for word', 'a changed flag', (t) => t.replace('--strict-mcp-config', '--strict-mcp')],
  ['claude follows timeout -k 1 <n> three times and agy once', 'a claude with no timeout', (t) => t.replace('timeout -k 1 3 claude auth status --json', 'claude auth status --json')],
  ['no command is claude, agy or codex at the start of a line', 'a bare claude command', (t) => t.replace('  unset IFS\n', '  unset IFS\n  claude auth status\n')],
  ['the only codex in a command is command -v codex', 'a start of codex', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : codex login status\n')],
  ['no credential file of Claude or agy is named', 'the credential file named', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : "$HOME/.claude/.credentials.json"\n')],
  ['auth.json and the agy token are named once each, as the argument of coai_sign_in', 'auth.json opened', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : "$HOME/.codex/auth.json"\n')],
  ['no jq program holds a word that prints a whole value, a key list or the environment', 'tojson', (t) => t.replace('(.rl.primary | win)', '(.rl.primary | tojson)')],
  ['</dev/null 2>/dev/null is there once', 'a second pair', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : </dev/null 2>/dev/null\n')],
  ['|| return 0 is there seven times', 'an eighth', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : || return 0\n')],
  ['the first set is set -f, and each set +f has a set -f two lines after it', 'a set +f left open', (t) => t.replaceAll('    set -f\n    while', '    :\n    while')],
];

test('A2-T25 companions: each rule is red for the one change it guards against, and only that rule', () => {
  const names = new Set(RULES.map(([name]) => name));
  assert.equal(names.size, RULES.length, 'two rules have one name');
  for (const [rule, what, change] of MUTATIONS) {
    assert.ok(names.has(rule), `no rule is named ${rule}`);
    const text = change(ACCOUNTS_SCRIPT);
    assert.notEqual(text, ACCOUNTS_SCRIPT, `the mutation "${what}" changed nothing, so it proves nothing`);
    assert.ok(broken(text).includes(rule), `the scan did not see "${what}" (rule: ${rule})`);
  }
  const guarded = new Set(MUTATIONS.map(([rule]) => rule));
  assert.deepEqual([...names].filter((name) => !guarded.has(name)), [], 'a rule has no companion');
});

test('A2-T25: coai_manager has exactly the eight lines the scan says it has', () => {
  assert.equal(managerLines(ACCOUNTS_SCRIPT).length, 11, 'the body of coai_manager: if, three elif, else, a printf for each of the five words, and fi');
  assert.equal(managerLines(ACCOUNTS_SCRIPT).filter((line) => /apt-get|dnf|yum|apk/u.test(line)).length, 8);
});

// ───────────────── A2-T26: the numbers in the script are the constants ─────────────────

test('A2-T26: the vendor commands and the readers carry the numbers of the constants', () => {
  const t = ACCOUNTS_SCRIPT;
  const s = ACCOUNTS_SECONDS;
  const r = ACCOUNTS_READ_SECONDS;
  for (const [what, text] of [
    ['the account command', `timeout -k 1 ${s.account} claude auth`],
    ['the version command', `timeout -k 1 ${s.version} claude --version`],
    ['the usage command', `timeout -k 1 ${s.usage} claude -p`],
    ['agy', `timeout -k 1 ${s.agy} agy`],
    ['the account reader (tail)', `timeout -k 1 ${r.account} tail`],
    ['the account reader (jq)', `timeout -k 1 ${r.account} jq -n -r "$jq_auth"`],
    ['the version reader', `timeout -k 1 ${r.version} sed`],
    ['the usage reader (tail)', `timeout -k 1 ${r.usage} tail`],
    ['the usage reader (jq)', `timeout -k 1 ${r.usage} jq -n -r "$jq_usage"`],
    ['the agy reader', `timeout -k 1 ${r.agy} sed`],
    ['the Codex list', `timeout -k 1 ${r.codexList} ls`],
    ['the Codex file time', `timeout -k 1 ${r.codexTime} stat`],
    ['the Codex file read', `timeout -k 1 ${r.codexFile} tail`],
    ['the Codex record reader', `timeout -k 1 ${r.codexRecord} jq -n -R`],
  ] as const) {
    assert.ok(t.includes(text), `${what}: the text has no "${text}"`);
  }
  assert.ok(t.includes(`claude --version | LC_ALL=C timeout -k 1 ${r.version} sed`), 'the version reader follows claude --version');
  assert.ok(t.includes(`agy --version | LC_ALL=C timeout -k 1 ${r.agy} sed`), 'the agy reader follows agy --version');
});

test('A2-T26: the byte caps, the line caps and the protocol are the constants', () => {
  const t = ACCOUNTS_SCRIPT;
  assert.equal(count(t, `tail -c ${ACCOUNT_JSON_MAX}`), 1);
  assert.equal(ACCOUNT_JSON_MAX, 65536);
  assert.equal(USAGE_JSON_MAX, CODEX_TAIL_MAX, 'the usage JSON and the Codex tail are the same number');
  assert.equal(count(t, `tail -c ${USAGE_JSON_MAX}`), 2, 'the text holds the 1 MiB exactly twice: the usage pipe and the Codex read');
  assert.equal(count(t, `tail -n ${CODEX_FILES_MAX}`), 1);
  assert.equal(count(t, `1,${USAGE_LINES_MAX}p`), 1);
  assert.equal(count(t, `-le ${USAGE_LINES_MAX}`), 1);
  assert.equal(count(t, `sent=${USAGE_LINES_MAX}`), 1);
  assert.ok(t.includes(`COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`));
  assert.equal(ACCOUNTS_REPLY_BYTES_MAX, (ACCOUNTS_LINES_MAX + 2) * (ACCOUNTS_LINE_BYTES_MAX + 1));
  assert.equal(ACCOUNTS_REPLY_BYTES_MAX, 33858);
  assert.equal(ACCOUNTS_LINES_MAX, 64);
  assert.equal(ACCOUNTS_LINE_BYTES_MAX, 512);
});

test('A2-T26: no ls, stat or Codex tail runs without a timeout, and each reader\'s limit is at least its command\'s', () => {
  const t = ACCOUNTS_SCRIPT;
  assert.equal(count(t, 'ls -tr'), 1);
  assert.equal(count(t, 'stat -c'), 1);
  assert.equal(matches(t, new RegExp(`tail -c ${CODEX_TAIL_MAX} <`, 'gu')), 1);
  for (const word of ['ls -tr', 'stat -c', `tail -c ${CODEX_TAIL_MAX} <`]) {
    assert.ok(new RegExp(`timeout -k 1 \\d+ ${word}`, 'u').test(t), `${word} does not follow a timeout -k 1 <n>`);
  }
  const s = ACCOUNTS_SECONDS;
  const r = ACCOUNTS_READ_SECONDS;
  for (const part of ['account', 'version', 'usage', 'agy'] as const) {
    assert.ok(r[part] >= s[part], `the reader of ${part} (${r[part]} s) ends before its command (${s[part]} s)`);
  }
});

const connectSeconds = (): number => {
  const word = SSH_OPTIONS.find((option) => /^ConnectTimeout=\d+$/u.test(option));
  assert.ok(word !== undefined, 'SSH_OPTIONS has no ConnectTimeout option');

  return Number(word?.split('=')[1]);
};

test('A2-T26: the readers\' limits and ssh\'s connect are under the runner\'s ceiling — the sum is said', () => {
  const r = ACCOUNTS_READ_SECONDS;
  const steps = r.account + r.version + r.usage + r.agy + r.codexList + r.codexTime + Math.max(r.codexRecord, CODEX_FILES_MAX * r.codexFile);
  const withConnect = steps + connectSeconds();
  assert.equal(steps, 24, 'the readers\' limits sum to 24 s (§ A2.3, the time table)');
  assert.equal(connectSeconds(), 5);
  assert.ok(withConnect < ACCOUNTS_CEILING_SECONDS, `the readers' limits (${steps} s) and ssh's connect (${connectSeconds()} s) are ${withConnect} s, over the ceiling of ${ACCOUNTS_CEILING_SECONDS} s`);
  const s = ACCOUNTS_SECONDS;
  const vendor = s.account + s.version + s.usage + s.agy + 4 + connectSeconds();
  assert.ok(vendor < ACCOUNTS_CEILING_SECONDS, `the four limits, one KILL second each and the connect are ${vendor} s`);
  assert.equal(ACCOUNTS_CEILING_SECONDS, 30);
});

test('A2-T26: each tool of ACCOUNTS_TOOLS is in the for-tool line and no other is; the manager words and paths are in the text in order', () => {
  const line = ACCOUNTS_SCRIPT.split('\n').find((one) => one.trimStart().startsWith('for tool in '));
  assert.equal(line, `  for tool in ${ACCOUNTS_TOOLS.join(' ')}; do`);
  const manager = managerLines(ACCOUNTS_SCRIPT);
  const printed = manager.flatMap((one) => /printf '([a-z-]+)'/u.exec(one)?.[1] ?? []);
  assert.deepEqual(printed, MANAGER_WORDS, 'MANAGER_WORDS are not the words coai_manager prints, in that order');
  const paths = manager.flatMap((one) => /\[ -x (\S+) \]/u.exec(one)?.[1] ?? []);
  assert.deepEqual(paths, ['/usr/bin/apt-get', '/usr/bin/dnf', '/usr/bin/yum', '/sbin/apk'], 'the four absolute paths, in the order apt-get, dnf, yum, apk');
});
