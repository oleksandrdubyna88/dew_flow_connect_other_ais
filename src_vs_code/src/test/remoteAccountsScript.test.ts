import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import {
  ACCOUNTS_CEILING_SECONDS, ACCOUNTS_CONNECT_SECONDS, ACCOUNTS_DIGEST, ACCOUNTS_KILL_SECONDS, ACCOUNTS_LINES_MAX, ACCOUNTS_LINE_BYTES_MAX, ACCOUNTS_MODES,
  ACCOUNTS_PATH, ACCOUNTS_PROTOCOL, ACCOUNTS_READ_SECONDS, ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_SCRIPT, ACCOUNTS_SECONDS, ACCOUNTS_SKIP_AGY, ACCOUNTS_TOOLS, ACCOUNT_JSON_MAX,
  AGY_LOG_TAIL_MAX, AGY_QUOTA_JSON_MAX, AGY_WINDOWS_MAX, CODEX_JSON_MAX, MANAGER_WORDS, USAGE_JSON_MAX, USAGE_LINES_MAX, isAccountsDigest, isAccountsMode, operandFits, worstSeconds,
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

/** The sha256 of `ACCOUNTS_SCRIPT`, as pinned by the E3.S2 spec (§ R2.3). A later change of the text moves the block, the pin and this literal in one commit. */
const PINNED = '67b7228c25f327f35cf57f74dbbe9c052dfac68ea44f69a0ee222cc0162995fa';

const HERE = join(__dirname, '..', '..');
const SPEC_NAME = 'PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S2_spec.md';

/** The spec, from `todo/` or from `research/` once it is promoted: found in both, or in neither, is a failure. */
function specText(): string {
  const places = ['todo', 'research'].map((folder) => join(HERE, '..', folder, SPEC_NAME)).filter((file) => existsSync(file));
  assert.equal(places.length, 1, `the spec must be in exactly one of todo/ and research/, found in ${places.length}`);

  return readFileSync(places[0] as string, 'utf8');
}

/** The first `sh` block after the heading that starts `## R2.3`, with its last line end, as the spec's own pin takes it. */
function specBlock(spec: string): string {
  const heading = spec.indexOf('\n## R2.3');
  assert.ok(heading >= 0, 'the spec has no heading that starts "## R2.3"');
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

test('E3S2-T1: the sha256 of ACCOUNTS_SCRIPT is the pinned value, and the text is 418 lines of ASCII ending with LF', () => {
  assert.match(PINNED, /^[0-9a-f]{64}$/u, 'the test still holds a pin that is not a sha256');
  assert.equal(sha256(ACCOUNTS_SCRIPT), PINNED, 'the text of ACCOUNTS_SCRIPT changed without the pin');
  assert.equal(ACCOUNTS_SCRIPT.split('\n').length - 1, 418, 'the text is 418 lines');
  assert.equal(Buffer.byteLength(ACCOUNTS_SCRIPT), 15_173, 'the text is 15 173 bytes');
  assert.equal(count(ACCOUNTS_SCRIPT, '${'), 16, 'the text holds ${ 16 times');
  assert.equal(count(ACCOUNTS_SCRIPT, '\\'), 82, 'the text holds 82 backslashes');
  assert.ok(ACCOUNTS_SCRIPT.endsWith('\n'), 'the text ends with a line end');
});

test('E3S2-T1: the spec has the same pin on its own line, and its sh block IS the constant', () => {
  const spec = specText();
  const pin = /^Pinned sha256: (.*)$/mu.exec(spec);
  assert.ok(pin !== null, 'the spec has no line "Pinned sha256:"');
  const value = /^`([0-9a-f]{64})`/u.exec(pin?.[1] ?? '');
  assert.ok(value !== null, `the spec's pin line still says ${JSON.stringify((pin?.[1] ?? '').slice(0, 20))}`);
  assert.equal(value?.[1], PINNED, 'the spec and the test pin different values');
  const block = specBlock(spec);
  assert.equal(block === ACCOUNTS_SCRIPT, true, `the spec and the constant differ at ${firstDifference(block, ACCOUNTS_SCRIPT)}`);
});

test('E3S2-T1 companions: a difference of one byte is named by its line, and a hash of another text differs', () => {
  assert.match(firstDifference('a\nb c\nd', 'a\nb  c\nd'), /^line 2: /u);
  assert.notEqual(sha256(`${ACCOUNTS_SCRIPT} `), sha256(ACCOUNTS_SCRIPT));
  assert.notEqual(sha256(ACCOUNTS_SCRIPT.replace('unset IFS', 'unset  IFS')), PINNED, 'one byte more does not move the hash');
  assert.equal(specBlock('x\n## R2.3 — t\n```sh\nline one\nline two\n```\nmore').endsWith('line two\n'), true);
});

// ───────────────── A2-T3 (the TypeScript half): the modes ─────────────────

test('E3S2-T4 (text): the modes are two words by a closed list, and the script holds the case rows and the digest rule', () => {
  assert.deepEqual([...ACCOUNTS_MODES], ['claude-usage', 'no-claude-usage']);
  for (const word of ACCOUNTS_MODES) {
    assert.equal(isAccountsMode(word), true, word);
  }
  for (const word of ['', 'usage', 'Claude-usage', 'claude-usage ', 'claude-usage;id', 'constructor']) {
    assert.equal(isAccountsMode(word), false, JSON.stringify(word));
  }
  const rows = ['    1:no-claude-usage:) want= ;;', '    2:no-claude-usage:no-agy-quota) want=; askq=no ;;', '    2:claude-usage:*)'];
  assert.deepEqual(ACCOUNTS_MODES.map((mode) => rows.some((row) => row.includes(`:${mode}:`))), [true, true], 'a mode has no case row');
  for (const row of rows) {
    assert.equal(ACCOUNTS_SCRIPT.split('\n').filter((line) => line === row).length, 1, `the script has no line ${JSON.stringify(row)}`);
  }
  assert.ok(ACCOUNTS_SCRIPT.includes('[ "${#want}" -eq 64 ] || { printf \'COAI-BAD operands\\n\'; return 0; }'), 'the digest is not held to 64 characters');
  assert.deepEqual(['0'.repeat(64), 'a'.repeat(64), 'f0'.repeat(32)].map(isAccountsDigest), [true, true, true]);
  for (const word of ['', '0'.repeat(63), '0'.repeat(65), 'A'.repeat(64), `${'0'.repeat(63)} `, `${'0'.repeat(63)}g`, 'someone@example.com']) {
    assert.equal(isAccountsDigest(word), false, JSON.stringify(word));
  }
  assert.equal(ACCOUNTS_DIGEST.source, '^[0-9a-f]{64}$');
  assert.ok(ACCOUNTS_SCRIPT.includes('case $#:${1-}:${2-} in\n'), 'the script does not count the operands with the case');
});

test('R3-T4: the second operand of the first mode is the one word, in TypeScript and in sh; no other word, and not for the usage mode', () => {
  assert.equal(ACCOUNTS_SKIP_AGY, 'no-agy-quota');
  assert.ok(ACCOUNTS_SCRIPT.includes(`    2:no-claude-usage:${ACCOUNTS_SKIP_AGY}) want=; askq=no ;;`));
  assert.equal(operandFits('no-claude-usage', undefined), true);
  assert.equal(operandFits('no-claude-usage', ACCOUNTS_SKIP_AGY), true);
  for (const word of ['', 'no-agy', 'no-agy-quota ', 'NO-AGY-QUOTA', 'no-agy-quota;id', '0'.repeat(64), 'someone@example.test']) {
    assert.equal(operandFits('no-claude-usage', word), false, JSON.stringify(word));
  }
  assert.equal(operandFits('claude-usage', '0'.repeat(64)), true);
  assert.equal(operandFits('claude-usage', undefined), false);
  assert.equal(operandFits('claude-usage', ACCOUNTS_SKIP_AGY), false);
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
  ['every LC_ALL=C stands before grep or sed, or before timeout and then sed or grep', (t) => matches(t, /LC_ALL=C/gu) !== matches(t, /LC_ALL=C (?:grep|sed|timeout -k 1 \d+ (?:sed|grep))\b/gu)],
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
  'timeout -k 1 2 agy --version',
  'timeout -k 1 4 agy -p /quota --output-format json',
  'timeout -k 1 4 codex app-server',
] as const;

const VENDOR_RULES: readonly Rule[] = [
  ['the six vendor command lines are there, each once, word for word', (t) => COMMAND_LINES.some((line) => count(t, line) !== 1)],
  ['claude follows timeout -k 1 <n> three times, agy twice and codex once', (t) => matches(t, /timeout -k 1 \d+ claude /gu) !== 3 || matches(t, /timeout -k 1 \d+ agy /gu) !== 2 || matches(t, /timeout -k 1 \d+ codex /gu) !== 1],
  ['no command is claude, agy or codex at the start of a line', (t) => linesOf(t).some((line) => /^\s*(?:claude|agy|codex)\s/u.test(line))],
  ['the only codex in a command is codex app-server (and coai_vendor codex)', (t) => /codex/u.test(t.replaceAll('timeout -k 1 4 codex app-server', '').replaceAll('coai_vendor codex', '').replaceAll(/COAI-(?:VENDOR|USAGE) codex/gu, '').replaceAll('.codex', '').replaceAll('coai_codex', '').replaceAll('jq_codex', ''))],
  ['no credential file of Claude or agy is named; cli.log once, as the log whose tail is searched', (t) => ['.credentials.json', '.claude.json'].some((name) => t.includes(name))
    || count(t, 'cli.log') !== 1 || !t.includes('logf=$HOME/.gemini/antigravity-cli/cli.log') || !t.includes('tail -c 1048576 "$logf"')],
  ['auth.json (in the two places Codex is found) and the agy token are named as the argument of coai_sign_in', (t) => count(t, 'auth.json') !== 2 || count(t, 'antigravity-oauth-token') !== 1
    || !t.includes('coai_sign_in "$HOME/.codex/auth.json"') || !t.includes('coai_sign_in "$HOME/.gemini/antigravity-cli/antigravity-oauth-token"')],
  ['no jq program holds a word that prints a whole value, a key list or the environment', (t) => /\b(?:tojson|to_entries|input_filename|debug|keys)\b|(?<!\/)\benv\b|@json|@text|\$ENV/u.test(t)],
];

const JQ_RULES: readonly Rule[] = [
  ['no jq program uses a regular expression, so a jq built without one still gives the quota', (t) => /\b(?:test|match|capture|scan|splits|sub|gsub)\(/u.test(t)],
];

const COMPANION_RULES: readonly Rule[] = [
  ['</dev/null 2>/dev/null is there once', (t) => count(t, '</dev/null 2>/dev/null') !== 1],
  ['|| return 0 is there seventeen times', (t) => count(t, '|| return 0') !== 17],
  ['the first set is set -f, and there is no set +f: nothing is globbed any more', (t) => {
    const lines = linesOf(t);
    const first = lines.find((line) => /^\s*set /u.test(line));

    return first?.trim() !== 'set -f' || lines.some((line) => line.trim() === 'set +f');
  }],
];

const RULES: readonly Rule[] = [...SHAPE_RULES, ...NO_WRITE_RULES, ...VENDOR_RULES, ...JQ_RULES, ...COMPANION_RULES];
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
  ['every LC_ALL=C stands before grep or sed, or before timeout and then sed or grep', 'LC_ALL before cat', (t) => t.replace('  unset IFS\n', '  unset IFS\n  LC_ALL=C cat\n')],
  ['no sudo, install, upgrade or add as a word', 'a sudo line', (t) => t.replace("      printf 'none'\n", "      printf 'none'\n      sudo apt-get install -y jq\n")],
  ['the package managers stand only in the eight lines of coai_manager', 'a package manager outside coai_manager', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : dnf\n')],
  ['the six vendor command lines are there, each once, word for word', 'a changed flag', (t) => t.replace('--strict-mcp-config', '--strict-mcp')],
  ['the six vendor command lines are there, each once, word for word', 'a changed agy quota command', (t) => t.replace('agy -p /quota --output-format json', 'agy -p /quota')],
  ['claude follows timeout -k 1 <n> three times, agy twice and codex once', 'a claude with no timeout', (t) => t.replace('timeout -k 1 3 claude auth status --json', 'claude auth status --json')],
  ['claude follows timeout -k 1 <n> three times, agy twice and codex once', 'a codex with no timeout', (t) => t.replace('timeout -k 1 4 codex app-server', 'codex app-server')],
  ['claude follows timeout -k 1 <n> three times, agy twice and codex once', 'an agy quota with no timeout', (t) => t.replace('timeout -k 1 4 agy -p /quota', 'agy -p /quota')],
  ['no command is claude, agy or codex at the start of a line', 'a bare claude command', (t) => t.replace('  unset IFS\n', '  unset IFS\n  claude auth status\n')],
  ['the only codex in a command is codex app-server (and coai_vendor codex)', 'a start of codex', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : codex login status\n')],
  ['no credential file of Claude or agy is named; cli.log once, as the log whose tail is searched', 'a second mention of the log', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : "$HOME/.gemini/antigravity-cli/cli.log"\n')],
  ['no credential file of Claude or agy is named; cli.log once, as the log whose tail is searched', 'the credential file named', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : "$HOME/.claude/.credentials.json"\n')],
  ['auth.json (in the two places Codex is found) and the agy token are named as the argument of coai_sign_in', 'auth.json opened', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : "$HOME/.codex/auth.json"\n')],
  ['no jq program holds a word that prints a whole value, a key list or the environment', 'tojson', (t) => t.replace('($r.primary | win)', '($r.primary | tojson)')],
  ['no jq program uses a regular expression, so a jq built without one still gives the quota', 'a test() in the bucket prefix', (t) => t.replace('startswith("gemini-")', 'test("^gemini-")')],
  ['</dev/null 2>/dev/null is there once', 'a second pair', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : </dev/null 2>/dev/null\n')],
  ['|| return 0 is there seventeen times', 'an eighteenth', (t) => t.replace('  unset IFS\n', '  unset IFS\n  : || return 0\n')],
  ['the first set is set -f, and there is no set +f: nothing is globbed any more', 'a set +f', (t) => t.replace('  unset IFS\n', '  unset IFS\n  set +f\n')],
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
    ['agy', `timeout -k 1 ${s.agy} agy --version`],
    ['the agy quota command', `timeout -k 1 ${s.agyQuota} agy -p /quota`],
    ['codex app-server', `timeout -k 1 ${s.codex} codex app-server`],
    ['the account reader (tail)', `timeout -k 1 ${r.account} tail`],
    ['the account reader (jq)', `timeout -k 1 ${r.account} jq -n -r "$jq_auth"`],
    ['the version reader', `timeout -k 1 ${r.version} sed`],
    ['the usage reader (tail)', `timeout -k 1 ${r.usage} tail`],
    ['the usage reader (jq)', `timeout -k 1 ${r.usage} jq -n -r "$jq_usage"`],
    ['the agy reader', `timeout -k 1 ${r.agy} sed`],
    ['the digest reader', `timeout -k 1 ${r.digest} sha256sum`],
    ['the Codex reader (tail)', `timeout -k 1 ${r.codex} tail`],
    ['the Codex reader (jq)', `timeout -k 1 ${r.codex} jq -n -R`],
    ['the quota reader (tail)', `timeout -k 1 ${r.agyQuota} tail`],
    ['the quota reader (jq)', `timeout -k 1 ${r.agyQuota} jq -n -r "$jq_agy"`],
    ['the log reader (tail)', `timeout -k 1 ${r.agyLog} tail -c`],
    ['the reader of a vendor program\'s first line (UID 0)', `timeout -k 1 ${r.shebang} sed -n -e '1{/^#!/p;q;}'`],
    ['the log reader (sed)', `timeout -k 1 ${r.agyLog} sed`],
    ['the log reader (tail -n)', `timeout -k 1 ${r.agyLog} tail -n 1`],
    ['the log reader (grep)', `timeout -k 1 ${r.agyLog} grep`],
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
  assert.deepEqual([CODEX_JSON_MAX, AGY_QUOTA_JSON_MAX, AGY_LOG_TAIL_MAX], [USAGE_JSON_MAX, USAGE_JSON_MAX, USAGE_JSON_MAX], 'the four 1 MiB reads are the same number');
  assert.equal(count(t, `tail -c ${USAGE_JSON_MAX}`), 4, 'the text holds the 1 MiB exactly four times: the usage pipe, Codex, agy\'s quota and agy\'s log');
  assert.equal(count(t, 'tail -n 1'), 1, 'the one line of the log');
  assert.equal(AGY_WINDOWS_MAX, USAGE_LINES_MAX);
  assert.equal(count(t, `1,${USAGE_LINES_MAX}p`), 2, 'the usage lines of Claude and the windows of agy');
  assert.equal(count(t, `-le ${USAGE_LINES_MAX}`), 1);
  assert.equal(count(t, `sent=${USAGE_LINES_MAX}`), 1);
  assert.ok(t.includes(`COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`));
  assert.equal(ACCOUNTS_REPLY_BYTES_MAX, (ACCOUNTS_LINES_MAX + 2) * (ACCOUNTS_LINE_BYTES_MAX + 1));
  assert.equal(ACCOUNTS_REPLY_BYTES_MAX, 33858);
  assert.equal(ACCOUNTS_LINES_MAX, 64);
  assert.equal(ACCOUNTS_LINE_BYTES_MAX, 512);
});

test('A2-T26: no Codex session file is read any more, every tail and the sleep follow their limits, and each reader\'s limit is at least its command\'s', () => {
  const t = ACCOUNTS_SCRIPT;
  for (const gone of ['ls -tr', '"$st" -c %Y', 'sessions', 'rollout', 'as-of', 'no-stat', 'no-sessions', 'uuid']) {
    assert.ok(!t.includes(gone), `the text still holds "${gone}", which served the session-file path`);
  }
  assert.equal(matches(t, /(?<!timeout -k 1 \d )tail -[cn] /gu), 0, 'a tail that does not follow a timeout -k 1 <n>... except the first of a pipe');
  const s = ACCOUNTS_SECONDS;
  const r = ACCOUNTS_READ_SECONDS;
  for (const part of ['account', 'version', 'usage', 'agy', 'codex', 'agyQuota'] as const) {
    assert.ok(r[part] >= s[part], `the reader of ${part} (${r[part]} s) ends before its command (${s[part]} s)`);
  }
  assert.ok(t.includes('        sleep 3\n'), 'the sleep that keeps stdin open for codex app-server');
  assert.ok(s.codex > 3, `codex app-server (${s.codex} s) must outlast the 3 s of its input`);
  assert.ok(r.agyQuota > s.agyQuota && r.codex > s.codex, 'the reader of a step with a KILL second is longer than its TERM');
});

const connectSeconds = (): number => {
  const word = SSH_OPTIONS.find((option) => /^ConnectTimeout=\d+$/u.test(option));
  assert.ok(word !== undefined, 'SSH_OPTIONS has no ConnectTimeout option');

  return Number(word?.split('=')[1]);
};

test('E3S2-T12 (T46, the sum): worstSeconds of each mode is the table of § R3.5 plus the connect time, and at most the ceiling minus 2 s', () => {
  const r = ACCOUNTS_READ_SECONDS;
  assert.equal(connectSeconds(), 5);
  assert.equal(ACCOUNTS_CONNECT_SECONDS, connectSeconds(), 'the connect seconds are the ConnectTimeout of SSH_OPTIONS');
  assert.equal(ACCOUNTS_CEILING_SECONDS, 30);
  assert.deepEqual({ ...ACCOUNTS_KILL_SECONDS }, { version: 1, agy: 1 });
  const table: Readonly<Record<(typeof ACCOUNTS_MODES)[number], number>> = { 'no-claude-usage': 21, 'claude-usage': 17 };
  const asRoot: Readonly<Record<(typeof ACCOUNTS_MODES)[number], number>> = { 'no-claude-usage': 24, 'claude-usage': 18 };
  for (const mode of ACCOUNTS_MODES) {
    assert.equal(worstSeconds(mode), table[mode] + ACCOUNTS_CONNECT_SECONDS, `${mode}: the table of § R3.5 says ${table[mode]} s and then the connect`);
    assert.ok(worstSeconds(mode) <= ACCOUNTS_CEILING_SECONDS - 2, `${mode}: ${worstSeconds(mode)} s is over the ceiling of ${ACCOUNTS_CEILING_SECONDS} s minus 2 s`);
    // as UID 0 the first line of each vendor program is read too (one second each, three in the first mode): the end line must still come before the runner's ceiling
    assert.equal(worstSeconds(mode, true), asRoot[mode] + ACCOUNTS_CONNECT_SECONDS, `${mode} as UID 0`);
    assert.ok(worstSeconds(mode, true) <= ACCOUNTS_CEILING_SECONDS - 1, `${mode} as UID 0: ${worstSeconds(mode, true)} s is over the ceiling of ${ACCOUNTS_CEILING_SECONDS} s minus 1 s`);
  }
  // the first mode, step by step, as the table says it: account 4; version 2 + 1; codex app-server 5; agy version 2 + 1, quota 5, log 1
  assert.equal(r.account + r.version + ACCOUNTS_KILL_SECONDS.version + r.codex + r.agy + ACCOUNTS_KILL_SECONDS.agy + r.agyQuota + r.agyLog, 21);
  // the usage mode: account 4; version 2 + 1; the digest 1; usage 9
  assert.equal(r.account + r.version + ACCOUNTS_KILL_SECONDS.version + r.digest + r.usage, 17);
  const s = ACCOUNTS_SECONDS;
  const first = s.account + s.version + s.codex + s.agy + s.agyQuota + 5 + connectSeconds();
  const usage = s.account + s.version + s.usage + 3 + connectSeconds();
  assert.ok(first < ACCOUNTS_CEILING_SECONDS && usage < ACCOUNTS_CEILING_SECONDS, `the limits of the first mode (${first} s) or of the usage mode (${usage} s), one KILL second each and the connect, reach the ceiling`);
});

test('A2-T26 / E3S2-T3: each tool of ACCOUNTS_TOOLS is in the text and no other is; the PATH line is ACCOUNTS_PATH; the manager words and paths are in the text in order', () => {
  const [first, ...others] = ACCOUNTS_TOOLS;
  assert.equal(first, 'id', 'id is the first tool and is checked by its own line');
  assert.equal(count(ACCOUNTS_SCRIPT, 'command -v id >/dev/null || { printf \'COAI-BAD tool\\n\'; return 0; }'), 1);
  const lines = ACCOUNTS_SCRIPT.split('\n');
  assert.ok(lines.includes(`  tools='${others.join(' ')}'`), 'the tools line does not list ACCOUNTS_TOOLS after id');
  assert.ok(lines.includes('  [ "$mode" != claude-usage ] || tools="$tools sha256sum"'), 'sha256sum is not checked in the usage mode');
  const setLines = lines.filter((one) => one.trimStart().startsWith('PATH='));
  assert.ok(setLines.includes(`  PATH=${ACCOUNTS_PATH}`), 'the PATH line is not ACCOUNTS_PATH');
  assert.equal(setLines.filter((one) => /^\s*PATH=\//u.test(one)).length, 1, 'more than one fixed PATH line');
  assert.ok(setLines.every((one) => one === `  PATH=${ACCOUNTS_PATH}` || /^ +PATH=(?:\$HOME\/\.local\/bin(?::\$PATH)?|\$PATH:\$HOME\/\.local\/bin)$/u.test(one)), 'a PATH line adds something that is not the home folder');
  const entries = ACCOUNTS_PATH.split(':');
  assert.equal(entries.length, 6);
  for (const entry of entries) {
    assert.ok(/^\/[a-z]/u.test(entry) && !/^\/(?:tmp|home|mnt)(?:\/|$)/u.test(entry) && !entry.includes('//') && !entry.endsWith('/'), `PATH entry ${JSON.stringify(entry)} is not a fixed system folder`);
  }
  assert.ok(ACCOUNTS_SCRIPT.includes('    *:*) printf \'COAI-BAD home\\n\'; return 0 ;;\n    /*) ;;'), 'the *:* row of the HOME check is not before /*');
  const manager = managerLines(ACCOUNTS_SCRIPT);
  const printed = manager.flatMap((one) => /printf '([a-z-]+)'/u.exec(one)?.[1] ?? []);
  assert.deepEqual(printed, MANAGER_WORDS, 'MANAGER_WORDS are not the words coai_manager prints, in that order');
  const folders = manager.flatMap((one) => /\[ -x (\S+) \]/u.exec(one)?.[1] ?? []);
  assert.deepEqual(folders, ['/usr/bin/apt-get', '/usr/bin/dnf', '/usr/bin/yum', '/sbin/apk'], 'the four absolute paths, in the order apt-get, dnf, yum, apk');
});
