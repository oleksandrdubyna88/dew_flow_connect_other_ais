// node --test scripts/lib/*.test.mjs — the confinement probe's shim arms speak the SHIPPED shim's contract (E4.3's flags, E4.5 brief
// item 12): `--home <owned root>`, `--persons-bin` and `--locks` on the argv, the owned root laid out as `<root>/home` beside an empty
// `<root>/os`, the agent's OS-home redirect and the person's `bin` first on PATH. Nothing here runs Kimi or spends a token: the argv is
// read against the shim's own `--help` text, and the built shim is asked ONE turn with an executable that does not exist and an owned
// root without a sign-in, so it parses every flag and refuses at the hold — before any agent could start.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, test } from 'node:test';
import * as kimi from './kimi.mjs';
import * as owned from './kimi-home.mjs';

const WINDOWS = process.platform === 'win32';
const here = dirname(fileURLToPath(import.meta.url));
const repo = join(here, '..', '..');
const SHIM = join(repo, 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', WINDOWS ? 'coai-mcp.exe' : 'coai-mcp');
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-kimi-shim-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

const exported = (module, names) => names.forEach((name) => assert.equal(typeof module[name], 'function', `${name} is not exported — the probe cannot speak the shim's contract`));
const FULL = { cwd: 'C:\\w', model: 'kimi-code/k3-256k', deadlineSeconds: 240, executable: 'C:\\k\\kimi.exe', root: 'C:\\r', personsBin: 'C:\\p\\bin', locks: 'C:\\l' };

test('a shim turn on an owned root carries --home, --persons-bin and --locks, each with its value, after the old flags', () => {
  exported(kimi, ['shimArgs']);
  const args = kimi.shimArgs(FULL);

  assert.deepEqual(args, ['--ask-acp', '--profile', 'kimi', '--cwd', 'C:\\w', '--model', 'kimi-code/k3-256k', '--deadline-seconds', '240',
    '--executable', 'C:\\k\\kimi.exe', '--home', 'C:\\r', '--persons-bin', 'C:\\p\\bin', '--locks', 'C:\\l']);
});

test('without a root the argv is the one the shim answers 65 to — the old shape is still what a root-less call gets, so the refusal can be measured', () => {
  const args = kimi.shimArgs({ ...FULL, root: '', personsBin: '', locks: '' });

  assert.ok(!args.includes('--home') && !args.includes('--persons-bin') && !args.includes('--locks'), args.join(' '));
});

test('a root with no person\'s bin or no lock folder is a half-plumbed call: refused here, not discovered as a 65 mid-run', () => {
  assert.throws(() => kimi.shimArgs({ ...FULL, personsBin: '' }), /--persons-bin/u);
  assert.throws(() => kimi.shimArgs({ ...FULL, locks: '' }), /--locks/u);
});

test('every flag the probe sends is named in the shim\'s own --help text', () => {
  // reads-another-program: a positive claim about the shim's help text — each flag the probe sends is named there, so a flag renamed
  // on the server fails here loudly instead of the probe sending one the shim does not read.
  const help = readFileSync(join(repo, 'src_mcp', 'src', 'Program.cs'), 'utf8');
  const flags = kimi.shimArgs(FULL).filter((word) => word.startsWith('--'));

  assert.ok(flags.length >= 9, 'the loop below would check nothing');
  for (const flag of flags) {
    assert.ok(help.includes(flag), `${flag} is not in the shim's help — the probe sends a flag the shim does not read`);
  }
});

test('an owned root is <root>/home beside an EMPTY <root>/os, and a home is mapped back to its root', () => {
  exported(owned, ['ownedRoot']);
  exported(kimi, ['rootOfHome']);
  const made = owned.ownedRoot(join(base, 'kh'));

  assert.equal(made.home, join(base, 'kh', 'home'));
  assert.equal(made.os, join(base, 'kh', 'os'));
  assert.ok(existsSync(made.home) && existsSync(made.os), 'the layout was not made');
  assert.equal(kimi.rootOfHome(made.home), made.root);
  assert.equal(kimi.rootOfHome(join(base, 'h1')), '', 'a folder not named home is no owned root — the shim would refuse it');
  assert.equal(kimi.rootOfHome(''), '');
});

test('the shim\'s own environment: KIMI_CODE_HOME the root\'s home, every OS-home name the root\'s os, the person\'s bin FIRST on PATH', () => {
  exported(owned, ['shimEnv']);
  const root = join(base, 'env');
  const env = owned.shimEnv({ root, personsBin: join(base, 'persons', 'bin') });

  assert.equal(env.KIMI_CODE_HOME, join(root, 'home'));
  assert.equal(env.KIMI_CODE_NO_AUTO_UPDATE, '1');
  for (const name of ['USERPROFILE', 'HOME']) {
    assert.equal(env[name], join(root, 'os'), name);
  }
  assert.ok(env.PATH.startsWith(`${join(base, 'persons', 'bin')};`), `PATH does not begin with the person's bin: ${env.PATH}`);
  assert.ok(env.PATH.split(';').length > 1, 'the parent PATH was dropped');
});

test('an arm that varies what the shim now fixes is refused by name — it cannot be measured through the shim, and is not run as something else', () => {
  exported(kimi, ['shimRefusal']);

  assert.match(kimi.shimRefusal({ pin: false }), /KIMI_CODE_NO_AUTO_UPDATE/u);
  assert.match(kimi.shimRefusal({ profile: '' }), /USERPROFILE|OS-home|redirect/iu);
  assert.equal(kimi.shimRefusal({ pin: true }), '');
  assert.equal(kimi.shimRefusal({ profile: 'C:\\x' }), '');
  assert.equal(kimi.shimRefusal({}), '');
});

test('the built shim parses the probe\'s argv and refuses at the hold, before any agent could start (a dry run: no kimi, no token)', { skip: !WINDOWS ? 'the hold is Windows-only (D18)' : !existsSync(SHIM) && 'the shim is not built' }, () => {
  const root = owned.ownedRoot(join(base, 'dry')).root;
  const locks = join(base, 'dry-locks');
  const work = join(base, 'dry-work');
  for (const dir of [locks, work]) {
    mkdirSync(dir, { recursive: true });
  }
  const args = kimi.shimArgs({ cwd: work, model: 'kimi-code/k3-256k', deadlineSeconds: 20, executable: join(base, 'no-such-kimi', 'kimi.exe'), root, personsBin: join(base, 'bin'), locks });
  const run = spawnSync(SHIM, args, { input: 'Reply OK', encoding: 'utf8', timeout: 60_000, env: owned.shimEnv({ root, personsBin: join(base, 'bin') }) });
  const report = JSON.parse(run.stdout.trim().split(/\r?\n/u).find((line) => line.startsWith('{')) ?? '{}');

  assert.equal(report.end, 'not_started', `${run.stdout}${run.stderr}`);
  assert.match(report.reason, /credentials junction/u, 'the shim did not reach the sign-in hold — a flag was not read');
  assert.ok(report.reason.includes(root.split(sep).join('\\')) || report.reason.includes(root), `the shim names another root than the one it was given: ${report.reason}`);
});
