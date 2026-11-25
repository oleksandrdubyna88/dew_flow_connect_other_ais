import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { ChildProcess, spawn } from 'node:child_process';
import {
  accessSync, constants, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, symlinkSync, utimesSync, writeFileSync,
} from 'node:fs';
import { delimiter, join } from 'node:path';
import test, { TestContext } from 'node:test';
import { AccountsRead, readAccounts } from '../remoteAccountsReply';
import { ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_SCRIPT, ACCOUNTS_TOOLS, AccountsMode } from '../remoteAccountsScript';
import { LINES, NOW } from './accountsFrames';
import { ownedTemp } from './ownedTemp';

/**
 * The REAL accounts script, run by a real `sh -s` (and by `bash --posix`), against a temporary HOME with false `claude`, `codex` and
 * `agy` programs and, where the machine has one, the REAL `jq` (E1.S2, spec § A2.11.5). Its stdout is then read by the real parser.
 *
 * <p><b>The harness is the shape of `remoteScriptRuns.test.ts`</b> (a shell found once by its absolute path, one test for each shell,
 * the child gets `HOME` and `PATH` and nothing else), not a copy of its fakes. The script under test sees `bin/` (a link to the real
 * file of each tool it needs, and to `jq`) and `$HOME/.local/bin` (the false `claude` and `agy`, where the measured VM has them).
 * Each false program appends ONE line to `fx/log` — its name, its pid, every argument in brackets, its folder, what its stdin is and its
 * `TZ` — and writes its whole environment to `fx/env.<pid>`.</p>
 *
 * <p><b>Rows marked jq</b> need the real `jq` and are skipped on a machine that has none, with the reason `no jq on this machine`;
 * on the Linux runner of CI a missing `sh` or `jq` FAILS (A2-T23). <b>What a fake cannot prove</b> is in the spec, § A2.14: the real
 * `claude` and `agy` programs, GNU `timeout` on a real VM, and what a real terminal-less ssh call prints.</p>
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

interface Tree { readonly root: string; readonly home: string; readonly local: string; readonly bin: string; readonly fx: string }

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
[ -e "${fx}/slow-$part" ] && sleep 0.3
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

const AGY = (fx: string): string => `${logged('agy', fx)}[ -e "${fx}/hang-agy" ] && exec sleep 100
[ -f "${fx}/agy.version" ] && cat "${fx}/agy.version"
exit 0
`;

const CODEX = (fx: string): string => `${logged('codex', fx)}exit 0
`;

/** Writes a file. A link of that name (the tree links the real tools into `bin/`) is removed first, so a write never goes THROUGH it to the real tool. */
function write(path: string, text: string, mode = 0o644): void {
  mkdirSync(join(path, '..'), { recursive: true });
  rmSync(path, { force: true });
  writeFileSync(path, text, { mode });
}

interface TreeOptions { readonly jq?: boolean; readonly stat?: boolean; readonly tools?: readonly string[] }

/** A HOME, a `bin/` with a link to each real tool (and `jq`, `stat` when wanted), the false programs, and the fakes' own folder. */
function tree(options: TreeOptions = {}): Tree {
  const root = ownedTemp('accounts');
  const t: Tree = { root, home: join(root, 'home'), local: join(root, 'home', '.local', 'bin'), bin: join(root, 'bin'), fx: join(root, 'fx') };
  for (const dir of [t.local, t.bin, t.fx]) {
    mkdirSync(dir, { recursive: true });
  }
  const names = [...(options.tools ?? ACCOUNTS_TOOLS), ...(options.stat === false ? [] : ['stat']), ...(options.jq === true && JQ !== undefined ? ['jq'] : [])];
  for (const name of names) {
    const real = onPath(name);
    assert.ok(real !== undefined, `the tool ${name} is not on this machine's PATH`);
    symlinkSync(real as string, join(t.bin, name));
  }
  write(join(t.local, 'claude'), CLAUDE(t.fx), 0o755);
  write(join(t.local, 'agy'), AGY(t.fx), 0o755);
  write(join(t.bin, 'codex'), CODEX(t.fx), 0o755);
  write(join(t.fx, 'agy.version'), 'Antigravity CLI v1.5.12 (stable)\n');

  return t;
}

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

const UUID1 = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const UUID2 = '7c1d44b0-2a9e-4c55-9b3e-5f0a8e2d6c21';
const UUID3 = '3a9f1c77-5d2b-4e88-8f10-9b6c2d4e1a05';
const UUID4 = '9e4b2f10-6c3d-4a77-b1e2-0d5f8c7a3b19';

const RATE = (used: number, over: Readonly<Record<string, unknown>> = {}): Record<string, unknown> => ({
  limit_id: 'codex', limit_name: null, primary: { used_percent: used, window_minutes: 10080, resets_at: 1795518850 }, secondary: null,
  credits: { has_credits: false, unlimited: false, balance: '0' }, plan_type: 'prolite', ...over,
});
const record = (stamp: string, used: number, over: Readonly<Record<string, unknown>> = {}): string => JSON.stringify({
  timestamp: stamp, type: 'event_msg', payload: { type: 'token_count', info: { last_token_usage: { input_tokens: 1 }, model_context_window: 1 }, rate_limits: RATE(used, over) },
});

/** A Codex session file in a day folder, its change time set by hand (integer seconds). */
function session(t: Tree, day: string, name: string, lines: readonly string[], mtime: number, tail = '\n'): string {
  const file = join(t.home, '.codex', 'sessions', ...day.split('/'), name);
  write(file, lines.length === 0 ? '' : `${lines.join('\n')}${tail}`);
  utimesSync(file, mtime, mtime);

  return file;
}
const rollout = (uuid: string, stamp = '2026-11-17T19-32-36'): string => `rollout-${stamp}-${uuid}.jsonl`;

interface Outcome { readonly code: number; readonly stdout: string; readonly stderr: string }

function started(shell: Shell, t: Tree, operands: readonly string[]): ChildProcess {
  return spawn(shell.path, [...shell.args, ...SHELL_ARGS, ...operands], { cwd: t.root, env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'pipe'] });
}

/** Run a script by `<shell> -s -- <operands>` on stdin; the environment holds NOTHING but HOME and PATH. */
function run(shell: Shell, t: Tree, operands: readonly string[], script: string = ACCOUNTS_SCRIPT, limit = 25_000): Promise<Outcome> {
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
    child.stdin?.end(script);
  });
}

const logLines = (t: Tree): readonly string[] => (existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8').split('\n').filter((line) => line.length > 0) : []);
const calls = (t: Tree, name: string): readonly string[] => logLines(t).filter((line) => line.startsWith(`${name} `));
const argsOf = (line: string): string => line.split(' ')[2] ?? '';
const envFiles = (t: Tree): readonly string[] => readdirSync(t.fx).filter((name) => name.startsWith('env.')).map((name) => readFileSync(join(t.fx, name), 'utf8'));
const lines = (outcome: Outcome): readonly string[] => outcome.stdout.split('\n').filter((line) => line.length > 0);

async function read(shell: Shell, t: Tree, mode: AccountsMode): Promise<{ readonly outcome: Outcome; readonly got: Extract<AccountsRead, { kind: 'read' }> }> {
  const outcome = await run(shell, t, [mode]);
  const got = readAccounts(outcome, mode, NOW);
  assert.equal(got.kind, 'read', `the reply is not read (${JSON.stringify(got)}): ${outcome.stdout.slice(0, 400)}`);

  return { outcome, got: got as Extract<AccountsRead, { kind: 'read' }> };
}

const whole = (outcome: Outcome): void => {
  assert.ok(outcome.stdout.startsWith('COAI-ACCOUNTS 1\n') && outcome.stdout.endsWith('COAI-ACCOUNTS-END\n'), `the frame is not whole: ${JSON.stringify(outcome.stdout.slice(-200))}`);
  assert.equal(outcome.code, 0);
};

const MODES: readonly AccountsMode[] = ['claude-usage', 'no-claude-usage'];

// ───────────────── A2-T1: one function, called last; a cut does nothing ─────────────────

/** The text with the two `/dev/null` of its LAST line written as a temporary folder's, so a cut inside the path writes under that folder only. */
function derived(text: string, folder: string): string {
  // The function is the first 221 lines; what comes after it is the call, and only there the path is mapped (the function's own >/dev/null stay).
  return text.split('\n').map((line, index) => (index >= 221 ? line.replaceAll('/dev/null', `${folder}/dev/null`) : line)).join('\n');
}

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
  const last = all[221] as string;
  const prefix = `${all.slice(0, 221).join('\n')}\n`;
  const cuts: { readonly what: string; readonly script: string; readonly byLine: boolean }[] = [
    ...Array.from({ length: 221 }, (_, index) => ({ what: `after line ${index + 1}`, script: `${all.slice(0, index + 1).join('\n')}\n`, byLine: true })),
    ...Array.from({ length: last.length - 1 }, (_, index) => ({ what: `at byte ${index + 1} of the last line`, script: prefix + last.slice(0, index + 1), byLine: false })),
  ];
  // Six at a time: every cut is its own shell, and none of them may start a program or write a path, so they do not meet.
  const outcomes: Outcome[] = [];
  for (let at = 0; at < cuts.length; at += 6) {
    outcomes.push(...await Promise.all(cuts.slice(at, at + 6).map((cut) => run(shell, t, ['claude-usage'], cut.script))));
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
  const base = ACCOUNTS_SCRIPT.split('\n').slice(0, 221);
  const call = `</dev/null 2>${folder}/dev/null coai_accounts "$@"`;
  const from = call.indexOf(`${folder}/dev/null`) + folder.length;
  for (let k = from + 2; k < from + '/dev/null'.length; k += 1) {
    await run(shell, t, ['no-claude-usage'], `${base.join('\n')}\n${call.slice(0, k)}`);
  }
  const made = ['d', 'de', 'dev/n', 'dev/nu', 'dev/nul'].filter((name) => existsSync(join(folder, name)));
  assert.ok(made.length >= 1, 'no cut of the two-line form created a path, so the test cannot see a created path');
});

// ───────────────── A2-T3 (the sh half): the operands ─────────────────

shellTest('A2-T3: a wrong or missing operand starts nothing, and says so; a right one starts agy', async (shell) => {
  for (const operands of [[], ['usage'], ['CLAUDE-USAGE'], ['claude-usage;id'], ['claude-usage', 'x'], ['-x']]) {
    const t = tree();
    const outcome = await run(shell, t, operands);
    assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD operands', 'COAI-ACCOUNTS-END'], `a wrong operand ${JSON.stringify(operands)} was not refused`);
    assert.deepEqual(logLines(t), [], `${JSON.stringify(operands)}: a program was started`);
    assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'operands' });
  }
  for (const mode of MODES) {
    const t = tree();
    await run(shell, t, [mode]);
    assert.equal(calls(t, 'agy').length, 1, `${mode}: a right operand did not start agy (a wrong one started it, or the operand case is gone)`);
  }
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
  const found = (await read(shell, t, 'claude-usage')).got.claude;
  assert.ok(found.state === 'found' && found.email === 'someone@example.com' && found.org === 'Example Org' && found.plan === 'team' && found.version === '2.1.326', JSON.stringify(found));
  assert.ok(found.state === 'found' && found.usage.state === 'read' && found.usage.windows.length === 3, JSON.stringify(found));

  const out = tree({ jq: true });
  claudeFixtures(out, { auth: '{"loggedIn":false}' });
  put(out, 'claude.auth.code', '1');
  assert.deepEqual((await read(shell, out, 'claude-usage')).got.claude, { state: 'signed-out' });
  assert.equal(calls(out, 'claude').length, 1, 'a signed-out account started a second claude program');

  const none = tree({ jq: true });
  renameSync(join(none.local, 'claude'), join(none.root, 'claude-away'));
  assert.deepEqual((await read(shell, none, 'claude-usage')).got.claude, { state: 'absent' });

  const bad = tree({ jq: true });
  claudeFixtures(bad, { auth: 'this is not json' });
  assert.deepEqual((await read(shell, bad, 'claude-usage')).got.claude, { state: 'unreadable', why: 'auth' });
});

jqTest('A2-T5: Claude usage — a model call, no line of the forms, an error; and the mode no-claude-usage skips it', async (shell) => {
  const usageOf = async (usage: string, code?: string): Promise<unknown> => {
    const t = tree({ jq: true });
    claudeFixtures(t, { usage });
    if (code !== undefined) {
      put(t, 'claude.usage.code', code);
    }
    const { outcome, got } = await read(shell, t, 'claude-usage');
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

jqTest('A2-T5: Codex — found, read; absent; found by ~/.codex alone; no sessions; no record', async (shell) => {
  const t = tree({ jq: true });
  session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  const codex = (await read(shell, t, 'no-claude-usage')).got.codex;
  assert.ok(codex.state === 'found' && codex.usage.state === 'read' && codex.plan === 'prolite', JSON.stringify(codex));

  const absent = tree({ jq: true });
  renameSync(join(absent.bin, 'codex'), join(absent.root, 'codex-away'));
  assert.deepEqual((await read(shell, absent, 'no-claude-usage')).got.codex, { state: 'absent' });

  const folderOnly = tree({ jq: true });
  renameSync(join(folderOnly.bin, 'codex'), join(folderOnly.root, 'codex-away'));
  mkdirSync(join(folderOnly.home, '.codex'), { recursive: true });
  const only = (await read(shell, folderOnly, 'no-claude-usage')).got.codex;
  assert.ok(only.state === 'found' && only.usage.state === 'failed' && only.usage.why === 'no-sessions', JSON.stringify(only));

  const empty = tree({ jq: true });
  session(empty, '2026/11/17', rollout(UUID1), ['{"type":"response_item","payload":{"type":"message"}}'], 1794938000);
  const none = (await read(shell, empty, 'no-claude-usage')).got.codex;
  assert.ok(none.state === 'found' && none.usage.state === 'failed' && none.usage.why === 'none', JSON.stringify(none));
});

shellTest('A2-T5: agy — found, and absent', async (shell) => {
  const t = tree();
  assert.equal((await read(shell, t, 'no-claude-usage')).got.agy.state, 'found');
  const gone = tree();
  renameSync(join(gone.local, 'agy'), join(gone.root, 'agy-away'));
  assert.equal((await read(shell, gone, 'no-claude-usage')).got.agy.state, 'absent');
});

shellTest('A2-T5 companion: a missing tool is said as COAI-BAD tool, not read as an account fault', async (shell) => {
  const t = tree({ tools: ACCOUNTS_TOOLS.filter((tool) => tool !== 'tail') });
  const outcome = await run(shell, t, ['no-claude-usage']);
  assert.deepEqual(lines(outcome), ['COAI-ACCOUNTS 1', 'COAI-BAD tool', 'COAI-ACCOUNTS-END']);
  assert.deepEqual(readAccounts(outcome, 'no-claude-usage', NOW), { kind: 'unreadable', why: 'tool' });
});

// ───────────────── A2-T6: every vendor command is the parent's, word for word ─────────────────

const AUTH_ARGS = '[auth][status][--json]';
const VERSION_ARGS = '[--version]';
const USAGE_ARGS = '[-p][/usage][--output-format][json][--no-session-persistence][--strict-mcp-config][--setting-sources][]';

jqTest('A2-T6: the commands, their order, their folder, their stdin and their TZ', async (shell) => {
  const t = tree({ jq: true });
  claudeFixtures(t);
  await read(shell, t, 'claude-usage');
  const claude = calls(t, 'claude');
  assert.deepEqual(claude.map(argsOf), [AUTH_ARGS, VERSION_ARGS, USAGE_ARGS], 'the claude commands, in order');
  assert.deepEqual(calls(t, 'agy').map(argsOf), [VERSION_ARGS]);
  for (const line of [...claude, ...calls(t, 'agy')]) {
    assert.ok(line.includes(' cwd=/ stdin=/dev/null '), `a program was started from another folder or with another stdin: ${line}`);
    assert.equal(line.endsWith(' tz=UTC'), argsOf(line) === USAGE_ARGS, `TZ is UTC on the -p line and only there: ${line}`);
  }
  assert.ok(envFiles(t).every((env) => !env.includes('LC_ALL=')), 'a vendor program saw an LC_ALL');

  const plain = tree({ jq: true });
  claudeFixtures(plain);
  await read(shell, plain, 'no-claude-usage');
  assert.deepEqual(calls(plain, 'claude').map(argsOf), [AUTH_ARGS, VERSION_ARGS], 'the usage command ran for no-claude-usage');
});

// ───────────────── A2-T7: agy gets nothing but --version; codex is never started; no credential file is opened ─────────────────

const CREDENTIAL_FILES = ['.claude/.credentials.json', '.claude.json', '.codex/auth.json', '.gemini/antigravity-cli/antigravity-oauth-token', '.gemini/antigravity-cli/cli.log'];
const SECRET_JSON = '{"accessToken":"sk-ant-oat01-SECRET-MARK-0123456789abcdef"}\n';
const YEAR_2001 = 978_307_200;

function plantCredentials(t: Tree): readonly string[] {
  const files = CREDENTIAL_FILES.map((name) => join(t.home, name));
  for (const file of files) {
    write(file, SECRET_JSON, 0o600);
    utimesSync(file, YEAR_2001, YEAR_2001);
  }

  return files;
}

shellTest('A2-T7: agy gets one --version, codex is never started, and no credential file is opened', async (shell, context) => {
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
    session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
    const files = plantCredentials(t);
    await run(shell, t, [mode]);
    assert.deepEqual(calls(t, 'agy').map(argsOf), [VERSION_ARGS], `${mode}: agy got more than --version`);
    assert.ok(calls(t, 'agy')[0]?.includes(' cwd=/ stdin=/dev/null '), `${mode}: agy was started from another folder or with another stdin: ${calls(t, 'agy')[0]}`);
    assert.deepEqual(calls(t, 'codex'), [], `${mode}: codex was started`);
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
  const outcome = await run(shell, t, [mode], ACCOUNTS_SCRIPT, 30_000);

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
  assert.ok(seconds >= 3.8, `agy was ended after ${seconds} s, before its limit of 4 s`);
  const got = readAccounts(outcome, 'no-claude-usage', NOW);
  assert.ok(got.kind === 'read' && got.agy.state === 'found' && got.agy.version === null, JSON.stringify(got));
  await noneAlive(t);
});

jqRows += 1;
test('A2-T8: the six hang runs, started together in ONE test', { timeout: 40_000, ...(SHELLS.length === 0 ? { skip: NO_SH } : (JQ === undefined ? { skip: NO_JQ } : {})) }, async () => {
  const sh = SHELLS[0] as Shell;
  const bash = SHELLS.find((shell) => shell.label !== 'sh') ?? sh;
  const make = (setup: (t: Tree) => void): Tree => {
    const t = tree({ jq: true });
    claudeFixtures(t);
    session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
    setup(t);

    return t;
  };
  const version = make((t) => put(t, 'hang-version', ''));
  const account = make((t) => put(t, 'hang-auth', ''));
  const usageSh = make((t) => put(t, 'child-usage', ''));
  const usageBash = make((t) => put(t, 'child-usage', ''));
  const agy = make((t) => put(t, 'hang-agy', ''));
  const file = make((t) => {
    session(t, '2026/11/17', rollout(UUID2), [record('2026-11-16T10:00:00.000Z', 50)], 1794930000);
    const newest = session(t, '2026/11/17', rollout(UUID3), [record('2026-11-17T19:40:00.000Z', 99)], 1794939000);
    put(t, 'hang-file', newest);
    write(join(t.bin, 'tail'), `#!/bin/sh\nPATH=/usr/bin:/bin\nif [ -f "${t.fx}/hang-file" ] && [ "$(readlink /proc/$$/fd/0)" = "$(cat "${t.fx}/hang-file")" ]; then exec sleep 100; fi\nexec ${onPath('tail')} "$@"\n`, 0o755);
  });
  const [v, a, us, ub, g, f] = await Promise.all([
    timed(sh, version, 'claude-usage'), timed(sh, account, 'claude-usage'), timed(sh, usageSh, 'claude-usage'),
    timed(bash, usageBash, 'claude-usage'), timed(sh, agy, 'claude-usage'), timed(sh, file, 'no-claude-usage'),
  ]);
  for (const [name, done] of [['version', v], ['account', a], ['usage sh', us], ['usage bash', ub], ['agy', g], ['codex file', f]] as const) {
    whole(done.outcome);
    assert.equal(readAccounts(done.outcome, name === 'codex file' ? 'no-claude-usage' : 'claude-usage', NOW).kind, 'read', `${name}: ${done.outcome.stdout.slice(0, 300)}`);
  }
  const claudeOf = (outcome: Outcome): string => outcome.stdout;
  assert.ok(v.seconds >= 1.8 && !claudeOf(v.outcome).includes(':version 2.1'), 'the version command: ended early, or its version was read');
  assert.ok(claudeOf(v.outcome).includes('COAI-USAGE claude read'), 'a version that hangs must not stop the usage read');
  assert.ok(a.seconds >= 2.8 && claudeOf(a.outcome).includes('COAI-VENDOR claude unreadable timeout'), 'the account command that hangs is unreadable timeout');
  assert.equal(calls(account, 'claude').filter((line) => argsOf(line).startsWith('[-p]')).length, 0, 'a usage command started after the account command timed out');
  for (const done of [us, ub]) {
    assert.ok(done.seconds >= 7.8 && done.outcome.stdout.includes('COAI-USAGE claude failed timeout'), `the usage command that starts a child and hangs: ${done.outcome.stdout.slice(0, 300)}`);
  }
  assert.ok(g.seconds >= 3.8 && g.outcome.stdout.includes('COAI-USAGE agy none') && !g.outcome.stdout.includes(':version 1.5'), 'agy that hangs');
  assert.ok(f.seconds >= 0.8 && f.seconds < 3 && f.outcome.stdout.includes(':window 14 10080'), `the read of the newest session file that hangs: ${f.seconds} s, ${f.outcome.stdout.slice(0, 400)}`);
  for (const t of [version, account, usageSh, usageBash, agy, file]) {
    await noneAlive(t);
  }
});

// ───────────────── A2-T9 and A2-T10: no secret, no activity leaves — SEEN ─────────────────

/** Everything one run left that could hold a leak: stdout, stderr, the arguments, every environment, the typed result. */
function everything(t: Tree, outcome: Outcome, mode: AccountsMode): string {
  // the value parsed with the mode that RAN, and a reply that is read: a value that is not would hold no marker whatever leaked
  const parsed = readAccounts(outcome, mode, NOW);
  assert.equal(parsed.kind, 'read', `${mode}: the reply is not read, so the scan of its typed value sees nothing: ${JSON.stringify(parsed)}`);
  const typed = JSON.stringify(parsed);

  return [outcome.stdout, outcome.stderr, existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8') : '', ...envFiles(t), typed].join('\n');
}

jqTest('A2-T9: no secret leaves — the five false credential files hold a marker token', async (shell) => {
  for (const mode of MODES) {
    const t = tree({ jq: true });
    claudeFixtures(t);
    session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
    plantCredentials(t);
    const calls0 = { n: 0 };
    const original = console.error;
    console.error = (): void => {
      calls0.n += 1;
    };
    let outcome: Outcome;
    try {
      outcome = await run(shell, t, [mode]);
      assert.equal(readAccounts(outcome, mode, NOW).kind, 'read', `${mode}: the reply is not read`);
    } finally {
      console.error = original;
    }
    assert.equal(outcome.stderr, '', 'stderr is not empty');
    assert.equal(calls0.n, 0, 'the parser wrote to the console');
    const all = everything(t, outcome, mode);
    assert.ok(!all.includes('SECRET-MARK'), `${mode}: SECRET-MARK is in an output, an argument, an environment or the typed result`);
    assert.ok(!outcome.stdout.includes('ORG-ID-MARK'), `${mode}: the organization id left the VM`);
  }
});

jqTest('A2-T10: the person\'s activity does not leave — Claude\'s usage text and a Codex transcript', async (shell) => {
  const t = tree({ jq: true });
  const activity = `${USAGE_TEXT('Requests in the last 24 hours: ACTIVITY-MARK\nCurrent sessions of ACTIVITY-MARK\n')}Current week (all models): 77% used \u00b7 resets Nov 22, 3am (UTC) ACTIVITY-MARK\n`;
  claudeFixtures(t, { usage: usageJson(activity) });
  const talk = (text: string): string => JSON.stringify({ type: 'response_item', payload: { type: 'message', text: `TRANSCRIPT-MARK rate_limits ${text}` } });
  session(t, '2026/11/17', rollout(UUID1), [talk('one'), record('2026-11-17T19:32:36.474Z', 14, { extra: 'TRANSCRIPT-MARK' }), talk('two')], 1794938000);
  const { outcome, got } = await read(shell, t, 'claude-usage');
  const out = lines(outcome);
  assert.equal(out.filter((line) => line.startsWith(':line ')).length, 3);
  assert.ok(out.includes(':unmatched 2'), `unmatched: ${out.filter((line) => line.startsWith(':unmatched')).join()}`);
  assert.ok(!outcome.stdout.includes('ACTIVITY-MARK'), 'ACTIVITY-MARK is in a :line');
  assert.ok(out.includes(':window 14 10080 1795518850'), 'the right window');
  assert.ok(!outcome.stdout.includes('TRANSCRIPT-MARK'), 'TRANSCRIPT-MARK left the VM');
  assert.equal(got.codex.state, 'found');
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
    write(join(t.bin, name), logged(name, t.fx), 0o755);
  }
  const word = MANAGERS.find(([path]) => executable(path))?.[1] ?? 'none';
  const { outcome, got } = await read(shell, t, 'claude-usage');
  assert.ok(lines(outcome).includes(`COAI-JQ missing ${word}`), outcome.stdout);
  assert.deepEqual(got.claude, { state: 'unreadable', why: 'no-jq' });
  assert.equal(calls(t, 'claude').length, 0, 'a claude was started on a host without jq');
  assert.deepEqual(got.codex, { state: 'unreadable', why: 'no-jq' });
  assert.ok(got.agy.state === 'found' && got.agy.version === '1.5.12', JSON.stringify(got.agy));
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
      session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
      plantCredentials(t);
      const before = listing(t.home);
      const rootBefore = readdirSync(t.root).sort();
      whole(await run(shell, t, [mode]));
      assert.deepEqual(listing(t.home), before, `${mode} (jq ${jq}): the home tree changed`);
      assert.deepEqual(readdirSync(t.root).sort(), rootBefore, `${mode} (jq ${jq}): the tree's root has a new entry`);
      assert.ok(logLines(t).every((line) => line.includes(' cwd=/ ')), 'a program saw another working folder than /');
    }
  }
});

// ───────────────── A2-T19 (the script's half): which Codex files are read, how far, and which record wins ─────────────────

const codexLinesOf = (outcome: Outcome): readonly string[] => lines(outcome).filter((line) => line.startsWith(':window ') || line.startsWith(':plan ') || line.startsWith(':as-of ') || line.startsWith('COAI-USAGE codex'));
const PAD = (n: number): string => `{"type":"response_item","payload":{"text":"${'a'.repeat(998)}"}}\n`.repeat(n);

async function codex(shell: Shell, t: Tree): Promise<readonly string[]> {
  return codexLinesOf((await read(shell, t, 'no-claude-usage')).outcome);
}

jqTest('A2-T19: the measured record — primary weekly, secondary null — and both windows', async (shell) => {
  const t = tree({ jq: true });
  session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  assert.deepEqual(await codex(shell, t), [':plan prolite', 'COAI-USAGE codex read', ':window 14 10080 1795518850', ':as-of 2026-11-17T19:32:36.474Z 1794938000']);
  const both = tree({ jq: true });
  session(both, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14, { secondary: { used_percent: 3.456, window_minutes: 300, resets_at: 1795500000 } })], 1794938000);
  const out = await codex(shell, both);
  assert.ok(out.includes(':window 14 10080 1795518850') && out.includes(':window 3.46 300 1795500000'), out.join('|'));
});

jqTest('A2-T19: no record, and a record further from the end than the tail, are failed none', async (shell) => {
  const t = tree({ jq: true });
  session(t, '2026/11/17', rollout(UUID1), ['{"type":"x"}'], 1794938000);
  assert.deepEqual(await codex(shell, t), ['COAI-USAGE codex failed none']);
  const far = tree({ jq: true });
  session(far, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000, `\n${PAD(1150)}`);
  assert.deepEqual(await codex(shell, far), ['COAI-USAGE codex failed none']);
});

jqTest('A2-T19: the newer record wins — also when the file changed last holds the older timestamp', async (shell) => {
  const t = tree({ jq: true });
  session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  session(t, '2026/11/17', rollout(UUID2, '2026-11-16T10-00-00'), [record('2026-11-16T10:00:00.000Z', 50)], 1794939000);
  assert.ok((await codex(shell, t)).includes(':window 14 10080 1795518850'), 'the record with the older timestamp won');
  const tie = tree({ jq: true });
  session(tie, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  session(tie, '2026/11/17', rollout(UUID2), [record('2026-11-17T19:32:36.474Z', 60)], 1794939000);
  assert.ok((await codex(shell, tie)).includes(':window 60 10080 1795518850'), 'on equal times the older file won');
});

jqTest('A2-T19: three files of two days are read — a fourth, a third day, and names outside the form are not', async (shell) => {
  const t = tree({ jq: true });
  session(t, '2026/11/15', rollout(UUID4), [record('2026-11-18T00:00:00.000Z', 99)], 1794900000);
  session(t, '2026/11/16', rollout(UUID1), [record('2026-11-18T00:00:00.000Z', 99)], 1794910000);
  session(t, '2026/11/16', rollout(UUID2), [record('2026-11-16T10:00:00.000Z', 10)], 1794920000);
  session(t, '2026/11/17', rollout(UUID3), [record('2026-11-17T10:00:00.000Z', 20)], 1794930000);
  session(t, '2026/11/17', rollout(UUID4), [record('2026-11-17T11:00:00.000Z', 30)], 1794940000);
  const out = await codex(shell, t);
  assert.ok(out.includes(':window 30 10080 1795518850'), out.join('|'));
  assert.ok(!out.join('|').includes(' 99 '), 'a file that must not be read was read');

  const names = tree({ jq: true });
  session(names, '2026/11/17', rollout(UUID1), [record('2026-11-17T10:00:00.000Z', 20)], 1794930000);
  for (const name of ['notes.jsonl', 'rollout-x.jsonl', `${rollout(UUID2)}.bak`, `rollout-a b-${UUID2}.jsonl`]) {
    session(names, '2026/11/17', name, [record('2026-11-18T10:00:00.000Z', 99)], 1794950000);
  }
  const named = await codex(shell, names);
  assert.ok(named.includes(':window 20 10080 1795518850') && !named.join('|').includes(' 99 '), named.join('|'));
});

jqTest('A2-T19: a cut first line and a last line with no line end do no harm; the file time is the newest file\'s', async (shell) => {
  const t = tree({ jq: true });
  const file = session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000, '');
  writeFileSync(file, `${PAD(1100)}${record('2026-11-17T19:32:36.474Z', 14)}`);
  utimesSync(file, 1794938000, 1794938000);
  const out = await codex(shell, t);
  assert.deepEqual(out.filter((line) => line.startsWith(':as-of')), [`:as-of 2026-11-17T19:32:36.474Z ${Math.floor(statSync(file).mtimeMs / 1000)}`]);
  assert.ok(out.includes(':window 14 10080 1795518850'));
});

jqTest('A2-T19: a host without stat says no-stat, and an ls that prints nothing is failed error', async (shell) => {
  const t = tree({ jq: true, stat: false });
  session(t, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  const { outcome, got } = await read(shell, t, 'no-claude-usage');
  assert.ok(lines(outcome).includes(':as-of 2026-11-17T19:32:36.474Z no-stat'), 'the last word of :as-of is not no-stat');
  assert.ok(got.codex.state === 'found' && got.codex.usage.state === 'read' && got.codex.usage.statMissing && got.codex.usage.asOf?.from === 'record', 'a host without stat was read as a time that is not known');

  const empty = tree({ jq: true });
  session(empty, '2026/11/17', rollout(UUID1), [record('2026-11-17T19:32:36.474Z', 14)], 1794938000);
  write(join(empty.bin, 'ls'), '#!/bin/sh\nexit 0\n', 0o755);
  assert.deepEqual(await codex(shell, empty), ['COAI-USAGE codex failed error']);
});

// ───────────────── A2-T22: the script bounds its own reply ─────────────────

jqTest('A2-T22: a 10 MB result, 16 000 good lines, and an email and an organization of hostile size', async (shell) => {
  const big = tree({ jq: true });
  claudeFixtures(big, { usage: usageJson('x'.repeat(10_000_000)) });
  const huge = await read(shell, big, 'claude-usage');
  assert.ok(lines(huge.outcome).includes('COAI-USAGE claude failed error'), 'a 10 MB result is not failed error');

  const many = tree({ jq: true });
  const goodLine = `Current session: 1% used \u00b7 resets Nov 19, 3pm (UTC)`;
  const count = 16_000;
  claudeFixtures(many, { usage: usageJson(`${Array.from({ length: count }, () => goodLine).join('\n')}\n`) });
  const wide = await read(shell, many, 'claude-usage');
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
      ? spawn('/bin/sh', ['-c', 'trap "" PIPE; exec "$@"', 'sh', shell.path, ...shell.args, ...SHELL_ARGS, 'claude-usage'], { cwd: t.root, env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'ignore'] })
      : started(shell, t, ['claude-usage']);
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
