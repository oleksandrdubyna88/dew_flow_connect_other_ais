import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { ChildProcess, spawn } from 'node:child_process';
import {
  accessSync, chmodSync, constants, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, symlinkSync, utimesSync, writeFileSync,
} from 'node:fs';
import { delimiter, join } from 'node:path';
import test, { TestContext } from 'node:test';
import { createHash } from 'node:crypto';
import { AccountsRead, readAccounts } from '../remoteAccountsReply';
import { ACCOUNTS_CONNECT_SECONDS, ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_SCRIPT, ACCOUNTS_SKIP_AGY, AccountsMode, accountsDigest, worstSeconds } from '../remoteAccountsScript';
import { LINES, NOW } from './accountsFrames';
import { ownedTemp } from './ownedTemp';

/**
 * The REAL accounts script (revision 3, E3.S2 and the owner's change of 2026-11-27), run by a real `sh -s` (and by `bash --posix`), against a temporary HOME with false `claude`, `codex` and
 * `agy` programs and, where the machine has one, the REAL `jq` (E1.S2, spec § A2.11.5; E3.S2, spec § R2.11). Its stdout is then read by the real parser.
 *
 * <p><b>The harness is the shape of `remoteScriptRuns.test.ts`</b> (a shell found once by its absolute path, one test for each shell, the child gets `HOME` and
 * `PATH` and nothing else). <b>The script no longer reads the inherited `PATH`</b>: it sets its own, the six system folders, and `$HOME/.local/bin` first. So a false
 * program stands in `$HOME/.local/bin` (the false `claude`, `agy`, `codex`, and a link to the real `jq`), and the caller's `PATH` of the child is a folder of decoys
 * that must never be started. <b>A tool is hidden by a prelude</b>, sent before the pinned text: a shell function `command` that answers `command -v <name>` with
 * "not there" for the names the test hides and walks the `PATH` for every other. The pinned text is unchanged and is what runs.</p>
 *
 * <p>Each false program appends ONE line to `fx/log` — its name, its pid, every argument in brackets, its folder, what its stdin is and its `TZ` — and writes
 * its whole environment to `fx/env.<pid>`.</p>
 *
 * <p>Rows marked jq need the real `jq` and are skipped on a machine that has none, with the reason `no jq on this machine`; on the Linux runner of CI a missing
 * `sh` or `jq` FAILS (A2-T23). <b>What a fake cannot prove</b> is in the specs: the real `claude` and `agy` programs, GNU `timeout` on a real VM, a real UID 0.</p>
 */

const SHELL_ARGS = ['-s', '--'] as const;
const NO_SH = 'no POSIX sh on this machine';
const NO_JQ = 'no jq on this machine';

/** A file of the PATH of this test run, or nothing. */
function onPath(name: string): string | undefined {
  return (process.env['PATH'] ?? '').split(delimiter).map((dir) => join(dir, name)).find((path) => existsSync(path));
}

interface Shell { readonly label: string; readonly path: string; readonly args: readonly string[] }

const SHELLS: readonly Shell[] = ((): readonly Shell[] => {
  const found: Shell[] = [];
  const sh = onPath('sh');
  const bash = onPath('bash');
  if (sh !== undefined) {
    found.push({ label: 'sh', path: sh, args: [] });
  }
  if (bash !== undefined) {
    found.push({ label: 'bash --posix', path: bash, args: ['--posix'] });
  }

  return found;
})();

const JQ = onPath('jq');
let jqRows = 0;

/** One test for each shell that is there; with none, one skipped test that says why. */
function shellTest(name: string, body: (shell: Shell, context: TestContext) => Promise<void>, options: { readonly skip?: string; readonly timeout?: number } = {}): void {
  if (SHELLS.length === 0) {
    test(name, { skip: NO_SH }, () => undefined);

    return;
  }
  for (const shell of SHELLS) {
    const settings = { timeout: options.timeout ?? 20_000, ...(options.skip === undefined ? {} : { skip: options.skip }) };
    test(`${name} [${shell.label}]`, settings, (context) => body(shell, context));
  }
}

/** A row that needs the real `jq`: skipped here, run on CI. */
function jqTest(name: string, body: (shell: Shell, context: TestContext) => Promise<void>, options: { readonly timeout?: number } = {}): void {
  jqRows += 1;
  shellTest(name, body, { ...options, ...(JQ === undefined ? { skip: NO_JQ } : {}) });
}

test('A2-T23: on the Linux runner of CI the sh and jq rows RUN — a missing sh or jq is a failure, not a skip', () => {
  if (process.env['CI'] === 'true' && process.platform === 'linux') {
    assert.ok(SHELLS.some((shell) => shell.label === 'sh'), `${NO_SH}: the sh rows of this file would be skipped in CI`);
    assert.ok(JQ !== undefined, `${NO_JQ}: the jq rows of this file would be skipped in CI`);
  }
  assert.ok(jqRows > 5, `this file has ${jqRows} rows marked jq; the gate would pass on an empty set`);
});

// ───────────────── the tree and the false programs ─────────────────

/** `bin`: the folder of the CALLER's `PATH` — decoys the script must never start. `local`: `$HOME/.local/bin`, first on the script's own list. */
interface Tree { readonly root: string; readonly home: string; readonly local: string; readonly bin: string; readonly fx: string; readonly hide: readonly string[]; prelude: string }

const ACCOUNT = {
  loggedIn: true, authMethod: 'claude.ai', apiProvider: 'firstParty', analyticsDisabled: false, projectsDirectory: '/x', configDirectory: '/y',
  loginExpiresAt: null, forcedLoginMethod: null, email: 'someone@example.com', orgId: 'ORG-ID-MARK', orgName: 'Example Org',
  subscriptionType: 'team', storedClaudeAiLogin: true, planTier: 'other',
};

/** The common start of a false program: its own PATH, and ONE log line. */
const logged = (name: string, fx: string): string => `#!/bin/sh
PATH=/usr/bin:/bin
args=''
for a in "$@"; do args="$args[$a]"; done
echo "${name} $$ $args cwd=$(pwd) stdin=$(readlink /proc/$$/fd/0) tz=\${TZ-unset}" >> "${fx}/log"
env > "${fx}/env.$$"
`;

const CLAUDE = (fx: string): string => `${logged('claude', fx)}case "$1" in
  auth) part=auth ;;
  --version) part=version ;;
  -p) part=usage ;;
  *) part=other ;;
esac
[ -e "${fx}/hang-$part" ] && exec sleep 100
[ -e "${fx}/deaf-$part" ] && { trap '' TERM; while :; do sleep 1 >/dev/null 2>&1; done; }
if [ -e "${fx}/slow-$part" ]; then
  delay=$(cat "${fx}/slow-$part")
  sleep "\${delay:-0.3}"
fi
if [ -e "${fx}/child-$part" ]; then
  sleep 100 &
  echo $! >> "${fx}/children"
  wait
fi
[ -f "${fx}/claude.$part" ] && cat "${fx}/claude.$part"
code=0
[ -f "${fx}/claude.$part.code" ] && code=$(cat "${fx}/claude.$part.code")
exit $code
`;

const AGY = (fx: string): string => `${logged('agy', fx)}case "$1" in
  --version) part=version ;;
  -p) part=quota ;;
  *) part=other ;;
esac
[ -e "${fx}/hang-agy" ] && exec sleep 100
[ -e "${fx}/hang-$part" ] && exec sleep 100
[ -e "${fx}/deaf-agy" ] && { trap '' TERM; while :; do sleep 1 >/dev/null 2>&1; done; }
[ -e "${fx}/deaf-$part" ] && { trap '' TERM; while :; do sleep 1 >/dev/null 2>&1; done; }
[ -f "${fx}/agy.$part" ] && cat "${fx}/agy.$part"
code=0
[ -f "${fx}/agy.$part.code" ] && code=$(cat "${fx}/agy.$part.code")
exit $code
`;

/** The false `codex app-server`: it prints the lines of `fx/codex.out` and exits; it reads nothing, as the script's own `sleep` keeps the pipe open. */
const CODEX = (fx: string): string => `${logged('codex', fx)}[ -e "${fx}/hang-codex" ] && exec sleep 100
[ -e "${fx}/deaf-codex" ] && { trap '' TERM; while :; do sleep 1 >/dev/null 2>&1; done; }
[ -f "${fx}/codex.out" ] && cat "${fx}/codex.out"
code=0
[ -f "${fx}/codex.code" ] && code=$(cat "${fx}/codex.code")
exit $code
`;

/** Writes a file. A link of that name is removed first, so a write never goes THROUGH it to the real tool. */
function write(path: string, text: string, mode = 0o644): void {
  mkdirSync(join(path, '..'), { recursive: true });
  rmSync(path, { force: true });
  writeFileSync(path, text, { mode });
}

interface TreeOptions {
  /** A link to the real `jq` stands in `$HOME/.local/bin`; without it `jq` is hidden by the prelude, whatever the machine holds. */
  readonly jq?: boolean;
  /** Names the prelude answers "not there" for, beside `jq` when `jq` is not wanted. */
  readonly hide?: readonly string[];
}

/** A shell function `command` for the names to hide: `command -v <name>` says "not there" for them and walks the `PATH` for every other. */
function hiding(names: readonly string[]): string {
  if (names.length === 0) {
    return '';
  }

  return `command() {
  [ "$1" = -v ] || return 127
  case " ${names.join(' ')} " in *" $2 "*) return 1 ;; esac
  coai_pre_path=$PATH
  while [ -n "$coai_pre_path" ]; do
    coai_pre_dir=\${coai_pre_path%%:*}
    case $coai_pre_path in *:*) coai_pre_path=\${coai_pre_path#*:} ;; *) coai_pre_path= ;; esac
    if [ -f "$coai_pre_dir/$2" ] && [ -x "$coai_pre_dir/$2" ]; then
      printf '%s\\n' "$coai_pre_dir/$2"
      return 0
    fi
  done
  return 1
}
`;
}

/** A HOME with `.local/bin` (the false programs), a decoy folder for the caller's PATH, and the fakes' own folder. */
function tree(options: TreeOptions = {}): Tree {
  // a short name: a path whose one segment is 40 characters of letters and digits is shaped like a credential, and the parser shows it as `-` (§ R2.7)
  const root = ownedTemp('acc');
  const hide = [...(options.hide ?? []), ...(options.jq === true ? [] : ['jq'])];
  const t: Tree = { root, home: join(root, 'home'), local: join(root, 'home', '.local', 'bin'), bin: join(root, 'bin'), fx: join(root, 'fx'), hide, prelude: `${hiding(hide)}${FAST_SLEEP}` };
  for (const dir of [t.local, t.bin, t.fx]) {
    mkdirSync(dir, { recursive: true });
  }
  if (options.jq === true && JQ !== undefined) {
    symlinkSync(JQ, join(t.local, 'jq'));
  }
  write(join(t.local, 'claude'), CLAUDE(t.fx), 0o755);
  write(join(t.local, 'agy'), AGY(t.fx), 0o755);
  write(join(t.local, 'codex'), CODEX(t.fx), 0o755);
  write(join(t.fx, 'agy.version'), 'Antigravity CLI v1.5.12 (stable)\n');
  write(join(t.fx, 'agy.quota'), quotaJson());
  write(join(t.fx, 'codex.out'), codexOut());
  agyLog(t, AGY_MAIL);

  return t;
}

const AGY_MAIL = 'agy@example.test';
const CODEX_MAIL = 'cox@example.test';

/** What `codex app-server` printed in the measurement: two replies and a notification between them; the fields no pattern names carry marks. */
const codexOut = (over: { readonly email?: unknown; readonly primary?: unknown; readonly secondary?: unknown; readonly plan?: unknown; readonly rateLimits?: unknown; readonly account?: unknown } = {}): string => [
  { id: 1, result: { userAgent: 'coai/0.171.0', codexHome: '/x', platformFamily: 'unix', platformOs: 'linux' } },
  { method: 'remoteControl/status/changed', params: { status: 'disabled', installationId: 'INSTALLATION-MARK' } },
  { id: 3, result: { ordinaryUsageAllowed: true, rateLimits: over.rateLimits ?? { limitId: 'codex', primary: over.primary ?? { usedPercent: 19, windowDurationMins: 10080, resetsAt: 1796123826 }, secondary: over.secondary ?? null, planType: over.plan ?? 'prolite' }, rateLimitResetCredits: { credits: [{ id: 'RESET-CREDIT-MARK' }] } } },
  { method: 'account/updated', params: { authMode: 'chatgpt', planType: 'prolite' } },
  { id: 2, result: { account: over.account ?? { type: 'chatgpt', email: 'email' in over ? over.email : CODEX_MAIL, planType: 'prolite', workspaceName: null }, userId: 'USER-ID-MARK', workspaceRouting: { chatgptAccountId: 'ACCOUNT-ID-MARK' } } },
].map((one) => JSON.stringify(one)).join('\n') + '\n';

/** The measured output of `agy -p /quota --output-format json`: two groups of two buckets; the third-party ones are full (the epoch is "not started"). */
const quotaJson = (over: Readonly<Record<string, unknown>> = {}, buckets?: readonly unknown[]): string => JSON.stringify({
  conversation_id: '', status: 'SUCCESS', response: 'RESPONSE-MARK', duration_seconds: 0, num_turns: 0,
  usage: { input_tokens: 0, output_tokens: 0, total_tokens: 0 },
  command: {
    name: 'usage',
    data: {
      description: 'DESCRIPTION-MARK',
      groups: [{ name: 'Gemini Models', description: 'GROUP-MARK', buckets: buckets ?? [
        { id: 'gemini-weekly', name: 'Weekly Limit Remaining', window: 'weekly', remaining_fraction: 0.5998565554618835, reset_time: '2026-12-03T07:42:14Z' },
        { id: 'gemini-5h', name: 'Five Hour Limit Remaining', window: '5h', remaining_fraction: 0.920835018157959, reset_time: '2026-11-27T21:00:41Z' },
      ] }, { name: 'Claude and GPT models', buckets: [
        { id: '3p-weekly', window: 'weekly', remaining_fraction: 1, reset_time: '1970-01-01T00:00:00Z' },
        { id: '3p-5h', window: '5h', remaining_fraction: 1, reset_time: '1970-01-01T00:00:00Z' },
      ] }],
    },
  },
  ...over,
}) + '\n';

/** agy's log: the real file is in `log/` and `cli.log` is a link to it. The lines are agy's own; the secret mark is not an account. */
function agyLog(t: Tree, email: string, extra: readonly string[] = []): void {
  const folder = join(t.home, '.gemini', 'antigravity-cli');
  write(join(folder, 'log', 'cli-1.log'), [`I1127 17:23:21.000000 1 server.go:1] started LOG-SECRET-MARK`, `I1127 17:23:22.016270 1 server_oauth.go:221] applyAuthResult: email=${email}, authMethod=consumer, quotaProject=`, ...extra, ''].join('\n'));
  rmSync(join(folder, 'cli.log'), { force: true });
  symlinkSync('log/cli-1.log', join(folder, 'cli.log'));
}

/** The shell function that makes the script's `sleep 2` instant: the false programs need no input, and a run is two seconds shorter. A run that measures time does not have it. */
const FAST_SLEEP = 'sleep() { :; }\n';

const put = (t: Tree, name: string, text: string): void => write(join(t.fx, name), text);

const USAGE_TEXT = (extra = ''): string => `You are currently using your subscription to power your Claude Code usage\n\n${LINES.join('\n')}\n\nWhat's contributing to your usage?\n${extra}`;
const usageJson = (result: string, over: Readonly<Record<string, unknown>> = {}): string => JSON.stringify({
  type: 'result', subtype: 'success', is_error: false, num_turns: 0, total_cost_usd: 0, duration_api_ms: 0, result, ...over,
});

/** The measured fixtures of Claude Code: the 14 keys of the account JSON, the usage JSON, the version line. */
function claudeFixtures(t: Tree, over: { readonly auth?: string; readonly usage?: string } = {}): void {
  put(t, 'claude.auth', over.auth ?? JSON.stringify(ACCOUNT));
  put(t, 'claude.version', '2.1.326 (Claude Code)\n');
  put(t, 'claude.usage', over.usage ?? usageJson(USAGE_TEXT('Requests in the last 24 hours: 3\n')));
}

/**
 * The digest the first reply of a place with this account makes (§ R2.2, D5): the sha256 of the account lines as the script prints them. Made here from the fixture, so a
 * test of the usage mode does not need the first mode to have run (it would start `claude` once more and move every count).
 */
function digestOf(t: Tree): string {
  const path = join(t.fx, 'claude.auth');
  const account = ((): Record<string, unknown> | undefined => {
    try {
      return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) as Record<string, unknown> : undefined;
    } catch {
      return undefined;
    }
  })();
  if (account === undefined || account['loggedIn'] !== true) {
    return '0'.repeat(64);
  }
  const key = (name: string, value: unknown): readonly string[] => (typeof value === 'string' && value !== '' ? [`:${name} ${value}`] : []);
  const lines = ['COAI-VENDOR claude found', ...key('email', account['email']), ...key('org', account['orgName']), ...key('plan', account['subscriptionType'])];

  return createHash('sha256').update(`${lines.join('\n')}\n`).digest('hex');
}

/** The operands of a call: the mode, and for the usage mode the digest of the place's fixture. */
const operandsOf = (t: Tree, mode: AccountsMode): readonly string[] => (mode === 'claude-usage' ? [mode, digestOf(t)] : [mode]);

interface Outcome { readonly code: number; readonly stdout: string; readonly stderr: string }

function started(shell: Shell, t: Tree, operands: readonly string[]): ChildProcess {
  return spawn(shell.path, [...shell.args, ...SHELL_ARGS, ...operands], { cwd: t.root, env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'pipe'] });
}

/** Run a script by `<shell> -s -- <operands>` on stdin; the environment holds NOTHING but HOME and PATH (the caller's PATH is a folder of decoys). The pinned text gets the tree's prelude first. */
function run(shell: Shell, t: Tree, operands: readonly string[], script: string = ACCOUNTS_SCRIPT, limit = 25_000): Promise<Outcome> {
  const sent = script === ACCOUNTS_SCRIPT ? `${t.prelude}${script}` : script;

  return new Promise((resolve, reject) => {
    const child = started(shell, t, operands);
    let stdout = '';
    let stderr = '';
    child.stdout?.setEncoding('utf8');
    child.stdout?.on('data', (text: string) => {
      stdout += text;
    });
    child.stderr?.setEncoding('utf8');
    child.stderr?.on('data', (text: string) => {
      stderr += text;
    });
    const timer = setTimeout(() => {
      child.kill('SIGKILL');
      reject(new Error(`the script did not end within ${limit / 1000} s: ${stdout.slice(-300)}`));
    }, limit);
    child.on('error', (error) => {
      clearTimeout(timer);
      reject(new Error(`the shell could not be started: ${error.message}`));
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code: code ?? -1, stdout, stderr });
    });
    child.stdin?.on('error', () => undefined);
    child.stdin?.end(sent);
  });
}

const logLines = (t: Tree): readonly string[] => (existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8').split('\n').filter((line) => line.length > 0) : []);
const calls = (t: Tree, name: string): readonly string[] => logLines(t).filter((line) => line.startsWith(`${name} `));
const argsOf = (line: string): string => line.split(' ')[2] ?? '';
const envFiles = (t: Tree): readonly string[] => readdirSync(t.fx).filter((name) => name.startsWith('env.')).map((name) => readFileSync(join(t.fx, name), 'utf8'));
const lines = (outcome: Outcome): readonly string[] => outcome.stdout.split('\n').filter((line) => line.length > 0);

type FirstRead = Extract<AccountsRead, { kind: 'read' }>;
type UsageRead = Extract<AccountsRead, { kind: 'claude-only' }>;

/** The first mode, run and read: always the `read` arm. */
async function read(shell: Shell, t: Tree, mode: 'no-claude-usage'): Promise<{ readonly outcome: Outcome; readonly got: FirstRead }> {
  const outcome = await run(shell, t, operandsOf(t, mode));
  const got = readAccounts(outcome, mode, NOW);
  assert.equal(got.kind, 'read', `the reply is not read (${JSON.stringify(got)}): ${outcome.stdout.slice(0, 400)}`);

  return { outcome, got: got as FirstRead };
}

/** The usage mode, run and read: always the `claude-only` arm. */
async function readUsage(shell: Shell, t: Tree): Promise<{ readonly outcome: Outcome; readonly got: UsageRead }> {
  const outcome = await run(shell, t, operandsOf(t, 'claude-usage'));
  const got = readAccounts(outcome, 'claude-usage', NOW);
  assert.equal(got.kind, 'claude-only', `the reply is not read (${JSON.stringify(got)}): ${outcome.stdout.slice(0, 400)}`);

  return { outcome, got: got as UsageRead };
}

const whole = (outcome: Outcome): void => {
  assert.ok(outcome.stdout.startsWith('COAI-ACCOUNTS 1\n') && outcome.stdout.endsWith('COAI-ACCOUNTS-END\n'), `the frame is not whole: ${JSON.stringify(outcome.stdout.slice(-200))}`);
  assert.equal(outcome.code, 0);
};

const MODES: readonly AccountsMode[] = ['claude-usage', 'no-claude-usage'];

// ───────────────── A2-T1: one function, called last; a cut does nothing ─────────────────

/** The text with the two `/dev/null` of its LAST line written as a temporary folder's, so a cut inside the path writes under that folder only. */
function derived(text: string, folder: string): string {
  // The function is the first lines; what comes after it is the call, and only there the path is mapped (the function's own >/dev/null stay).
  return text.split('\n').map((line, index) => (index >= FUNCTION_LINES ? line.replaceAll('/dev/null', `${folder}/dev/null`) : line)).join('\n');
}

/** The lines of the function: every line but the last, which is the call with its end line (and the empty text after the last LF). */
const FUNCTION_LINES = ACCOUNTS_SCRIPT.split('\n').length - 2;

const entries = (folder: string): readonly string[] => readdirSync(folder).sort();

/** A folder that holds `dev/null` (an empty file) and nothing else. */
function scratch(): string {
  const folder = ownedTemp('accounts-dev');
  assert.ok(!/[\s'"\\$`]/u.test(folder), `the scratch folder ${folder} holds a character the derived text cannot carry`);
  write(join(folder, 'dev', 'null'), '');

  return folder;
}

shellTest('A2-T1: a whole run starts with the begin line, ends with the end line, and exits 0', async (shell) => {
  const t = tree();
  whole(await run(shell, t, ['no-claude-usage']));
});

shellTest('A2-T1: a script cut after any line of its function, or at any byte of its last line, does nothing', async (shell) => {
  const t = tree();
  const folder = scratch();
  const text = derived(ACCOUNTS_SCRIPT, folder);
  const all = text.split('\n');
  const silent = (outcome: Outcome, what: string): void => {
    assert.equal(outcome.stdout, '', `${what}: a cut text printed a line`);
    assert.deepEqual(logLines(t), [], `${what}: a cut text started a program (with the script as its stdin, or with any stdin)`);
  };
  const last = all[FUNCTION_LINES] as string;
  const prefix = `${all.slice(0, FUNCTION_LINES).join('\n')}\n`;
  const cuts: { readonly what: string; readonly script: string; readonly byLine: boolean }[] = [
    ...Array.from({ length: FUNCTION_LINES }, (_, index) => ({ what: `after line ${index + 1}`, script: `${all.slice(0, index + 1).join('\n')}\n`, byLine: true })),
    ...Array.from({ length: last.length - 1 }, (_, index) => ({ what: `at byte ${index + 1} of the last line`, script: prefix + last.slice(0, index + 1), byLine: false })),
  ];
  // Six at a time: every cut is its own shell, and none of them may start a program or write a path, so they do not meet.
  const outcomes: Outcome[] = [];
  for (let at = 0; at < cuts.length; at += 6) {
    outcomes.push(...await Promise.all(cuts.slice(at, at + 6).map((cut) => run(shell, t, ['claude-usage', '0'.repeat(64)], cut.script))));
  }
  // A path that a cut created is the worst of the faults, so it is said first, with its name.
  assert.deepEqual(entries(folder), ['dev'], `a cut text created a path under ${folder}: ${entries(folder).join(', ')}`);
  assert.deepEqual(entries(join(folder, 'dev')), ['null'], `a cut text created ${folder}/dev/${entries(join(folder, 'dev')).join(', ')}`);
  cuts.forEach((cut, index) => {
    const outcome = outcomes[index] as Outcome;
    silent(outcome, cut.what);
    if (!cut.byLine) {
      assert.notEqual(outcome.code, 0, `${cut.what}: the shell did not say that the group was cut`);
      assert.deepEqual(readAccounts(outcome, 'claude-usage', NOW), { kind: 'unreadable', why: 'exit' }, 'the parser says exit for the empty stdout of a cut script');
    }
  });
  for (const tail of ['', '\n']) {
    const outcome = await run(shell, t, ['no-claude-usage'], `${text.trimEnd()}${tail}`);
    whole(outcome);
    assert.equal(readAccounts(outcome, 'no-claude-usage', NOW).kind, 'read', 'the whole last line, with or without its line end, gives a whole reply');
  }
  assert.deepEqual(readAccounts({ code: 0, stdout: 'COAI-ACCOUNTS 1\nCOAI-JQ found\n' }, 'claude-usage', NOW), { kind: 'unreadable', why: 'cut' }, 'a stdout that lost its end line is cut');
}, { timeout: 60_000 });

shellTest('A2-T1 companion: the last line as TWO lines (the form before gate G2) creates a path when it is cut inside the path of 2>', async (shell) => {
  const t = tree();
  const folder = scratch();
  const base = ACCOUNTS_SCRIPT.split('\n').slice(0, FUNCTION_LINES);
  const call = `</dev/null 2>${folder}/dev/null coai_accounts "$@"`;
  const from = call.indexOf(`${folder}/dev/null`) + folder.length;
  for (let k = from + 2; k < from + '/dev/null'.length; k += 1) {
    await run(shell, t, ['no-claude-usage'], `${base.join('\n')}\n${call.slice(0, k)}`);
  }
  const made = ['d', 'de', 'dev/n', 'dev/nu', 'dev/nul'].filter((name) => existsSync(join(folder, name)));
  assert.ok(made.length >= 1, 'no cut of the two-line form created a path, so the test cannot see a created path');
});

// ───────────────── A2-T3 / E3S2-T4 (the sh half): the operands ─────────────────

const D64 = '0123456789abcdef'.repeat(4);

shellTest('E3S2-T4: the operands — a wrong or missing one starts nothing and says so; the right forms start what they should', async (shell) => {
  const wrong: readonly (readonly string[])[] = [
    [], ['usage'], ['CLAUDE-USAGE'], ['claude-usage;id'], ['-x'],
    ['no-claude-usage', D64],
    ['no-claude-usage', ''],
    ['claude-usage'],
    ['claude-usage', D64.slice(1)],
    ['claude-usage', `${D64}0`],
    ['claude-usage', D64.toUpperCase()],
    ['claude-usage', `${D64.slice(0, 63)} `],
    ['claude-usage', 'x'],
    ['claude-usage', D64, 'extra'],
    ['claude-usage', 'someone@example.com'],
    ['claude-usage', 'no-agy-quota'],
    ['no-claude-usage', 'no-agy-quota', 'extra'],
    ['no-claude-usage', 'no-agy-quota '],
    ['no-claude-usage', 'NO-AGY-QUOTA'],
    ['no-claude-usage', 'no-agy-quota;id'],
    ['no-claude-usage', D64, 'no-agy-quota'],
  ];
  for (const operands of wrong) {
    const t = tree();
    const outcome = await run(shell, t, operands);
    assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD operands', 'COAI-ACCOUNTS-END'], `a wrong operand ${JSON.stringify(operands)} was not refused`);
    assert.deepEqual(logLines(t), [], `${JSON.stringify(operands)}: a program was started`);
    assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'operands' });
  }
  const t = tree();
  await run(shell, t, ['no-claude-usage']);
  assert.equal(calls(t, 'agy').length, 1, 'the first mode did not start agy (a wrong operand started it, or the operand case is gone)');
});

jqTest('R3-T4: the second operand no-agy-quota — agy is asked its version and NOT its quota, the reply says skipped, and the account is still read from the log', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const asked = await run(shell, t, ['no-claude-usage']);
  assert.deepEqual(calls(t, 'agy').map(argsOf), [VERSION_ARGS, QUOTA_ARGS], 'without the operand agy is asked its version and its quota');
  assert.ok(lines(asked).includes('COAI-USAGE agy read'));
  const skip = tree({ jq: true });
  claudeFixtures(skip);
  const outcome = await run(shell, skip, ['no-claude-usage', ACCOUNTS_SKIP_AGY]);
  whole(outcome);
  assert.deepEqual(calls(skip, 'agy').map(argsOf), [VERSION_ARGS], 'agy was asked its quota with the operand that says not to');
  assert.ok(lines(outcome).includes('COAI-USAGE agy skipped') && !outcome.stdout.includes(':quota '), outcome.stdout);
  const got = readAccounts(outcome, 'no-claude-usage', NOW);
  assert.ok(got.kind === 'read' && got.agy.state === 'found' && got.agy.usage.state === 'skipped' && got.agy.email === AGY_MAIL, JSON.stringify(got));
  assert.equal(calls(skip, 'codex').length, 1, 'the operand does not touch Codex');
});

shellTest('E3S2-T3: HOME that holds a colon is COAI-BAD home and starts nothing; a relative HOME too', async (shell) => {
  for (const home of ['/a:/b', '/a:', ':/a', 'relative', '']) {
    const t = tree();
    const child = spawn(shell.path, [...shell.args, ...SHELL_ARGS, 'no-claude-usage'], { cwd: t.root, env: { HOME: home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '';
    child.stdout?.setEncoding('utf8');
    child.stdout?.on('data', (text: string) => { stdout += text; });
    const ended = new Promise<void>((resolve) => child.on('close', () => resolve()));
    child.stdin?.on('error', () => undefined);
    child.stdin?.end(ACCOUNTS_SCRIPT);
    await ended;
    assert.deepEqual(stdout.split('\n').filter((line) => line.length > 0), ['COAI-ACCOUNTS 1', 'COAI-BAD home', 'COAI-ACCOUNTS-END'], JSON.stringify(home));
    assert.deepEqual(logLines(t), [], `${JSON.stringify(home)}: a program was started`);
  }
});

// ───────────────── E3S2-T2: the inherited PATH is not used ─────────────────

jqTest('E3S2-T2: a false claude that is only in a folder of the caller\'s PATH records no start, and the reply says absent', async (shell) => {
  for (const mode of MODES) {
    const t = tree({ jq: true });
    claudeFixtures(t);
    // the only claude and agy are in the folder the CALLER's PATH names (`bin`), never in $HOME/.local/bin
    renameSync(join(t.local, 'claude'), join(t.bin, 'claude'));
    renameSync(join(t.local, 'agy'), join(t.bin, 'agy'));
    const outcome = await run(shell, t, operandsOf(t, mode));
    whole(outcome);
    assert.ok(lines(outcome).includes('COAI-VENDOR claude absent'), `${mode}: ${outcome.stdout}`);
    assert.deepEqual(calls(t, 'claude'), [], `${mode}: a claude of the caller's PATH was started`);
    assert.deepEqual(calls(t, 'agy'), [], `${mode}: an agy of the caller's PATH was started`);
  }
});

// ───────────────── E3S2-T5: the usage mode reads Claude only ─────────────────

jqTest('E3S2-T5: the usage mode reads Claude only — the false agy records no start, no line names Codex, and the frame holds the JQ line and the Claude part', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const { outcome, got } = await readUsage(shell, t);
  assert.deepEqual(calls(t, 'agy'), [], 'the false agy was started in the usage mode');
  assert.deepEqual(calls(t, 'codex'), []);
  assert.ok(!/codex|agy|:window|:quota/iu.test(outcome.stdout), `a line of the reply names Codex or agy: ${outcome.stdout}`);
  const heads = lines(outcome).filter((line) => line.startsWith('COAI-'));
  assert.deepEqual(heads, ['COAI-ACCOUNTS 1', 'COAI-JQ found', 'COAI-VENDOR claude found', 'COAI-USAGE claude read', 'COAI-ACCOUNTS-END']);
  assert.equal(got.claude.state, 'found');
});

// ───────────────── E3S2-T6, T7: the account check, the digest fits or does not ─────────────────

jqTest('E3S2-T6: the digest fits — the false claude records ONE start with -p /usage, and the reply holds COAI-USAGE claude read', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const { outcome, got } = await readUsage(shell, t);
  const usage = calls(t, 'claude').filter((line) => argsOf(line).startsWith('[-p]'));
  assert.equal(usage.length, 1, `the usage command was started ${usage.length} times`);
  assert.equal(argsOf(usage[0] as string), '[-p][/usage][--output-format][json][--no-session-persistence][--strict-mcp-config][--setting-sources][]');
  assert.ok(lines(outcome).includes('COAI-USAGE claude read'), outcome.stdout);
  assert.ok(got.claude.state === 'found' && got.claude.usage.state === 'read');
  assert.ok(!outcome.stdout.includes(digestOf(t)), 'the digest is in a line of the reply');
});

jqTest('E3S2-T7: another digest — skipped account-changed, and the false claude records NO start with -p', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const other = digestOf(t).replace(/^./u, (c) => (c === '0' ? '1' : '0'));
  const outcome = await run(shell, t, ['claude-usage', other]);
  whole(outcome);
  assert.ok(lines(outcome).includes('COAI-USAGE claude skipped account-changed'), outcome.stdout);
  assert.equal(calls(t, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0, 'the usage command ran for another digest');
  const got = readAccounts(outcome, 'claude-usage', NOW);
  assert.ok(got.kind === 'claude-only' && got.claude.state === 'found' && got.claude.usage.state === 'skipped' && 'why' in got.claude.usage, JSON.stringify(got));
  // the same account with another plan is another account for the check
  const plan = tree({ jq: true });
  claudeFixtures(plan);
  const digest = digestOf(plan);
  claudeFixtures(plan, { auth: JSON.stringify({ ...ACCOUNT, subscriptionType: 'max' }) });
  const changed = await run(shell, plan, ['claude-usage', digest]);
  assert.ok(lines(changed).includes('COAI-USAGE claude skipped account-changed'), changed.stdout);
  assert.equal(calls(plan, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0);
});

// ───────────────── E3S2-T8: accountsDigest equals sha256sum of the lines the real script printed ─────────────────

jqTest('E3S2-T8: accountsDigest equals sha256sum of the lines the real script printed — all three keys, the email only, none; the version line does not change it', async (shell) => {
  const variants: readonly (readonly [string, Readonly<Record<string, unknown>>])[] = [
    ['all three keys', ACCOUNT],
    ['the email only', { loggedIn: true, email: 'only@example.com' }],
    ['none of them', { loggedIn: true }],
    ['a value with a format character', { ...ACCOUNT, orgName: 'Org\u202eEvil' }],
  ];
  for (const [name, account] of variants) {
    const t = tree({ jq: true });
    claudeFixtures(t, { auth: JSON.stringify(account) });
    const outcome = await run(shell, t, ['no-claude-usage']);
    const digest = accountsDigest(outcome.stdout);
    assert.match(digest ?? '', /^[0-9a-f]{64}$/u, name);
    // the lines as printed: the marker and the key lines after it, each with its LF
    const all = outcome.stdout.split('\n');
    const at = all.indexOf('COAI-VENDOR claude found');
    const kept = [all[at] as string];
    for (const line of all.slice(at + 1)) {
      if (!/^:(?:email|org|plan) /u.test(line)) {
        break;
      }
      kept.push(line);
    }
    assert.equal(digest, createHash('sha256').update(`${kept.join('\n')}\n`).digest('hex'), name);
    // the digest the SCRIPT would accept: a usage call with it starts the usage command (the two sides hash the same bytes)
    const usage = await run(shell, t, ['claude-usage', digest as string]);
    assert.ok(lines(usage).includes('COAI-USAGE claude read') || lines(usage).includes('COAI-USAGE claude failed error'), `${name}: the script did not accept the digest: ${usage.stdout}`);
    assert.ok(!lines(usage).includes('COAI-USAGE claude skipped account-changed'), `${name}: the script judged the account changed`);
    // the version line does not change it
    const withVersion = outcome.stdout.replace('COAI-USAGE claude skipped', ':version 9.9.9\nCOAI-USAGE claude skipped');
    assert.equal(accountsDigest(withVersion), digest, `${name}: the :version line moved the digest`);
  }
  // no found marker, no frame
  assert.equal(accountsDigest('COAI-ACCOUNTS 1\nCOAI-JQ found\nCOAI-VENDOR claude signed-out\nCOAI-ACCOUNTS-END\n'), undefined);
  assert.equal(accountsDigest('COAI-VENDOR claude found\n:email a@b\n'), undefined);
  assert.equal(accountsDigest(''), undefined);
});

// ───────────────── E3S2-T11: a tool hidden by the prelude ─────────────────

shellTest('E3S2-T11: the usage mode with sha256sum hidden → COAI-BAD tool; the first mode there reads the three parts', async (shell) => {
  const hidden = tree({ hide: ['sha256sum'] });
  const outcome = await run(shell, hidden, ['claude-usage', D64]);
  assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD tool', 'COAI-ACCOUNTS-END']);
  assert.deepEqual(logLines(hidden), []);
  assert.deepEqual(readAccounts(outcome, 'claude-usage', NOW), { kind: 'unreadable', why: 'tool' });
  const first = tree({ hide: ['sha256sum'] });
  const got = (await read(shell, first, 'no-claude-usage')).got;
  assert.equal(got.agy.state, 'found', 'a place without sha256sum does not show its accounts');
  // every tool of the list, hidden, is COAI-BAD tool (id included), and nothing starts
  for (const tool of ['id', 'timeout', 'tail', 'sleep', 'sed', 'grep']) {
    const t = tree({ hide: [tool] });
    const out = await run(shell, t, ['no-claude-usage']);
    assert.deepEqual(lines(out), ['COAI-ACCOUNTS 1', 'COAI-BAD tool', 'COAI-ACCOUNTS-END'], tool);
    assert.deepEqual(logLines(t), [], tool);
  }
});

jqTest('E3S2-T11: jq hidden → COAI-JQ missing <word>', async (shell) => {
  const t = tree({ jq: true, hide: ['jq'] });
  const outcome = await run(shell, t, ['no-claude-usage']);
  const word = lines(outcome).find((line) => line.startsWith('COAI-JQ missing '));
  assert.match(word ?? '', /^COAI-JQ missing (?:apt-get|dnf|yum|apk|none)$/u, outcome.stdout);
});

// ───────────────── A2-T4: the PATH ─────────────────

shellTest('A2-T4: an agy only in $HOME/.local/bin is found, and no profile is read', async (shell) => {
  const t = tree();
  write(join(t.home, '.profile'), `: > "${t.fx}/profile-ran"\n`);
  write(join(t.home, '.bashrc'), `: > "${t.fx}/profile-ran"\n`);
  const { got } = await read(shell, t, 'no-claude-usage');
  assert.equal(got.agy.state, 'found', 'an agy in ~/.local/bin was read as absent');
  assert.equal(existsSync(join(t.fx, 'profile-ran')), false, 'a profile was read');
  renameSync(t.local, join(t.root, 'home', 'local-bin-moved'));
  const again = await read(shell, t, 'no-claude-usage');
  assert.equal(again.got.agy.state, 'absent');
  assert.equal(again.got.claude.state, 'absent');
});

jqTest('A2-T4: a claude only in $HOME/.local/bin is found', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const { got } = await read(shell, t, 'no-claude-usage');
  assert.equal(got.claude.state, 'found', 'a claude in ~/.local/bin was read as absent');
});

// ───────────────── A2-T5: each vendor state, each usage state, each mode ─────────────────

const ZERO_TURNS = USAGE_TEXT();

jqTest('A2-T5: Claude — found with the measured fixtures, signed out, absent, an account that is no JSON', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const found = (await readUsage(shell, t)).got.claude;
  assert.ok(found.state === 'found' && found.email === 'someone@example.com' && found.org === 'Example Org' && found.plan === 'team' && found.version === '2.1.326', JSON.stringify(found));
  assert.ok(found.state === 'found' && found.usage.state === 'read' && found.usage.windows.length === 3, JSON.stringify(found));

  const out = tree({ jq: true });
  claudeFixtures(out, { auth: '{"loggedIn":false}' });
  put(out, 'claude.auth.code', '1');
  assert.deepEqual((await readUsage(shell, out)).got.claude, { state: 'signed-out' });
  assert.equal(calls(out, 'claude').length, 1, 'a signed-out account started a second claude program');

  const none = tree({ jq: true });
  renameSync(join(none.local, 'claude'), join(none.root, 'claude-away'));
  assert.deepEqual((await readUsage(shell, none)).got.claude, { state: 'absent' });

  const bad = tree({ jq: true });
  claudeFixtures(bad, { auth: 'this is not json' });
  assert.deepEqual((await readUsage(shell, bad)).got.claude, { state: 'unreadable', why: 'auth' });
});

jqTest('A2-T5: Claude usage — a model call, no line of the forms, an error; and the mode no-claude-usage skips it', async (shell) => {
  const usageOf = async (usage: string, code?: string): Promise<unknown> => {
    const t = tree({ jq: true });
    claudeFixtures(t, { usage });
    if (code !== undefined) {
      put(t, 'claude.usage.code', code);
    }
    const { outcome, got } = await readUsage(shell, t);
    const claude = got.claude;

    return claude.state === 'found' ? { usage: claude.usage, text: outcome.stdout } : claude;
  };
  const turns = (await usageOf(usageJson(ZERO_TURNS, { num_turns: 3 }))) as { usage: unknown; text: string };
  assert.deepEqual(turns.usage, { state: 'failed', why: 'model-call' });
  assert.ok(turns.text.includes(':turns 3') && !turns.text.includes(':line '), 'the answer of a model call left the VM');
  assert.deepEqual(((await usageOf(usageJson('You are using your subscription\n\nNo lines here\n'))) as { usage: unknown }).usage, { state: 'failed', why: 'format' });
  assert.deepEqual(((await usageOf(usageJson(ZERO_TURNS), '1')) as { usage: unknown }).usage, { state: 'failed', why: 'error' }, 'code 1');
  assert.deepEqual(((await usageOf(usageJson(ZERO_TURNS, { is_error: true }))) as { usage: unknown }).usage, { state: 'failed', why: 'error' }, 'is_error true');
  assert.deepEqual(((await usageOf('not json at all')) as { usage: unknown }).usage, { state: 'failed', why: 'error' }, 'no JSON');

  const t = tree({ jq: true });
  claudeFixtures(t);
  const skipped = (await read(shell, t, 'no-claude-usage')).got.claude;
  assert.ok(skipped.state === 'found' && skipped.usage.state === 'skipped');
  assert.equal(calls(t, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0, 'the usage command ran for no-claude-usage');
});

jqTest('R3-T5: Codex — found with its account, its plan and its window from codex app-server; absent; no rate limits; no reply', async (shell) => {
  const t = tree({ jq: true });
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.deepEqual(got.codex, { state: 'found', signInFile: false, email: CODEX_MAIL, plan: 'prolite', usage: { state: 'read', windows: [{ minutes: 10080, usedPercent: 19, resetsAtMs: 1796123826000 }] } });
  assert.ok(!outcome.stdout.includes(':as-of'), 'a time of a session file is still printed');

  const absent = tree({ jq: true });
  renameSync(join(absent.local, 'codex'), join(absent.root, 'codex-away'));
  assert.deepEqual((await read(shell, absent, 'no-claude-usage')).got.codex, { state: 'absent' });

  const folderOnly = tree({ jq: true });
  renameSync(join(folderOnly.local, 'codex'), join(folderOnly.root, 'codex-away'));
  mkdirSync(join(folderOnly.home, '.codex', 'sessions', '2026', '11', '17'), { recursive: true });
  assert.deepEqual((await read(shell, folderOnly, 'no-claude-usage')).got.codex, { state: 'found', signInFile: false, email: '', plan: null, usage: { state: 'failed', why: 'not-started' } }, 'a ~/.codex with no program on the script\'s PATH is a Codex that was not started, not one that vanished');
  assert.equal(calls(folderOnly, 'codex').length, 0);
  const nowhere = tree({ jq: true });
  renameSync(join(nowhere.local, 'codex'), join(nowhere.root, 'codex-away'));
  assert.deepEqual((await read(shell, nowhere, 'no-claude-usage')).got.codex, { state: 'absent' }, 'no program and no ~/.codex is no Codex');
  const noJq = tree({ hide: ['jq'] });
  renameSync(join(noJq.local, 'codex'), join(noJq.root, 'codex-away'));
  mkdirSync(join(noJq.home, '.codex'), { recursive: true });
  assert.deepEqual((await read(shell, noJq, 'no-claude-usage')).got.codex, { state: 'unreadable', why: 'no-jq' }, 'the no-jq word keeps its place beside COAI-JQ missing');

  const none = tree({ jq: true });
  put(none, 'codex.out', codexOut({ rateLimits: { limitId: 'codex', primary: null, secondary: null, planType: 'prolite' } }));
  const noLimits = (await read(shell, none, 'no-claude-usage')).got.codex;
  assert.deepEqual(noLimits, { state: 'found', signInFile: false, email: CODEX_MAIL, plan: 'prolite', usage: { state: 'failed', why: 'none' } });

  const silent = tree({ jq: true });
  put(silent, 'codex.out', '');
  assert.deepEqual((await read(shell, silent, 'no-claude-usage')).got.codex, { state: 'found', signInFile: false, email: '', plan: null, usage: { state: 'failed', why: 'error' } }, 'no reply at all is no rate limits, with no account');
});

jqTest('R3-T5: agy — the quota of both groups, the reset epoch of a window that has started, a dash for one that has not, and the account from the log', async (shell) => {
  const t = tree({ jq: true });
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.deepEqual(lines(outcome).filter((line) => line.startsWith(':quota ') || line.startsWith('COAI-USAGE agy')), [
    'COAI-USAGE agy read', ':quota gemini weekly 60 1796283734', ':quota gemini 5h 92 1795813241', ':quota third-party weekly 100 -', ':quota third-party 5h 100 -',
  ]);
  assert.ok(got.agy.state === 'found' && got.agy.email === AGY_MAIL && got.agy.usage.state === 'read' && got.agy.usage.windows.length === 4, JSON.stringify(got.agy));
});

shellTest('A2-T5: agy — found, and absent', async (shell) => {
  const t = tree();
  assert.equal((await read(shell, t, 'no-claude-usage')).got.agy.state, 'found');
  const gone = tree();
  renameSync(join(gone.local, 'agy'), join(gone.root, 'agy-away'));
  assert.equal((await read(shell, gone, 'no-claude-usage')).got.agy.state, 'absent');
});

shellTest('A2-T5 companion: a missing tool is said as COAI-BAD tool, not read as an account fault', async (shell) => {
  const t = tree({ hide: ['tail'] });
  const outcome = await run(shell, t, ['no-claude-usage']);
  assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD tool', 'COAI-ACCOUNTS-END']);
  assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'tool' });
});

// ───────────────── A2-T6: every vendor command is the parent's, word for word ─────────────────

const AUTH_ARGS = '[auth][status][--json]';
const VERSION_ARGS = '[--version]';
const QUOTA_ARGS = '[-p][/quota][--output-format][json]';
const APP_SERVER_ARGS = '[app-server]';
const USAGE_ARGS = '[-p][/usage][--output-format][json][--no-session-persistence][--strict-mcp-config][--setting-sources][]';

jqTest('A2-T6: the commands, their order, their folder, their stdin and their TZ', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  await readUsage(shell, t);
  const claude = calls(t, 'claude');
  assert.deepEqual(claude.map(argsOf), [AUTH_ARGS, VERSION_ARGS, USAGE_ARGS], 'the claude commands, in order');
  assert.deepEqual(calls(t, 'agy').map(argsOf), [], 'agy was started in the usage mode');
  for (const line of claude) {
    assert.ok(line.includes(' cwd=/ stdin=/dev/null '), `a program was started from another folder or with another stdin: ${line}`);
    assert.equal(line.endsWith(' tz=UTC'), argsOf(line) === USAGE_ARGS, `TZ is UTC on the -p line and only there: ${line}`);
  }
  assert.ok(envFiles(t).every((env) => !env.includes('LC_ALL=')), 'a vendor program saw an LC_ALL');

  const plain = tree({ jq: true });
  claudeFixtures(plain);
  await read(shell, plain, 'no-claude-usage');
  assert.deepEqual(calls(plain, 'claude').map(argsOf), [AUTH_ARGS, VERSION_ARGS], 'the usage command ran for no-claude-usage');
  assert.deepEqual(calls(plain, 'agy').map(argsOf), [VERSION_ARGS, QUOTA_ARGS]);
  assert.deepEqual(calls(plain, 'codex').map(argsOf), [APP_SERVER_ARGS]);
  for (const line of calls(plain, 'codex')) {
    assert.ok(line.includes(' cwd=/ stdin=pipe:') && line.endsWith(' tz=unset'), `codex was started from another folder, with a stdin that is no pipe or with a TZ: ${line}`);
  }
  for (const line of calls(plain, 'agy')) {
    assert.ok(line.includes(' cwd=/ stdin=/dev/null ') && line.endsWith(' tz=unset'), `agy was started from another folder, with another stdin or with a TZ: ${line}`);
  }
});

// ───────────────── A2-T7: agy gets --version and /quota, codex gets app-server; no credential file is opened ─────────────────

const CREDENTIAL_FILES = ['.claude/.credentials.json', '.claude.json', '.codex/auth.json', '.gemini/antigravity-cli/antigravity-oauth-token'];
const SECRET_JSON = '{"accessToken":"sk-ant-oat01-SECRET-MARK-0123456789abcdef"}\n';
const YEAR_2001 = 978_307_200;

function plantCredentials(t: Tree): readonly string[] {
  const files = CREDENTIAL_FILES.map((name) => join(t.home, name));
  for (const file of files) {
    write(file, SECRET_JSON, 0o600);
    utimesSync(file, YEAR_2001, YEAR_2001);
  }
  // agy's own log is read, for one line: its other lines hold a secret that must not leave
  agyLog(t, AGY_MAIL, ['I1127 17:23:23.000000 1 auth.go:1] token=sk-ant-oat01-SECRET-MARK-0123456789abcdef']);

  return files;
}

shellTest('A2-T7: agy gets --version and /quota, codex gets app-server in the first mode only, and no credential file is opened', async (shell, context) => {
  const probe = join(ownedTemp('accounts-probe'), 'probe');
  write(probe, 'x');
  utimesSync(probe, YEAR_2001, YEAR_2001);
  readFileSync(probe);
  const shows = statSync(probe).atimeMs > YEAR_2001 * 1000 + 86_400_000;
  if (!shows && process.env['CI'] === 'true' && process.platform === 'linux') {
    assert.fail('the file system does not show a read, so the access-time half of A2-T7 proves nothing on CI');
  }
  for (const mode of MODES) {
    const t = tree({ jq: true });
    claudeFixtures(t);
    const files = plantCredentials(t);
    await run(shell, t, operandsOf(t, mode));
    // agy and Codex are read by the first mode only (E3.S2: the usage mode reads Claude and nothing else)
    assert.deepEqual(calls(t, 'agy').map(argsOf), mode === 'no-claude-usage' ? [VERSION_ARGS, QUOTA_ARGS] : [], `${mode}: agy got more than --version and /quota`);
    assert.ok(calls(t, 'agy').every((line) => line.includes(' cwd=/ stdin=/dev/null ')), `${mode}: agy was started from another folder or with another stdin: ${calls(t, 'agy')[0]}`);
    assert.deepEqual(calls(t, 'codex').map(argsOf), mode === 'no-claude-usage' ? [APP_SERVER_ARGS] : [], `${mode}: codex got more than app-server, or was started in the usage mode`);
    if (shows) {
      for (const file of files) {
        assert.equal(statSync(file).atimeMs, YEAR_2001 * 1000, `${mode}: ${file} was opened`);
      }
    }
  }
  if (!shows) {
    context.diagnostic('the file system does not show a read: the access-time half was skipped');
  }
});

// ───────────────── A2-T8: a command that hangs, or starts a child and hangs ─────────────────

/** The seconds a run took, and the outcome. */
async function timed(shell: Shell, t: Tree, mode: AccountsMode): Promise<{ readonly seconds: number; readonly outcome: Outcome }> {
  const began = Date.now();
  const outcome = await run(shell, t, operandsOf(t, mode), ACCOUNTS_SCRIPT, 30_000);

  return { seconds: (Date.now() - began) / 1000, outcome };
}

function pidsOf(t: Tree): readonly number[] {
  const logged2 = logLines(t).map((line) => Number(line.split(' ')[1]));
  const children = existsSync(join(t.fx, 'children')) ? readFileSync(join(t.fx, 'children'), 'utf8').split('\n').filter((line) => line !== '').map(Number) : [];

  return [...logged2, ...children].filter((pid) => Number.isInteger(pid) && pid > 1);
}

const alive = (pid: number): boolean => {
  try {
    process.kill(pid, 0);

    return true;
  } catch {
    return false;
  }
};

async function noneAlive(t: Tree): Promise<void> {
  for (let tries = 0; tries < 20 && pidsOf(t).some(alive); tries += 1) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  assert.deepEqual(pidsOf(t).filter(alive), [], 'a program of a hang case is still alive');
}

shellTest('A2-T8 (iv): agy that hangs by exec sleep is ended by its own limit; the reply still ends with its end line', async (shell) => {
  const t = tree();
  put(t, 'hang-agy', '');
  const { seconds, outcome } = await timed(shell, t, 'no-claude-usage');
  whole(outcome);
  assert.ok(seconds >= 1.8, `agy was ended after ${seconds} s, before its limit of 2 s`);
  const got = readAccounts(outcome, 'no-claude-usage', NOW);
  assert.ok(got.kind === 'read' && got.agy.state === 'found' && got.agy.version === null, JSON.stringify(got));
  assert.deepEqual(calls(t, 'agy').map(argsOf), [VERSION_ARGS], 'with no jq agy is not asked for its quota');
  assert.ok(lines(outcome).includes('COAI-USAGE agy failed no-jq'), outcome.stdout);
  await noneAlive(t);
});

jqRows += 1;
test('A2-T8: the six hang runs, started together in ONE test', { timeout: 40_000, ...(SHELLS.length === 0 ? { skip: NO_SH } : (JQ === undefined ? { skip: NO_JQ } : {})) }, async () => {
  const sh = SHELLS[0] as Shell;
  const bash = SHELLS.find((shell) => shell.label !== 'sh') ?? sh;
  const make = (setup: (t: Tree) => void): Tree => {
    const t = tree({ jq: true });
    claudeFixtures(t);
    setup(t);

    return t;
  };
  const version = make((t) => put(t, 'hang-version', ''));
  const account = make((t) => put(t, 'hang-auth', ''));
  const usageSh = make((t) => put(t, 'child-usage', ''));
  const usageBash = make((t) => put(t, 'child-usage', ''));
  const agy = make((t) => put(t, 'hang-quota', ''));
  const file = make((t) => put(t, 'hang-codex', ''));
  const [v, a, us, ub, g, f] = await Promise.all([
    timed(sh, version, 'claude-usage'), timed(sh, account, 'claude-usage'), timed(sh, usageSh, 'claude-usage'),
    timed(bash, usageBash, 'claude-usage'), timed(sh, agy, 'no-claude-usage'), timed(sh, file, 'no-claude-usage'),
  ]);
  for (const [name, done] of [['version', v], ['account', a], ['usage sh', us], ['usage bash', ub], ['agy quota', g], ['codex', f]] as const) {
    whole(done.outcome);
    const first = name === 'codex' || name === 'agy quota';
    assert.equal(readAccounts(done.outcome, first ? 'no-claude-usage' : 'claude-usage', NOW).kind, first ? 'read' : 'claude-only', `${name}: ${done.outcome.stdout.slice(0, 300)}`);
  }
  const claudeOf = (outcome: Outcome): string => outcome.stdout;
  assert.ok(v.seconds >= 1.8 && !claudeOf(v.outcome).includes(':version 2.1'), 'the version command: ended early, or its version was read');
  assert.ok(claudeOf(v.outcome).includes('COAI-USAGE claude read'), 'a version that hangs must not stop the usage read');
  assert.ok(a.seconds >= 2.8 && claudeOf(a.outcome).includes('COAI-VENDOR claude unreadable timeout'), 'the account command that hangs is unreadable timeout');
  assert.equal(calls(account, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0, 'a usage command started after the account command timed out');
  for (const done of [us, ub]) {
    assert.ok(done.seconds >= 7.8 && done.outcome.stdout.includes('COAI-USAGE claude failed timeout'), `the usage command that starts a child and hangs: ${done.outcome.stdout.slice(0, 300)}`);
  }
  assert.ok(g.seconds >= 3.8 && g.outcome.stdout.includes('COAI-USAGE agy failed timeout') && g.outcome.stdout.includes(':version 1.5.12'), `the quota command of agy that hangs: ${g.seconds} s, ${g.outcome.stdout.slice(0, 400)}`);
  assert.ok(f.seconds >= 3.8 && f.seconds < 6.5 && f.outcome.stdout.includes('COAI-USAGE codex failed timeout') && !f.outcome.stdout.includes(':window '), `codex app-server that hangs: ${f.seconds} s, ${f.outcome.stdout.slice(0, 400)}`);
  for (const t of [version, account, usageSh, usageBash, agy, file]) {
    await noneAlive(t);
  }
});

// ───────────────── A2-T9 and A2-T10: no secret, no activity leaves — SEEN ─────────────────

/** Everything one run left that could hold a leak: stdout, stderr, the arguments, every environment, the typed result. */
function everything(t: Tree, outcome: Outcome, mode: AccountsMode): string {
  // the value parsed with the mode that RAN, and a reply that is read: a value that is not would hold no marker whatever leaked
  const parsed = readAccounts(outcome, mode, NOW);
  assert.equal(parsed.kind, mode === 'claude-usage' ? 'claude-only' : 'read', `${mode}: the reply is not read, so the scan of its typed value sees nothing: ${JSON.stringify(parsed)}`);
  const typed = JSON.stringify(parsed);

  return [outcome.stdout, outcome.stderr, existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8') : '', ...envFiles(t), typed].join('\n');
}

jqTest('A2-T9: no secret leaves — the four false credential files and agy\'s log hold a marker token', async (shell) => {
  for (const mode of MODES) {
    const t = tree({ jq: true });
    claudeFixtures(t);
    plantCredentials(t);
    const calls0 = { n: 0 };
    const original = console.error;
    console.error = (): void => {
      calls0.n += 1;
    };
    let outcome: Outcome;
    try {
      outcome = await run(shell, t, operandsOf(t, mode));
      assert.equal(readAccounts(outcome, mode, NOW).kind, mode === 'claude-usage' ? 'claude-only' : 'read', `${mode}: the reply is not read`);
    } finally {
      console.error = original;
    }
    assert.equal(outcome.stderr, '', 'stderr is not empty');
    assert.equal(calls0.n, 0, 'the parser wrote to the console');
    const all = everything(t, outcome, mode);
    assert.ok(!all.includes('SECRET-MARK'), `${mode}: SECRET-MARK is in an output, an argument, an environment or the typed result`);
    for (const mark of ['LOG-SECRET-MARK', 'USER-ID-MARK', 'ACCOUNT-ID-MARK', 'RESET-CREDIT-MARK', 'INSTALLATION-MARK', 'RESPONSE-MARK', 'DESCRIPTION-MARK', 'GROUP-MARK']) {
      assert.ok(!all.includes(mark), `${mode}: ${mark} is in an output, an argument, an environment or the typed result`);
    }
    assert.ok(!outcome.stdout.includes('ORG-ID-MARK'), `${mode}: the organization id left the VM`);
  }
});

jqTest('A2-T10: the person\'s activity does not leave — Claude\'s usage text, and the fields of Codex and agy that no pattern names', async (shell) => {
  const t = tree({ jq: true });
  const activity = `${USAGE_TEXT('Requests in the last 24 hours: ACTIVITY-MARK\nCurrent sessions of ACTIVITY-MARK\n')}Current week (all models): 77% used \u00b7 resets Nov 22, 3am (UTC) ACTIVITY-MARK\n`;
  claudeFixtures(t, { usage: usageJson(activity) });
  const { outcome } = await readUsage(shell, t);
  const out = lines(outcome);
  assert.equal(out.filter((line) => line.startsWith(':line ')).length, 3);
  assert.ok(out.includes(':unmatched 2'), `unmatched: ${out.filter((line) => line.startsWith(':unmatched')).join()}`);
  assert.ok(!outcome.stdout.includes('ACTIVITY-MARK'), 'ACTIVITY-MARK is in a :line');
  // the Codex window and agy's quota are the first mode's; the fields beside them carry marks
  const first = await read(shell, t, 'no-claude-usage');
  assert.ok(lines(first.outcome).includes(':window 19 10080 1796123826'), 'the right window');
  for (const mark of ['USER-ID-MARK', 'ACCOUNT-ID-MARK', 'RESET-CREDIT-MARK', 'INSTALLATION-MARK', 'RESPONSE-MARK', 'DESCRIPTION-MARK', 'GROUP-MARK', 'LOG-SECRET-MARK']) {
    assert.ok(!first.outcome.stdout.includes(mark), `${mark} left the VM`);
  }
  assert.equal(first.got.codex.state, 'found');
});

// ───────────────── A2-T11: of a version command only the version leaves ─────────────────

shellTest('A2-T11: agy — the version is the digits and dots of its first line, and nothing else', async (shell) => {
  const cases: readonly (readonly [string, string | null])[] = [
    ['Antigravity CLI v1.5.12 (stable) VERSION-MARK\nUpdate available: 1.6.0 VERSION-MARK\n', '1.5.12'],
    ['Welcome VERSION-MARK\n1.5.12\n', null],
    [`${'1'.repeat(40)}\n`, '1'.repeat(32)],
    ['unknown\n', null],
    ['7\n', '7'],
  ];
  for (const [text, expected] of cases) {
    const t = tree();
    put(t, 'agy.version', text);
    const { outcome, got } = await read(shell, t, 'no-claude-usage');
    assert.ok(!outcome.stdout.includes('VERSION-MARK') && !outcome.stdout.includes('1.6.0'), `${JSON.stringify(text)}: a banner left the VM`);
    assert.ok(got.agy.state === 'found', JSON.stringify(got.agy));
    const lineOf = lines(outcome).find((line) => line.startsWith(':version '));
    assert.equal(lineOf === undefined ? undefined : lineOf.slice(9), expected === null ? undefined : expected, JSON.stringify(text));
  }
});

jqTest('A2-T11: claude --version — the same rule', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  const { outcome } = await read(shell, t, 'no-claude-usage');
  assert.ok(lines(outcome).includes(':version 2.1.326'));
  const banner = tree({ jq: true });
  claudeFixtures(banner);
  put(banner, 'claude.version', 'Welcome VERSION-MARK\n2.1.326\n');
  // The tree's agy prints a version of its own; with that line gone, any `:version` line left is the claude one.
  put(banner, 'agy.version', 'unknown\n');
  const second = await read(shell, banner, 'no-claude-usage');
  assert.ok(!second.outcome.stdout.includes('VERSION-MARK') && !second.outcome.stdout.includes(':version '), 'a banner line first leaves no version');
});

// ───────────────── A2-T12: a host without jq says so, with its manager word; agy is still read ─────────────────

const MANAGERS = [['/usr/bin/apt-get', 'apt-get'], ['/usr/bin/dnf', 'dnf'], ['/usr/bin/yum', 'yum'], ['/sbin/apk', 'apk']] as const;
const executable = (path: string): boolean => {
  try {
    accessSync(path, constants.X_OK);

    return true;
  } catch {
    return false;
  }
};

shellTest('A2-T12 (a)(c): without jq the reply says so with the manager word, starts no claude, and runs no package manager', async (shell) => {
  const t = tree();
  claudeFixtures(t);
  for (const name of ['apt-get', 'dnf', 'yum', 'apk', 'sudo']) {
    write(join(t.local, name), logged(name, t.fx), 0o755);
  }
  const word = MANAGERS.find(([path]) => executable(path))?.[1] ?? 'none';
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(lines(outcome).includes(`COAI-JQ missing ${word}`), outcome.stdout);
  assert.deepEqual(got.claude, { state: 'unreadable', why: 'no-jq' });
  // the usage mode on such a place: the Claude part, no Codex, no agy
  const usage = await readUsage(shell, t);
  assert.deepEqual(usage.got.claude, { state: 'unreadable', why: 'no-jq' });
  assert.equal(usage.got.jqMissing, word);
  assert.equal(calls(t, 'claude').length, 0, 'a claude was started on a host without jq');
  assert.deepEqual(got.codex, { state: 'unreadable', why: 'no-jq' });
  assert.ok(got.agy.state === 'found' && got.agy.version === '1.5.12' && got.agy.email === '' && got.agy.usage.state === 'failed' && got.agy.usage.why === 'no-jq', JSON.stringify(got.agy));
  assert.deepEqual(calls(t, 'agy').map(argsOf), [VERSION_ARGS], 'agy was asked for its quota on a host without jq');
  assert.deepEqual(calls(t, 'codex'), [], 'codex was started on a host without jq');
  assert.equal(got.jqMissing, word);
  for (const name of ['apt-get', 'dnf', 'yum', 'apk', 'sudo']) {
    assert.deepEqual(calls(t, name), [], `a package manager was started on a host without jq: ${name}`);
  }
});

shellTest('A2-T12 (b): the five branches of coai_manager, from a temporary folder, with each file there and with none', async (shell) => {
  const body = ACCOUNTS_SCRIPT.split('\n');
  const from = body.indexOf('  coai_manager() {');
  const to = body.indexOf('  }', from);
  assert.ok(from > 0 && to > from, 'coai_manager is not in the text');
  const cases: readonly (readonly [readonly string[], string])[] = [
    [['usr/bin/apt-get'], 'apt-get'], [['usr/bin/dnf'], 'dnf'], [['usr/bin/yum'], 'yum'], [['sbin/apk'], 'apk'], [[], 'none'],
    [['usr/bin/apt-get', 'usr/bin/dnf'], 'apt-get'], [['usr/bin/dnf', 'usr/bin/yum'], 'dnf'],
  ];
  for (const [files, word] of cases) {
    const t = tree();
    const folder = scratch();
    for (const file of files) {
      write(join(folder, file), logged(file, t.fx), 0o755);
    }
    const text = `${body.slice(from, to + 1).join('\n').replaceAll('/usr/bin/', `${folder}/usr/bin/`).replaceAll('/sbin/', `${folder}/sbin/`)}\ncoai_manager\nprintf '\\n'\n`;
    const outcome = await run(shell, t, [], text);
    assert.equal(outcome.stdout.trim(), word, JSON.stringify(files));
    assert.deepEqual(logLines(t), [], 'a package manager was started: it is tested for with -x and never run');
  }
});

// ───────────────── A2-T13: nothing is written ─────────────────

/** The whole tree under a folder: path, size, time, mode. */
function listing(folder: string): readonly string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);
    const stat = lstatSync(path);
    const here = `${path} ${stat.size} ${stat.mtimeMs} ${stat.mode}`;

    return entry.isDirectory() ? [here, ...listing(path)] : [here];
  });
}

shellTest('A2-T13: nothing is written — the home tree is identical after a run of each mode, and every program saw /', async (shell) => {
  for (const jq of [false, true]) {
    if (jq && JQ === undefined) {
      continue;
    }
    for (const mode of MODES) {
      const t = tree({ jq });
      claudeFixtures(t);
      plantCredentials(t);
      const before = listing(t.home);
      const rootBefore = readdirSync(t.root).sort();
      whole(await run(shell, t, operandsOf(t, mode)));
      assert.deepEqual(listing(t.home), before, `${mode} (jq ${jq}): the home tree changed`);
      assert.deepEqual(readdirSync(t.root).sort(), rootBefore, `${mode} (jq ${jq}): the tree's root has a new entry`);
      assert.ok(logLines(t).every((line) => line.includes(' cwd=/ ')), 'a program saw another working folder than /');
    }
  }
});

// ───────────────── R3-T6 (the script's half): what codex app-server said, and what is sent of it ─────────────────

/** The lines of Codex's part: those before agy's. */
const codexLinesOf = (outcome: Outcome): readonly string[] => {
  const all = lines(outcome);
  const end = all.findIndex((line) => line.startsWith('COAI-VENDOR agy '));

  return (end < 0 ? all : all.slice(0, end)).filter((line) => line.startsWith(':window ') || line.startsWith(':plan ') || line.startsWith(':account ') || line.startsWith('COAI-USAGE codex'));
};

async function codexWith(shell: Shell, out: string): Promise<readonly string[]> {
  const t = tree({ jq: true });
  put(t, 'codex.out', out);

  return codexLinesOf((await read(shell, t, 'no-claude-usage')).outcome);
}

jqTest('R3-T6: the measured replies — the account, the plan, the weekly window; and both windows when there are two', async (shell) => {
  assert.deepEqual(await codexWith(shell, codexOut()), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex read', ':window 19 10080 1796123826']);
  const two = codexOut({ secondary: { usedPercent: 3.456, windowDurationMins: 300, resetsAt: 1795500000 } });
  assert.deepEqual(await codexWith(shell, two), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex read', ':window 19 10080 1796123826', ':window 3.46 300 1795500000']);
});

jqTest('R3-T6: no rate limits — failed none, with the account and plan that were said; no reply, no JSON and an error reply are failed error', async (shell) => {
  const empty = codexOut({ rateLimits: { primary: null, secondary: null, planType: 'prolite' } });
  assert.deepEqual(await codexWith(shell, empty), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex failed none']);
  // only a real reply without rate limits is none: no reply at all, a reply that is no JSON and a reply that is an error are errors
  assert.deepEqual(await codexWith(shell, 'not json at all\n{"id":3\n'), ['COAI-USAGE codex failed error']);
  assert.deepEqual(await codexWith(shell, ''), ['COAI-USAGE codex failed error']);
  assert.deepEqual(await codexWith(shell, '{"id":1,"result":{}}\n{"id":3,"error":{"code":-32600,"message":"not signed in"}}\n'), ['COAI-USAGE codex failed error'], 'an error reply is no rate limits that were stated');
  const onlyRates = codexOut().split('\n').filter((line) => !line.includes('"id":2')).join('\n');
  assert.deepEqual(await codexWith(shell, onlyRates), [':plan prolite', 'COAI-USAGE codex read', ':window 19 10080 1796123826'], 'no account reply: the rate limits are still shown');
});

jqTest('R3-S4: the exit code of codex app-server — the figures are accepted when the rate limits came (even on 124 or 3), no rate limits reply with a bad exit is an error, 124 is a timeout, and a reply without id 1 is no reply', async (shell) => {
  const withCode = async (out: string, code: string): Promise<readonly string[]> => {
    const t = tree({ jq: true });
    put(t, 'codex.out', out);
    put(t, 'codex.code', code);

    return codexLinesOf((await read(shell, t, 'no-claude-usage')).outcome);
  };
  const figures = [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex read', ':window 19 10080 1796123826'];
  assert.deepEqual(await withCode(codexOut(), '0'), figures);
  assert.deepEqual(await withCode(codexOut(), '124'), figures, 'the reply came and the program was ended after it');
  assert.deepEqual(await withCode(codexOut(), '3'), figures, 'the reply came');
  const noRates = codexOut().split('\n').filter((line) => !line.includes('"id":3')).join('\n');
  assert.deepEqual(await withCode(noRates, '124'), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex failed timeout']);
  assert.deepEqual(await withCode(noRates, '137'), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex failed timeout']);
  assert.deepEqual(await withCode(noRates, '3'), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex failed error']);
  assert.deepEqual(await withCode(noRates, '0'), [':account cox@example.test', ':plan prolite', 'COAI-USAGE codex failed error'], 'a program that exits at once with no rate-limit reply');
  assert.deepEqual(await withCode('', '2'), ['COAI-USAGE codex failed error'], 'no app-server in this version: it printed nothing and exited');
});

jqTest('R3-T6: an email that is not in the safe shape is not sent, and the plan and the windows still are', async (shell) => {
  const bad = ['a b@example.test', 'no-at-sign', '@example.test', 'x@y', 'two@@example.test', `${'a'.repeat(65)}@example.test`, 'a@b.cé', 'a;id@example.test', 'a"b@example.test', 'a<b>@example.test', 42, null, { x: 1 }];
  for (const email of bad) {
    const out = await codexWith(shell, codexOut({ email }));
    assert.ok(!out.some((line) => line.startsWith(':account ')), `${JSON.stringify(email)} was sent: ${out.join('|')}`);
    assert.ok(out.includes('COAI-USAGE codex read'), JSON.stringify(email));
  }
  const multi = await codexWith(shell, codexOut({ email: 'line\nCOAI-USAGE codex failed none' }));
  assert.ok(!multi.some((line) => line.startsWith(':account ')) && multi.filter((line) => line.startsWith('COAI-USAGE')).length === 1, multi.join('|'));
  assert.deepEqual(await codexWith(shell, codexOut({ email: 'first.last+tag@sub.example.test' })), [':account first.last+tag@sub.example.test', ':plan prolite', 'COAI-USAGE codex read', ':window 19 10080 1796123826']);
});

jqTest('R3-T6: a window with no number is a dash, the plan is cut to one line, and a 2 MB notice is bounded', async (shell) => {
  const odd = codexOut({ primary: { usedPercent: 'x', windowDurationMins: 10080, resetsAt: null }, plan: `pro\nCOAI-USAGE codex read${'x'.repeat(300)}` });
  const out = await codexWith(shell, odd);
  assert.ok(out.includes(':window - 10080 -'), out.join('|'));
  assert.equal(out.filter((line) => line.startsWith('COAI-USAGE')).length, 1);
  assert.ok(out.every((line) => Buffer.byteLength(line) <= 512), 'a line over 512 bytes');
  const big = `${codexOut()}{"method":"noise","params":{"x":"${'y'.repeat(2_000_000)}"}}\n`;
  const [first] = await codexWith(shell, big);
  assert.ok(first === undefined || first.startsWith(':') || first.startsWith('COAI-USAGE'), 'a 2 MB notice made the script fail loudly');
});

// ───────────────── R3-T7 (the script's half): what agy -p /quota printed, and what is sent of it ─────────────────

/** The lines of agy's part: from its marker to the end line. */
const agyLinesOf = (outcome: Outcome): readonly string[] => {
  const all = lines(outcome);
  const at = all.findIndex((line) => line.startsWith('COAI-VENDOR agy '));

  return at < 0 ? [] : all.slice(at).filter((line) => line.startsWith(':quota ') || line.startsWith(':account ') || line.startsWith('COAI-USAGE agy') || line.startsWith('COAI-VENDOR agy'));
};

async function agyWith(shell: Shell, text: string, code?: string): Promise<readonly string[]> {
  const t = tree({ jq: true });
  put(t, 'agy.quota', text);
  if (code !== undefined) {
    put(t, 'agy.quota.code', code);
  }

  return agyLinesOf((await read(shell, t, 'no-claude-usage')).outcome);
}

const MODEL_CALL_SAID = ['COAI-VENDOR agy found', ':account agy@example.test', 'COAI-USAGE agy failed model-call'];

jqTest('R3-T7: an unknown slash command is sent to the model — every reply that is not a usage reply with no turn, no tokens and no conversation is failed model-call, and no figure leaves', async (shell) => {
  const replies: readonly (readonly [string, string])[] = [
    ['a turn was made', quotaJson({ num_turns: 1 })],
    ['tokens were spent', quotaJson({ usage: { input_tokens: 5, output_tokens: 0, total_tokens: 5 } })],
    ['tokens that are no number', quotaJson({ usage: { total_tokens: 'many' } })],
    ['a usage that is no object', quotaJson({ usage: 'x' })],
    ['a conversation was created', quotaJson({ conversation_id: 'CONVERSATION-MARK' })],
    ['another command', quotaJson({ command: { name: 'help', data: { groups: [] } } })],
    ['no command at all', quotaJson({ command: undefined })],
    ['a command that is not an object', quotaJson({ command: 'usage' })],
    ['a model answer', JSON.stringify({ conversation_id: 'C1', status: 'SUCCESS', response: 'MODEL-ANSWER-MARK', num_turns: 2, usage: { total_tokens: 99 } })],
    ['no conversation_id', JSON.stringify({ status: 'SUCCESS', num_turns: 0, command: { name: 'usage', data: { groups: [] } } })],
  ];
  for (const [what, reply] of replies) {
    const out = await agyWith(shell, reply);
    assert.deepEqual(out.filter((line) => line.startsWith('COAI-USAGE')), ['COAI-USAGE agy failed model-call'], what);
    assert.ok(!out.some((line) => line.startsWith(':quota ')) && !out.join('').includes('MARK'), `${what}: ${out.join('|')}`);
  }
  assert.deepEqual(await agyWith(shell, quotaJson({ num_turns: 1 })), MODEL_CALL_SAID);
});

jqTest('R3-T7: no reply, a reply that is no JSON, an exit code, a hang — failed error, failed timeout; a reply of no known bucket is read with no :quota line', async (shell) => {
  const usageOf = (out: readonly string[]): readonly string[] => out.filter((line) => line.startsWith('COAI-USAGE') || line.startsWith(':quota '));
  assert.deepEqual(usageOf(await agyWith(shell, '')), ['COAI-USAGE agy failed error']);
  assert.deepEqual(usageOf(await agyWith(shell, 'this is not json\n')), ['COAI-USAGE agy failed error']);
  assert.deepEqual(usageOf(await agyWith(shell, quotaJson(), '1')), ['COAI-USAGE agy failed error'], 'a non-zero exit code is an error, whatever was printed');
  assert.deepEqual(usageOf(await agyWith(shell, quotaJson().slice(0, 300))), ['COAI-USAGE agy failed error'], 'a reply cut in the middle');
  const none = quotaJson({}, [{ id: 'other-weekly', window: 'weekly', remaining_fraction: 0.5, reset_time: '2026-12-03T07:42:14Z' }]);
  const out = usageOf(await agyWith(shell, none));
  assert.deepEqual(out.filter((line) => line.startsWith(':quota ')), [':quota third-party weekly 100 -', ':quota third-party 5h 100 -'], 'a bucket whose id has an unknown prefix is left out; the other group is kept');
  const empty = quotaJson({ command: { name: 'usage', data: { groups: [] } } });
  assert.deepEqual(usageOf(await agyWith(shell, empty)), ['COAI-USAGE agy read'], 'a usage reply with no bucket is read, and the parser says format');
});

jqTest('R3-T7: the buckets — the group by the id prefix, the window by its closed word, the percent left rounded to an integer in 0..100, the reset as epoch seconds or a dash', async (shell) => {
  const bucket = (id: unknown, window: unknown, fraction: unknown, reset: unknown): Record<string, unknown> => ({ id, window, remaining_fraction: fraction, reset_time: reset });
  const quota = async (...buckets: readonly Record<string, unknown>[]): Promise<readonly string[]> => (await agyWith(shell, quotaJson({}, buckets))).filter((line) => line.startsWith(':quota ')).filter((line) => !line.startsWith(':quota third-party'));
  assert.deepEqual(await quota(bucket('gemini-5h', '5h', 0.004, '2026-11-27T21:00:41Z')), [':quota gemini 5h 0 1795813241']);
  assert.deepEqual(await quota(bucket('gemini-5h', '5h', 0.005, '2026-11-27T21:00:41Z')), [':quota gemini 5h 1 1795813241']);
  assert.deepEqual(await quota(bucket('gemini-weekly', 'weekly', 1, '2026-11-27T21:00:41.123456Z')), [':quota gemini weekly 100 1795813241'], 'a fraction of a second in the reset');
  assert.deepEqual(await quota(bucket('gemini-weekly', 'weekly', 0.5, '1970-01-01T00:00:00Z')), [':quota gemini weekly 50 -'], 'the epoch is a window that has not started');
  assert.deepEqual(await quota(bucket('gemini-weekly', 'weekly', 0.5, null)), [':quota gemini weekly 50 -']);
  assert.deepEqual(await quota(bucket('gemini-weekly', 'weekly', 0.5, 'tomorrow')), [':quota gemini weekly 50 -']);
  assert.deepEqual(await quota(bucket('gemini-weekly', 'weekly', 1.5, '2026-11-27T21:00:41Z'), bucket('gemini-5h', '5h', -0.1, '2026-11-27T21:00:41Z'), bucket('gemini-5h', '5h', 'x', '2026-11-27T21:00:41Z'), bucket('gemini-5h', '5h', null, '2026-11-27T21:00:41Z')), [], 'a fraction outside 0..1, or no number, is no bucket');
  assert.deepEqual(await quota(bucket('gemini-5h', 'monthly', 0.5, '2026-11-27T21:00:41Z'), bucket('gemini-5h', 5, 0.5, '2026-11-27T21:00:41Z'), bucket(7, '5h', 0.5, '2026-11-27T21:00:41Z'), bucket('gemini', '5h', 0.5, '2026-11-27T21:00:41Z'), bucket('3p', '5h', 0.5, '2026-11-27T21:00:41Z')), [], 'an unknown window word, an id with no prefix word');
  assert.deepEqual(await quota(bucket('gemini-5h', '5h', 0.5, '2026-11-27T21:00:41Z')), [':quota gemini 5h 50 1795813241']);
  // the id is judged by its prefix and never printed: a hostile rest of it cannot make a line
  const hostile = await agyWith(shell, quotaJson({}, [bucket('gemini-5h\nCOAI-USAGE agy failed error', '5h', 0.5, '2026-11-27T21:00:41Z')]));
  assert.deepEqual(hostile.filter((line) => line.startsWith('COAI-USAGE') || line.startsWith(':quota gemini')), ['COAI-USAGE agy read', ':quota gemini 5h 50 1795813241'], hostile.join('|'));
});

jqTest('R3-T7: a quota with more buckets than a reply may hold is cut at eight lines, and the reply stays whole', async (shell) => {
  const many = Array.from({ length: 200 }, (_, n) => ({ id: n % 2 === 0 ? 'gemini-5h' : '3p-weekly', window: n % 2 === 0 ? '5h' : 'weekly', remaining_fraction: 0.5, reset_time: '2026-11-27T21:00:41Z' }));
  const t = tree({ jq: true });
  put(t, 'agy.quota', quotaJson({}, many));
  const { outcome } = await read(shell, t, 'no-claude-usage');
  whole(outcome);
  assert.ok(lines(outcome).filter((line) => line.startsWith(':quota ')).length <= 8);
});

jqTest('R3-T8: the account of agy — the LAST applyAuthResult line of cli.log, validated; a bad shape, no line and no log are no :account line, and the log is read after the quota call', async (shell) => {
  const accountOf = async (prepare: (t: Tree) => void): Promise<readonly string[]> => {
    const t = tree({ jq: true });
    prepare(t);

    return agyLinesOf((await read(shell, t, 'no-claude-usage')).outcome).filter((line) => line.startsWith(':account '));
  };
  const old = 'I1127 17:00:00.000000 1 server_oauth.go:221] applyAuthResult: email=old@example.test, authMethod=consumer, quotaProject=';
  assert.deepEqual(await accountOf((t) => agyLog(t, 'new@example.test', [])), [':account new@example.test']);
  assert.deepEqual(await accountOf((t) => { agyLog(t, 'new@example.test'); write(join(t.home, '.gemini', 'antigravity-cli', 'log', 'cli-1.log'), `${old}\nI1127 17:23:22.016270 1 server_oauth.go:221] applyAuthResult: email=new@example.test, authMethod=consumer\nI1127 17:24:00.000000 1 other.go:1] fine\n`); }), [':account new@example.test'], 'the last of two lines, with a line after it');
  // the last line is the one that counts: a last line with a bad shape is no account, not the older good one
  assert.deepEqual(await accountOf((t) => { agyLog(t, 'x'); write(join(t.home, '.gemini', 'antigravity-cli', 'log', 'cli-1.log'), `${old}\nI1127 17:23:22.016270 1 server_oauth.go:221] applyAuthResult: email=not an email, authMethod=consumer\n`); }), []);
  for (const bad of ['a;id@example.test', 'x@y', '@example.test', 'a@@example.test', `${'a'.repeat(65)}@example.test`, 'a"b@example.test', 'a b@example.test', 'a@b.cé']) {
    assert.deepEqual(await accountOf((t) => agyLog(t, bad)), [], `${JSON.stringify(bad)} was sent`);
  }
  assert.deepEqual(await accountOf((t) => rmSync(join(t.home, '.gemini'), { recursive: true, force: true })), [], 'no log, no line');
  assert.deepEqual(await accountOf((t) => write(join(t.home, '.gemini', 'antigravity-cli', 'log', 'cli-1.log'), 'nothing of the kind\n')), []);
  assert.deepEqual(await accountOf((t) => write(join(t.home, '.gemini', 'antigravity-cli', 'log', 'cli-1.log'), `${'x'.repeat(2_000_000)}\n${old}\n`)), [':account old@example.test'], 'a line in the last megabyte of a big log');
  assert.deepEqual(await accountOf((t) => write(join(t.home, '.gemini', 'antigravity-cli', 'log', 'cli-1.log'), `${old}\n${'x'.repeat(2_000_000)}\n`)), [], 'a line further from the end than the last megabyte is not found');
  // the log is read after the quota call: the false agy writes the log line as its quota call runs, and the account is the one it wrote
  const t = tree({ jq: true });
  rmSync(join(t.home, '.gemini'), { recursive: true, force: true });
  const log = join(t.home, '.gemini', 'antigravity-cli', 'cli.log');
  write(join(t.local, 'agy'), `${AGY(t.fx).replace('exit $code', `case "$1" in -p) mkdir -p "${join(t.home, '.gemini', 'antigravity-cli')}" && echo 'I1127 17:23:22.016270 1 server_oauth.go:221] applyAuthResult: email=fresh@example.test, authMethod=consumer' > "${log}" ;; esac\nexit $code`)}`, 0o755);
  assert.deepEqual(agyLinesOf((await read(shell, t, 'no-claude-usage')).outcome).filter((line) => line.startsWith(':account ')), [':account fresh@example.test']);
});

// ───────────────── A2-T22: the script bounds its own reply ─────────────────

jqTest('A2-T22: a 10 MB result, 16 000 good lines, and an email and an organization of hostile size', async (shell) => {
  const big = tree({ jq: true });
  claudeFixtures(big, { usage: usageJson('x'.repeat(10_000_000)) });
  const huge = await readUsage(shell, big);
  assert.ok(lines(huge.outcome).includes('COAI-USAGE claude failed error'), 'a 10 MB result is not failed error');

  const many = tree({ jq: true });
  const goodLine = `Current session: 1% used \u00b7 resets Nov 19, 3pm (UTC)`;
  const count = 16_000;
  claudeFixtures(many, { usage: usageJson(`${Array.from({ length: count }, () => goodLine).join('\n')}\n`) });
  const wide = await readUsage(shell, many);
  assert.equal(lines(wide.outcome).filter((line) => line.startsWith(':line ')).length, 8);
  assert.ok(lines(wide.outcome).includes(`:unmatched ${count - 8}`), lines(wide.outcome).filter((line) => line.startsWith(':unmatched')).join());

  const hostile = tree({ jq: true });
  claudeFixtures(hostile, { auth: JSON.stringify({ ...ACCOUNT, email: 'a b '.repeat(2500), orgName: 'Org\nCOAI-USAGE claude read' }) });
  const small = await read(shell, hostile, 'no-claude-usage');
  assert.equal(lines(small.outcome).filter((line) => line.startsWith(':email ')).length, 1);
  assert.equal(lines(small.outcome).filter((line) => line.startsWith(':org ')).length, 1);
  for (const done of [huge, wide, small]) {
    const all = done.outcome.stdout.split('\n');
    assert.ok(all.length - 1 <= 66 && all.every((line) => Buffer.byteLength(line) <= 512), 'more than 64 lines in the frame, or a line over 512 bytes');
    assert.ok(Buffer.byteLength(done.outcome.stdout) <= ACCOUNTS_REPLY_BYTES_MAX);
  }
});

// ───────────────── A2-T30: no command waits for the script's stdin, and a reader that is gone stops the script ─────────────────

shellTest('A2-T30: the end line comes with stdin still open; then closing it ends the shell with code 0', async (shell) => {
  const t = tree();
  const child = started(shell, t, ['no-claude-usage']);
  let stdout = '';
  child.stdout?.setEncoding('utf8');
  child.stdout?.on('data', (text: string) => {
    stdout += text;
  });
  const ended = new Promise<number>((resolve) => child.on('close', (code) => resolve(code ?? -1)));
  // a bounded cleanup: a shell that is still there after the test failed is killed, whatever the failure was
  const killer = setTimeout(() => { child.kill('SIGKILL'); }, 15_000);
  void ended.then(() => { clearTimeout(killer); });
  try {
    child.stdin?.write(ACCOUNTS_SCRIPT);
    const began = Date.now();
    while (!stdout.endsWith('COAI-ACCOUNTS-END\n') && Date.now() - began < 5000) {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    // before EOF, so a script that waits for it is still seen
    assert.ok(stdout.endsWith('COAI-ACCOUNTS-END\n'), `no end line within 5 s with stdin open: ${stdout.slice(-200)}`);
  } finally {
    child.stdin?.end();
  }
  assert.equal(await ended, 0);
});

shellTest('A2-T30: a reader that goes away stops the script — also with SIGPIPE ignored', async (shell) => {
  for (const ignore of [false, true]) {
    const t = tree({ jq: true });
    claudeFixtures(t);
    put(t, 'slow-auth', '');
    // The shell path and its arguments are positional parameters of the wrapper, so a path with a space is not split.
    const child = ignore
      ? spawn('/bin/sh', ['-c', 'trap "" PIPE; exec "$@"', 'sh', shell.path, ...shell.args, ...SHELL_ARGS, ...operandsOf(t, 'claude-usage')], { cwd: t.root, env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'ignore'] })
      : started(shell, t, operandsOf(t, 'claude-usage'));
    const ended = new Promise<void>((resolve) => child.on('close', () => resolve()));
    let heard = false;
    child.stdout?.once('data', () => { heard = true; child.stdout?.destroy(); });
    child.stdin?.on('error', () => undefined);
    child.stdin?.end(ACCOUNTS_SCRIPT);
    const began = Date.now();
    await Promise.race([ended, new Promise((resolve) => setTimeout(resolve, 5000))]);
    assert.ok(Date.now() - began < 5000, `the shell did not end within 5 s after its reader was gone (SIGPIPE ignored: ${ignore})`);
    assert.equal(heard, true, `the shell ended without ever writing: no reader was disconnected, so nothing was tested (SIGPIPE ignored: ${ignore})`);
    if (JQ !== undefined) {
      // With jq the account command (made slow on purpose) is running when the reader leaves, so the next step must not start.
      assert.equal(calls(t, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0, `the usage command started after the reader was gone (SIGPIPE ignored: ${ignore})`);
      assert.equal(calls(t, 'agy').length, 0, `agy started after the reader was gone (SIGPIPE ignored: ${ignore})`);
    }
  }
});

// ───────────────── E3S2-T13: T46, the run — the worst path of each mode ends inside its sum plus one second ─────────────────

jqRows += 1;
test('E3S2-T13: T46, the run — a false claude --version, codex and agy that ignore TERM, the usage step reached; each run ends inside its sum plus 1 s with its end line', { timeout: 60_000, ...(SHELLS.length === 0 ? { skip: NO_SH } : (JQ === undefined ? { skip: NO_JQ } : {})) }, async () => {
  const sh = SHELLS[0] as Shell;
  const realSum = onPath('sha256sum');
  assert.ok(realSum !== undefined, 'no sha256sum on this machine');
  const make = (usage: boolean): Tree => {
    const t = tree({ jq: true });
    claudeFixtures(t);
    // a run that measures time has the real sleep: the two seconds of codex app-server's input are part of the sum
    t.prelude = '';
    put(t, 'slow-auth', '2.5');
    put(t, 'deaf-version', '');
    put(t, 'deaf-agy', '');
    put(t, 'deaf-codex', '');
    if (usage) {
      put(t, 'slow-usage', '7');
      // the digest is made just under its limit of 1 s, and is the TRUE one: a digest killed at its limit would skip the usage step and this run would measure 8 s, not 17
      write(join(t.local, 'sha256sum'), `#!/bin/sh\nsleep 0.9\nexec ${realSum} "$@"\n`, 0o755);
    }

    return t;
  };
  const first = make(false);
  const usage = make(true);
  const [f, u] = await Promise.all([timed(sh, first, 'no-claude-usage'), timed(sh, usage, 'claude-usage')]);
  whole(f.outcome);
  whole(u.outcome);
  // inside the sum of the steps (the ssh connect is not part of a local run) plus one second
  assert.ok(f.seconds <= worstSeconds('no-claude-usage') - ACCOUNTS_CONNECT_SECONDS + 1, `the first mode took ${f.seconds} s`);
  assert.ok(u.seconds <= worstSeconds('claude-usage') - ACCOUNTS_CONNECT_SECONDS + 1, `the usage mode took ${u.seconds} s`);
  assert.ok(f.seconds >= 2.5 + 1.8 + 3.8 + 1.8 + 3.8, `the first mode ended after ${f.seconds} s, before its deaf programs were killed`);
  // the usage step must be REACHED (gate E3-G1, F2): the false sha256sum's true digest, then the usage command ended by its own limit
  assert.ok(lines(u.outcome).includes('COAI-USAGE claude failed timeout') || lines(u.outcome).includes('COAI-USAGE claude read'), `the usage step was not reached: ${u.outcome.stdout}`);
  assert.ok(lines(u.outcome).includes('COAI-USAGE claude read'), `the slow usage step did not end with its figures: ${u.outcome.stdout}`);
  assert.equal(calls(usage, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 1, 'the usage command was not started');
  assert.ok(u.seconds >= 2.5 + 2.8 + 0.8 + 6.8, `the usage mode ended after ${u.seconds} s, before the usage step`);
  // a quota command that ignores TERM is killed one second later, at the end of its readers' limit: they may have gone by then, and the reply is a timeout or an error, never figures
  assert.ok((lines(f.outcome).includes('COAI-USAGE agy failed timeout') || lines(f.outcome).includes('COAI-USAGE agy failed error')) && !f.outcome.stdout.includes(':version 1.5') && !f.outcome.stdout.includes(':quota '), `the deaf agy: ${f.outcome.stdout}`);
  // a codex that ignores TERM is killed one second later, at the very end of its readers' limit: they have gone by then, and Codex is said not to have answered
  assert.ok(lines(f.outcome).includes('COAI-USAGE codex failed error') || lines(f.outcome).includes('COAI-USAGE codex failed timeout'), `the deaf codex: ${f.outcome.stdout}`);
  for (const t of [first, usage]) {
    await noneAlive(t);
  }
});

// ───────────────── E3S2-T22, T23: leaks in the usage mode, and in the path ─────────────────

jqTest('E3S2-T22: leak, the usage mode — a marker in a Codex session file, in the false agy\'s output and in a field of the usage JSON that no pattern names is in no byte of stdout; the digest is in no line', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t, { usage: usageJson(USAGE_TEXT('Requests in the last 24 hours: 3\n'), { stray_field: 'STRAY-FIELD-MARK', session_id: 'SESSION-MARK', permission_denials: ['DENIAL-MARK'] }) });
  put(t, 'codex.out', `CODEX-FILE-MARK\n${codexOut()}`);
  put(t, 'agy.version', 'Antigravity CLI v1.5.12 AGY-OUTPUT-MARK\n');
  write(join(t.local, 'agy'), `${AGY(t.fx)}echo AGY-OUTPUT-MARK\n`, 0o755);
  const { outcome, got } = await readUsage(shell, t);
  for (const mark of ['STRAY-FIELD-MARK', 'SESSION-MARK', 'DENIAL-MARK', 'CODEX-FILE-MARK', 'AGY-OUTPUT-MARK', 'ORG-ID-MARK']) {
    assert.ok(!outcome.stdout.includes(mark), `${mark} is in stdout`);
    assert.ok(!outcome.stderr.includes(mark), `${mark} is in stderr`);
    assert.ok(!JSON.stringify(got).includes(mark), `${mark} is in the typed result`);
  }
  assert.ok(!outcome.stdout.includes(digestOf(t)), 'the digest is in a line of the reply');
  assert.deepEqual(calls(t, 'agy'), [], 'the agy ran in the usage mode');
  assert.equal(got.claude.state, 'found');
});

// ───────────────── the UID-0 runs (E3S1-T20 … T26, spec E3.S1 § W8 and § W14): tests of THIS text ─────────────────

/**
 * The prelude that makes a run that is not root stand for UID 0 (spec E3.S1, § W8): `id` prints 0; `stat` answers the real owner and mode with the test's own UID
 * written as 0, answers `0 755` for the folders ABOVE the test's root (a runner's temporary folder is open to all) and for the system tools, and leaves the owner as it
 * is under a folder the test names `other`. The script keeps what `command -v stat` answered, so `command` answers the function's name for `stat`. `relative` makes
 * `command -v` answer a name with no path for those programs. The pinned text is unchanged and is what runs; this prelude is not combined with `hiding`.
 */
function uid0Prelude(t: Tree, relative: readonly string[] = [], claimed: Readonly<Record<string, string>> = {}): string {
  const realStat = onPath('stat');
  assert.ok(realStat !== undefined, 'no stat on this machine');
  assert.ok(/^[A-Za-z0-9._/-]+$/u.test(t.root), `the root ${t.root} holds a character the prelude's patterns cannot carry`);
  const other = join(t.root, 'other');
  const uid = process.getuid?.() ?? 0;

  return `id() { printf '0\\n'; }
stat() {
  if [ "$2" != '%u %a' ]; then ${realStat} "$@"; return; fi
  case $4 in
    ${t.root}|${t.root}/*)
      coai_so=$(${realStat} "$@") || return 1
      coai_ow=\${coai_so%% *}
      coai_md=\${coai_so#* }
      case $4 in
        ${other}|${other}/*) ;;
        *) if [ "$coai_ow" = ${uid} ]; then coai_ow=0; fi ;;
      esac
      printf '%s %s\\n' "$coai_ow" "$coai_md"
      ;;
    *) printf '0 755\\n' ;;
  esac
}
command() {
  [ "$1" = -v ] || return 127
  case $2 in stat|id) printf '%s\\n' "$2"; return 0 ;; esac
  case " ${relative.join(' ')} " in *" $2 "*) printf '%s\\n' "$2"; return 0 ;; esac
${Object.entries(claimed).map(([name, path]) => `  case $2 in ${name}) printf '%s\\n' ${path}; return 0 ;; esac`).join('\n')}
  coai_pre_path=$PATH
  while [ -n "$coai_pre_path" ]; do
    coai_pre_dir=\${coai_pre_path%%:*}
    case $coai_pre_path in *:*) coai_pre_path=\${coai_pre_path#*:} ;; *) coai_pre_path= ;; esac
    if [ -f "$coai_pre_dir/$2" ] && [ -x "$coai_pre_dir/$2" ]; then
      printf '%s\\n' "$coai_pre_dir/$2"
      return 0
    fi
  done
  return 1
}
`;
}

/** A tree that stands for UID 0 with everything closed: the account, the false programs in the home folder. Its `sleep` is the real one: a UID 0 run refuses a shell function there. */
function rootTree(relative: readonly string[] = []): Tree {
  const t = tree({ jq: true });
  claudeFixtures(t);
  t.prelude = uid0Prelude(t, relative);

  return t;
}


jqTest('E3S1-T20: as UID 0, all closed — the three parts, and claude and agy each record a start', async (shell) => {
  const t = rootTree();
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.codex.state === 'found' && got.agy.state === 'found', JSON.stringify(got));
  assert.ok(calls(t, 'claude').length >= 2, 'claude was not started');
  assert.equal(calls(t, 'agy').length, 2, 'agy was not started twice (version, quota)');
  assert.equal(calls(t, 'codex').length, 1, 'codex was not started');
  assert.ok(!outcome.stdout.includes(':path'), 'a :path line in a read that was whole');
  // the usage mode as UID 0 too
  const usage = await readUsage(shell, t);
  assert.ok(usage.got.claude.state === 'found' && usage.got.claude.usage.state === 'read', JSON.stringify(usage.got));
});

const warnLines = (outcome: Outcome): readonly string[] => lines(outcome).filter((line) => line.startsWith(':unsafe-path '));

jqTest('E3S1-T21: as UID 0, root\'s ~/.local/bin writable by its group (the owner\'s decision of 2026-11-27) — claude, codex and agy are read and started, each with one :unsafe-path line of that folder, and nothing is refused', async (shell) => {
  const t = rootTree();
  chmodSync(t.local, 0o775);
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.codex.state === 'found' && got.agy.state === 'found', outcome.stdout);
  assert.deepEqual(warnLines(outcome), [`:unsafe-path ${t.local}`, `:unsafe-path ${t.local}`, `:unsafe-path ${t.local}`]);
  assert.ok(got.claude.state === 'found' && got.claude.unsafePath === t.local && got.agy.state === 'found' && got.agy.unsafePath === t.local && got.codex.state === 'found' && got.codex.unsafePath === t.local);
  assert.ok(!outcome.stdout.includes('COAI-BAD') && !outcome.stdout.includes(':path ') && !outcome.stdout.includes(' unsafe'), 'something was refused');
  assert.ok(calls(t, 'claude').length >= 1 && calls(t, 'agy').length === 2 && calls(t, 'codex').length === 1, 'a vendor was not started');
  // the usage mode says it for Claude too, and the digest does not change
  const usage = await readUsage(shell, t);
  assert.ok(usage.got.claude.state === 'found' && usage.got.claude.unsafePath === t.local && usage.got.claude.usage.state === 'read', JSON.stringify(usage.got));
  // closed again: no warning at all
  chmodSync(t.local, 0o755);
  const closed = await read(shell, t, 'no-claude-usage');
  assert.deepEqual(warnLines(closed.outcome), []);
  assert.ok(closed.got.claude.state === 'found' && !('unsafePath' in closed.got.claude));
});

jqTest('E3S1-T21: as UID 0, a TOOL (sed) behind a link into a folder open to all is still refused — COAI-BAD unsafe with that folder, and no program of the place is started', async (shell) => {
  const t = rootTree();
  const open = join(t.root, 'open');
  mkdirSync(open, { recursive: true });
  chmodSync(open, 0o777);
  const sed = onPath('sed');
  assert.ok(sed !== undefined, 'no sed on this machine');
  symlinkSync(sed as string, join(open, 'sed-real'));
  symlinkSync(join(open, 'sed-real'), join(t.local, 'sed'));
  const outcome = await run(shell, t, ['no-claude-usage']);
  assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD unsafe', `:path ${open}`, 'COAI-ACCOUNTS-END']);
  assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'unsafe', path: open });
  assert.deepEqual(logLines(t), [], 'a program of the place was started');
});

jqTest('E3S1-T22: as UID 0, claude is a link whose target sits in a folder open to all — it is read and started with that folder in its warning; agy beside it has none', async (shell) => {
  const t = rootTree();
  const open = join(t.root, 'open');
  mkdirSync(open, { recursive: true });
  chmodSync(open, 0o777);
  write(join(open, 'claude-real'), CLAUDE(t.fx), 0o755);
  rmSync(join(t.local, 'claude'), { force: true });
  symlinkSync(join(open, 'claude-real'), join(t.local, 'claude'));
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.claude.unsafePath === open, outcome.stdout);
  assert.ok(calls(t, 'claude').length >= 1, 'claude was not started');
  assert.ok(got.agy.state === 'found' && !('unsafePath' in got.agy));
  assert.equal(calls(t, 'agy').length, 2, 'agy beside it was not read');
  assert.deepEqual(warnLines(outcome), [`:unsafe-path ${open}`]);
});

jqTest('E3S1-T23: as UID 0, agy is another owner\'s file behind an absolute link — it is read and started with that folder in its warning; claude beside it has none', async (shell) => {
  const t = rootTree();
  const other = join(t.root, 'other');
  mkdirSync(other, { recursive: true });
  write(join(other, 'agy-real'), AGY(t.fx), 0o755);
  rmSync(join(t.local, 'agy'), { force: true });
  symlinkSync(join(other, 'agy-real'), join(t.local, 'agy'));
  const { got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.agy.state === 'found' && got.agy.unsafePath === other, JSON.stringify(got.agy));
  assert.equal(calls(t, 'agy').length, 2, 'agy was not started');
  assert.ok(got.claude.state === 'found' && !('unsafePath' in got.claude));
  assert.ok(calls(t, 'claude').length >= 1, 'claude beside it was not read');
});

/** A false claude that starts with this first line (a script's `#!`), as UID 0: its interpreter is a wrapper that runs `sh` and stands in a folder open to all. */
function claudeBehind(t: Tree, first: (open: string) => string): string {
  const open = join(t.root, 'open');
  mkdirSync(open, { recursive: true });
  chmodSync(open, 0o777);
  write(join(open, 'interp-real'), '#!/bin/sh\nexec /bin/sh "$@"\n', 0o755);
  symlinkSync(join(open, 'interp-real'), join(t.local, 'fakenode'));
  write(join(t.local, 'claude'), CLAUDE(t.fx).replace('#!/bin/sh', first(open)), 0o755);

  return open;
}

jqTest('R3-S3: as UID 0, the interpreter of a vendor script is checked too — `#!/usr/bin/env fakenode` with fakenode in a folder open to all: claude is read and started, and the warning names that folder', async (shell) => {
  const t = rootTree();
  const open = claudeBehind(t, () => '#!/usr/bin/env fakenode');
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.claude.unsafePath === open, outcome.stdout);
  assert.ok(calls(t, 'claude').length >= 1, 'claude was not started');
  assert.deepEqual(warnLines(outcome), [`:unsafe-path ${open}`]);
  assert.ok(got.agy.state === 'found' && !('unsafePath' in got.agy) && got.codex.state === 'found' && !('unsafePath' in got.codex), 'a warning of another vendor');
});

jqTest('R3-S3: …an absolute interpreter, `env -S` with a variable before the name, and the vendor\'s own path keeps the first warning; a closed interpreter warns of nothing', async (shell) => {
  const absolute = rootTree();
  const open = claudeBehind(absolute, (folder) => `#!${folder}/interp-real`);
  const a = await read(shell, absolute, 'no-claude-usage');
  assert.ok(a.got.claude.state === 'found' && a.got.claude.unsafePath === open, a.outcome.stdout);
  const withOptions = rootTree();
  const folder = claudeBehind(withOptions, () => '#!/usr/bin/env -S FOO=bar fakenode');
  const b = await read(shell, withOptions, 'no-claude-usage');
  assert.ok(b.got.claude.state === 'found' && b.got.claude.unsafePath === folder, b.outcome.stdout);
  const closed = rootTree();
  write(join(closed.local, 'claude'), CLAUDE(closed.fx).replace('#!/bin/sh', '#!/usr/bin/env sh'), 0o755);
  const c = await read(shell, closed, 'no-claude-usage');
  assert.ok(c.got.claude.state === 'found' && !('unsafePath' in c.got.claude), c.outcome.stdout);
  assert.deepEqual(warnLines(c.outcome), []);
  // the vendor's own folder fails first: the warning names it, and the interpreter's is not added
  const both = rootTree();
  chmodSync(both.local, 0o775);
  claudeBehind(both, () => '#!/usr/bin/env fakenode');
  const d = await read(shell, both, 'no-claude-usage');
  assert.ok(d.got.claude.state === 'found' && d.got.claude.unsafePath === both.local, d.outcome.stdout);
});

jqTest('R3-S5: as UID 0, agy\'s log is checked before it is read — a folder of its path open to others: no :account line, and agy is still read; a closed path gives it', async (shell) => {
  const t = rootTree();
  const log = join(t.home, '.gemini', 'antigravity-cli', 'log');
  chmodSync(log, 0o775);
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.agy.state === 'found' && got.agy.email === '' && got.agy.usage.state === 'read', outcome.stdout);
  assert.ok(!outcome.stdout.includes(':account agy@') && !outcome.stdout.includes(AGY_MAIL), 'the account of an agy log that others can write was sent');
  assert.ok(!outcome.stdout.includes('COAI-BAD') && !outcome.stdout.includes(' unsafe'), 'a refusal');
  chmodSync(log, 0o755);
  const closed = await read(shell, t, 'no-claude-usage');
  assert.ok(closed.got.agy.state === 'found' && closed.got.agy.email === AGY_MAIL, closed.outcome.stdout);
  // the link cli.log leads to a file in a folder open to all: the same
  const t2 = rootTree();
  const open = join(t2.root, 'open');
  mkdirSync(open, { recursive: true });
  chmodSync(open, 0o777);
  rmSync(join(t2.home, '.gemini', 'antigravity-cli', 'cli.log'), { force: true });
  write(join(open, 'cli-x.log'), 'I1127 17:23:22.016270 1 server_oauth.go:221] applyAuthResult: email=agy@example.test, authMethod=consumer\n');
  symlinkSync(join(open, 'cli-x.log'), join(t2.home, '.gemini', 'antigravity-cli', 'cli.log'));
  const second = await read(shell, t2, 'no-claude-usage');
  assert.ok(second.got.agy.state === 'found' && second.got.agy.email === '', second.outcome.stdout);
});

jqTest('E3S1-T24: as UID 0, a tool (jq) writable by others — COAI-BAD unsafe with its path, and NO program of that place records a start', async (shell) => {
  const t = rootTree();
  const copy = join(t.local, 'jq');
  rmSync(copy, { force: true });
  write(copy, readFileSync(JQ as string, 'utf8'), 0o777);
  chmodSync(copy, 0o777);
  const outcome = await run(shell, t, ['no-claude-usage']);
  assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD unsafe', `:path ${copy}`, 'COAI-ACCOUNTS-END']);
  assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'unsafe', path: copy });
  assert.deepEqual(logLines(t), [], 'a program of the place was started');
});

jqTest('E3S1-T25: as UID 0, 17 links in a row are warned and read; a link that points to itself is not found; a command -v answer that is not absolute is warned with a dash', async (shell) => {
  const t = rootTree();
  rmSync(join(t.local, 'claude'), { force: true });
  write(join(t.local, 'real-claude'), CLAUDE(t.fx), 0o755);
  // claude -> l1 -> l2 … -> l17 -> real-claude: 18 links
  let target = 'real-claude';
  for (let n = 17; n >= 1; n -= 1) {
    symlinkSync(target, join(t.local, `l${n}`));
    target = `l${n}`;
  }
  symlinkSync(target, join(t.local, 'claude'));
  rmSync(join(t.local, 'agy'), { force: true });
  // a link to itself is not found by `command -v` (the kernel says it loops), so the prelude claims its path: the walk is what refuses it, and agy is then asked to run by that path
  symlinkSync('agy', join(t.local, 'agy'));
  t.prelude = uid0Prelude(t, [], { agy: join(t.local, 'agy') });
  const { got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.claude.unsafePath !== undefined && got.claude.unsafePath !== null && got.claude.unsafePath.startsWith(t.local), JSON.stringify(got.claude));
  assert.ok(calls(t, 'claude').length >= 1, 'claude behind 18 links was not started');
  assert.ok(got.agy.state === 'found' && got.agy.unsafePath === join(t.local, 'agy'), JSON.stringify(got.agy));
  assert.deepEqual(calls(t, 'agy'), [], 'a link that loops cannot be started');
  // sixteen links are still followed, and need no warning for them
  const ok = rootTree();
  rmSync(join(ok.local, 'claude'), { force: true });
  write(join(ok.local, 'real-claude'), CLAUDE(ok.fx), 0o755);
  let to = 'real-claude';
  for (let n = 15; n >= 1; n -= 1) {
    symlinkSync(to, join(ok.local, `l${n}`));
    to = `l${n}`;
  }
  symlinkSync(to, join(ok.local, 'claude'));
  const sixteen = await read(shell, ok, 'no-claude-usage');
  assert.ok(sixteen.got.claude.state === 'found' && !('unsafePath' in sixteen.got.claude), '16 links are not followed');
  // a command -v answer with no path: claude is found by its name, runs, and the warning shows a dash
  const bare = rootTree(['claude']);
  const outcome = await run(shell, bare, ['no-claude-usage']);
  assert.ok(lines(outcome).includes('COAI-VENDOR claude found'), outcome.stdout);
  assert.ok(lines(outcome).includes(':unsafe-path -'), 'the relative answer is not shown');
  assert.ok(calls(bare, 'claude').length >= 1, 'claude was not started');
  const parsed = readAccounts(outcome, 'no-claude-usage', NOW);
  assert.ok(parsed.kind === 'read' && parsed.claude.state === 'found' && parsed.claude.unsafePath === null, JSON.stringify(parsed));
});

jqTest('E3S1-T26: not UID 0 — the folders of T21 and T22 start the tools and print no :path line; and with root\'s HOME a claude in ANOTHER home and one on the caller\'s PATH record no start', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  chmodSync(t.local, 0o775);
  const open = join(t.root, 'open');
  mkdirSync(open, { recursive: true });
  chmodSync(open, 0o777);
  write(join(open, 'claude-real'), CLAUDE(t.fx), 0o755);
  rmSync(join(t.local, 'claude'), { force: true });
  symlinkSync(join(open, 'claude-real'), join(t.local, 'claude'));
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(got.claude.state === 'found' && got.agy.state === 'found', JSON.stringify(got));
  assert.ok(!outcome.stdout.includes(':path') && !outcome.stdout.includes('unsafe'), 'a :path line for a run that is not UID 0');
  assert.ok(calls(t, 'claude').length >= 1 && calls(t, 'agy').length === 2);

  const other = tree({ jq: true });
  claudeFixtures(other);
  const another = join(other.root, 'another-home', '.local', 'bin');
  mkdirSync(another, { recursive: true });
  write(join(another, 'claude'), CLAUDE(other.fx), 0o755);
  write(join(other.bin, 'claude'), CLAUDE(other.fx), 0o755);
  rmSync(join(other.local, 'claude'), { force: true });
  other.prelude = uid0Prelude(other);
  const root = await run(shell, other, ['no-claude-usage']);
  assert.ok(lines(root).includes('COAI-VENDOR claude absent'), root.stdout);
  assert.deepEqual(calls(other, 'claude'), [], 'a claude of another home or of the caller\'s PATH was started');
});

jqTest('E3S2-T23: leak, the path — a run that does not stand for UID 0 never prints a :path or :unsafe-path line; a warned path that holds a space is printed as -', async (shell) => {
  const plain = tree({ jq: true });
  claudeFixtures(plain);
  chmodSync(plain.local, 0o777);
  const outcome = await run(shell, plain, ['no-claude-usage']);
  assert.ok(!outcome.stdout.includes(':path') && !outcome.stdout.includes(':unsafe-path'), outcome.stdout);
  // as UID 0, a link whose target folder has a space in its name
  const t = rootTree();
  const spaced = join(t.root, 'open dir');
  mkdirSync(spaced, { recursive: true });
  chmodSync(spaced, 0o777);
  write(join(spaced, 'claude-real'), CLAUDE(t.fx), 0o755);
  rmSync(join(t.local, 'claude'), { force: true });
  symlinkSync(join(spaced, 'claude-real'), join(t.local, 'claude'));
  const root = await run(shell, t, ['no-claude-usage']);
  assert.ok(lines(root).includes('COAI-VENDOR claude found'), root.stdout);
  assert.ok(lines(root).includes(':unsafe-path -'), `the path with a space was printed: ${root.stdout}`);
  assert.ok(!root.stdout.includes('open dir'), 'the folder name is in stdout');
  const got = readAccounts(root, 'no-claude-usage', NOW);
  assert.ok(got.kind === 'read' && got.claude.state === 'found' && got.claude.unsafePath === null, JSON.stringify(got));
});
