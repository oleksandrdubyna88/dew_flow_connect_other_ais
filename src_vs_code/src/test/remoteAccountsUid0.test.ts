import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import test from 'node:test';
import { ACCOUNTS_SCRIPT } from '../remoteAccountsScript';
import { ownedTemp } from './ownedTemp';

/**
 * As UID 0, nothing that another user can write is started (E3.S1, spec § W8; E3S1-T20 to T26). These are tests of the TEXT of the accounts script's
 * revision 3 (the S2 spec, § R2.3): they run the real script by `sh -s`, with false programs in a temporary `HOME`.
 *
 * <p><b>How a run that is not root stands for UID 0.</b> The test sends, BEFORE the pinned text, a prelude of two shell functions: `id` prints `0`, and
 * `stat` answers the real owner and mode with the test's own UID written as `0`, answers `0 755` for every folder ABOVE the test's root (a runner's
 * temporary folder is open to all), and leaves the owner as it is under a folder the test names `other`. The pinned text is unchanged and is what runs.
 * A real UID 0 is what rows R13 and H1 of the owner's check show; this is not that.</p>
 *
 * <p>The script is given a made-up `jq` (a file in the test's own `~/.local/bin` that prints the account lines), so these runs need no real `jq`.</p>
 */

const PIN2 = '6d02ea1cb3bb1644fee0a2d65c948ff5b9ea7f0cb829496156a86569bd18bfa2';
const sha256 = (text: string): string => createHash('sha256').update(text).digest('hex');
const ON_PATH = (name: string): string | undefined => (process.env['PATH'] ?? '').split(delimiter).map((dir) => join(dir, name)).find((path) => existsSync(path));
const SH = ON_PATH('sh');
const NO_SH = SH === undefined ? 'no POSIX sh on this machine' : false;
const SYSTEM_PATH = '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin';

/** The text that runs is the constant, which is the pinned revision 3 (the S2 spec, § R2.3). */
const SCRIPT = ACCOUNTS_SCRIPT;

test('E3S1-T20…T26: the constant ACCOUNTS_SCRIPT is the pinned revision 3 these runs use', () => {
  assert.equal(sha256(SCRIPT), PIN2);
});

interface Tree {
  readonly root: string;
  readonly home: string;
  readonly local: string;
  readonly fx: string;
}

const write = (path: string, text: string, mode = 0o755): void => {
  mkdirSync(join(path, '..'), { recursive: true });
  writeFileSync(path, text, { mode });
  chmodSync(path, mode);
};

/** The false programs record each start in `fx/log`. `claude auth status --json` answers a signed-in account; the false `jq` prints the account lines. */
const FAKE_CLAUDE = (fx: string): string => `#!/bin/sh
echo "claude $*" >> "${fx}/log"
case "$1" in
  auth) echo '{"loggedIn":true,"email":"ann@example.test"}' ;;
  --version) echo '2.1.5 (Claude Code)' ;;
esac
exit 0
`;
const FAKE_AGY = (fx: string): string => `#!/bin/sh
echo "agy $*" >> "${fx}/log"
echo 'Antigravity CLI v1.5.12 (stable)'
exit 0
`;
const FAKE_JQ = (fx: string): string => `#!/bin/sh
echo "jq" >> "${fx}/log"
cat >/dev/null
printf 'COAI-VENDOR claude found\\n:email ann@example.test\\n'
exit 0
`;

function tree(): Tree {
  const root = ownedTemp('uid0');
  const home = join(root, 'home');
  const local = join(home, '.local', 'bin');
  const fx = join(root, 'fx');
  mkdirSync(local, { recursive: true });
  mkdirSync(fx, { recursive: true });
  for (const dir of [root, home, join(home, '.local'), local, fx]) {
    chmodSync(dir, 0o755);
  }
  write(join(local, 'claude'), FAKE_CLAUDE(fx));
  write(join(local, 'agy'), FAKE_AGY(fx));
  write(join(local, 'jq'), FAKE_JQ(fx));

  return { root, home, local, fx };
}

/** The prelude of § W8: `id` is 0; `stat` writes the test's own UID as 0 inside the test's root and leaves the owner under a folder named `other`. */
const prelude = (t: Tree): string => `id() { printf '0\\n'; }
stat() {
  case "$2" in
    %u\\ %a) ;;
    *) command stat "$@"; return ;;
  esac
  real=$(command stat -c '%u %a' -- "$4") || return 1
  owner=\${real%% *}
  mode=\${real#* }
  case $4 in
    "${t.root}"/other|"${t.root}"/other/*) ;;
    "${t.root}"|"${t.root}"/*) [ "$owner" = "${process.getuid?.() ?? 0}" ] && owner=0 ;;
    *) owner=0; mode=755 ;;
  esac
  printf '%s %s\\n' "$owner" "$mode"
}
`;

interface Outcome { readonly lines: readonly string[]; readonly log: readonly string[] }

/** One run of the script as the prelude's "UID 0" (or as the runner itself when `uid0` is false). `caller` is the caller's own PATH. */
function run(t: Tree, mode: string, options: { readonly uid0?: boolean; readonly caller?: string; readonly home?: string; readonly script?: string } = {}): Outcome {
  const text = `${options.uid0 === false ? '' : prelude(t)}${options.script ?? SCRIPT}`;
  const got = spawnSync(SH as string, ['-s', '--', mode], { input: text, encoding: 'utf8', timeout: 30_000, env: { HOME: options.home ?? t.home, PATH: options.caller ?? SYSTEM_PATH } });
  assert.equal(got.status, 0, `sh ended with ${got.status}: ${got.stderr}`);
  const log = existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8').split('\n').filter((line) => line !== '') : [];

  return { lines: got.stdout.split('\n').filter((line) => line !== ''), log };
}

const started = (out: Outcome, name: string): boolean => out.log.some((line) => line === name || line.startsWith(`${name} `));
const has = (out: Outcome, line: string): boolean => out.lines.includes(line);
const NO = 'no-claude-usage';

// ───────────────── the runs ─────────────────

test('E3S1-T20: as UID 0, all closed — the three parts; claude and agy each record a start', { skip: NO_SH }, () => {
  const t = tree();
  const out = run(t, NO);
  assert.deepEqual(out.lines.slice(0, 2), ['COAI-ACCOUNTS 1', 'COAI-JQ found']);
  assert.ok(has(out, 'COAI-VENDOR claude found'), JSON.stringify(out.lines));
  assert.ok(has(out, 'COAI-VENDOR agy found'));
  assert.ok(started(out, 'claude'), 'claude was started');
  assert.ok(started(out, 'agy'), 'agy was started');
  assert.equal(out.lines.some((line) => line.startsWith(':path')), false);
});

/** The `:unsafe-path` lines that follow a vendor's `found` marker, up to its `COAI-USAGE` line. */
const warnOf = (out: Outcome, vendor: string): readonly string[] => {
  const at = out.lines.indexOf(`COAI-VENDOR ${vendor} found`);
  const rest = at < 0 ? [] : out.lines.slice(at + 1);
  const end = rest.findIndex((line) => line.startsWith('COAI-USAGE'));

  return (end < 0 ? rest : rest.slice(0, end)).filter((line) => line.startsWith(':unsafe-path'));
};

test('E3S1-T21: root\'s ~/.local/bin writable by its group (the owner\'s decision of 2026-11-27) — claude and agy ARE read and started, each with one :unsafe-path line of that folder; no refusal', { skip: NO_SH }, () => {
  const t = tree();
  chmodSync(t.local, 0o775);
  const out = run(t, NO);
  assert.ok(has(out, 'COAI-VENDOR claude found') && has(out, 'COAI-VENDOR agy found'), JSON.stringify(out.lines));
  assert.deepEqual(warnOf(out, 'claude'), [`:unsafe-path ${t.local}`]);
  assert.deepEqual(warnOf(out, 'agy'), [`:unsafe-path ${t.local}`]);
  assert.equal(out.lines.some((line) => line.startsWith(':path') || line.includes(' unsafe') || line.startsWith('COAI-BAD')), false, 'a refusal');
  assert.equal(started(out, 'claude'), true);
  assert.equal(started(out, 'agy'), true);
  assert.equal(started(out, 'jq'), false, 'a false jq of the same folder is a tool: it is not started, and the system one is used');
});

test('E3S1-T21: a TOOL under a folder others can write is still refused — COAI-BAD unsafe with its path, and no program of the place is started', { skip: NO_SH }, () => {
  const t = tree();
  const open = join(t.root, 'open');
  mkdirSync(open);
  chmodSync(open, 0o777);
  const sed = ON_PATH('sed');
  assert.ok(sed !== undefined, 'no sed on this machine');
  symlinkSync(sed as string, join(open, 'sed-real'));
  symlinkSync(join(open, 'sed-real'), join(t.local, 'sed'));
  const out = run(t, NO);
  assert.deepEqual(out.lines.filter((line) => line.startsWith('COAI-BAD') || line.startsWith(':path') || line.startsWith('COAI-VENDOR')), ['COAI-BAD unsafe', `:path ${open}`]);
  assert.deepEqual(out.log, [], 'a vendor was started beside a refused tool');
});

test('E3S1-T22: claude is a link whose target sits in a folder open to all — it is read, started, and warned with that folder; agy beside it is read without a warning', { skip: NO_SH }, () => {
  const t = tree();
  const open = join(t.root, 'open');
  mkdirSync(open);
  chmodSync(open, 0o777);
  write(join(open, 'claude-real'), FAKE_CLAUDE(t.fx));
  rmSync(join(t.local, 'claude'));
  symlinkSync(join(open, 'claude-real'), join(t.local, 'claude'));
  const out = run(t, NO);
  assert.ok(has(out, 'COAI-VENDOR claude found'), JSON.stringify(out.lines));
  assert.deepEqual(warnOf(out, 'claude'), [`:unsafe-path ${open}`]);
  assert.equal(started(out, 'claude'), true);
  assert.ok(has(out, 'COAI-VENDOR agy found'));
  assert.deepEqual(warnOf(out, 'agy'), []);
  assert.ok(started(out, 'agy'));
});

test('E3S1-T23: agy is another owner\'s file behind an absolute link — it is read, started and warned with that folder; claude beside it is read without a warning', { skip: NO_SH }, () => {
  const t = tree();
  const other = join(t.root, 'other');
  mkdirSync(other);
  write(join(other, 'agy-real'), FAKE_AGY(t.fx));
  rmSync(join(t.local, 'agy'));
  symlinkSync(join(other, 'agy-real'), join(t.local, 'agy'));
  const out = run(t, NO);
  assert.ok(has(out, 'COAI-VENDOR agy found'), JSON.stringify(out.lines));
  assert.deepEqual(warnOf(out, 'agy'), [`:unsafe-path ${other}`]);
  assert.equal(started(out, 'agy'), true);
  assert.deepEqual(warnOf(out, 'claude'), []);
  assert.ok(has(out, 'COAI-VENDOR claude found'));
  assert.ok(started(out, 'claude'));
});

test('E3S1-T24: a tool (jq) writable by others — COAI-BAD unsafe with its path, and NO program of that place records a start', { skip: NO_SH }, () => {
  const t = tree();
  chmodSync(join(t.local, 'jq'), 0o757);
  const out = run(t, NO);
  assert.deepEqual(out.lines.filter((line) => line.startsWith('COAI-BAD') || line.startsWith(':path')), ['COAI-BAD unsafe', `:path ${join(t.local, 'jq')}`]);
  assert.deepEqual(out.log, [], 'nothing of the place was started');
});

test('E3S1-T25: 17 links in a row are warned and read; a link that points to itself is not found; a command -v answer that is not absolute is warned with a dash', { skip: NO_SH }, () => {
  const t = tree();
  rmSync(join(t.local, 'claude'));
  write(join(t.root, 'real-claude'), FAKE_CLAUDE(t.fx));
  let target = join(t.root, 'real-claude');
  for (let at = 17; at >= 1; at -= 1) {
    const link = at === 1 ? join(t.local, 'claude') : join(t.root, `hop${at}`);
    symlinkSync(target, link);
    target = link;
  }
  const chain = run(t, NO);
  assert.ok(has(chain, 'COAI-VENDOR claude found'), `the chain of 17: ${JSON.stringify(chain.lines)}`);
  assert.equal(warnOf(chain, 'claude').length, 1, 'the walk refused more than 16 links, so the warning is there');
  assert.equal(started(chain, 'claude'), true);
  const loop = tree();
  rmSync(join(loop.local, 'claude'));
  symlinkSync('claude', join(loop.local, 'claude'));
  const self = run(loop, NO);
  // `command -v` does not find a link that loops (ELOOP: it is not executable), so the script says `absent` and starts nothing: the property holds, the word differs from the spec's `unsafe`.
  assert.ok(has(self, 'COAI-VENDOR claude absent'), `the self link: ${JSON.stringify(self.lines)}`);
  assert.equal(started(self, 'claude'), false);
  const relative = tree();
  const asFunction = run(relative, NO, { script: `claude() { :; }\n${SCRIPT}` });
  assert.ok(has(asFunction, 'COAI-VENDOR claude found'), `the function: ${JSON.stringify(asFunction.lines)}`);
  assert.deepEqual(warnOf(asFunction, 'claude'), [':unsafe-path -'], 'a path that is not absolute is not shown');
});

test('E3S1-T26: NOT UID 0 — the folders of T21 and T22 start the tools and print no :path line', { skip: NO_SH }, () => {
  const t = tree();
  chmodSync(t.local, 0o775);
  const first = run(t, NO, { uid0: false });
  assert.ok(has(first, 'COAI-VENDOR claude found'), JSON.stringify(first.lines));
  assert.ok(started(first, 'claude') && started(first, 'agy'));
  assert.equal(first.lines.some((line) => line.startsWith(':path')), false);
  const u = tree();
  const open = join(u.root, 'open');
  mkdirSync(open);
  chmodSync(open, 0o777);
  write(join(open, 'claude-real'), FAKE_CLAUDE(u.fx));
  rmSync(join(u.local, 'claude'));
  symlinkSync(join(open, 'claude-real'), join(u.local, 'claude'));
  const second = run(u, NO, { uid0: false });
  assert.ok(started(second, 'claude'));
  assert.equal(second.lines.some((line) => line.startsWith(':path')), false);
});

test('E3S1-T26: root\'s HOME — a claude in ANOTHER home\'s .local/bin and one on the caller\'s PATH record no start', { skip: NO_SH }, () => {
  const t = tree();
  const rootHome = join(t.root, 'roothome');
  mkdirSync(join(rootHome, '.local', 'bin'), { recursive: true });
  for (const dir of [rootHome, join(rootHome, '.local'), join(rootHome, '.local', 'bin')]) {
    chmodSync(dir, 0o755);
  }
  write(join(rootHome, '.local', 'bin', 'jq'), FAKE_JQ(t.fx));
  const callerBin = join(t.root, 'callerbin');
  write(join(callerBin, 'claude'), FAKE_CLAUDE(t.fx));
  const out = run(t, NO, { home: rootHome, caller: `${callerBin}${delimiter}${SYSTEM_PATH}` });
  assert.ok(has(out, 'COAI-VENDOR claude absent'), JSON.stringify(out.lines));
  assert.equal(started(out, 'claude'), false, 'neither the other home\'s nor the caller\'s claude was started');
});
