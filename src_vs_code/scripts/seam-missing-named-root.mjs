/**
 * A NAMED data folder that is missing, across the seam (research/PLAN_names_and_a_missing_root.md, the plan round's
 * finding 1): the extension decides it is missing and creates nothing (research/PLAN_paths_per_side.md D3), and since
 * story 3 the real coai-mcp refuses the same folder by itself — a writer mode exits 74, a served tool is an MCP error
 * naming the folder. Two suites each agree with themselves; this leg is the live check that the two halves agree with
 * EACH OTHER over the real binary, for the same environment.
 *
 * <p>Three environments:</p>
 * <ul>
 *   <li>(a) a named root that does not exist — both refuse, and nothing is created;</li>
 *   <li>(b) a named root that exists, with a side whose folder does not — neither refuses, and the side folder is made
 *       lazily, as it always was;</li>
 *   <li>(c) no <code>COAI_DATA_DIR</code> at all, the default location — neither refuses.</li>
 * </ul>
 *
 * <p>The extension's half is its own decision — <code>namedRoot()</code> over this process's environment, the disk asked
 * through the page's bounded probes, then <code>dataDirFinding</code>. The server's half is the real binary twice:
 * <code>--check-model</code> with a row that cannot consult (so a server that is NOT refusing answers "unavailable" and
 * launches nothing), and a served <code>initialize</code> plus one <code>providers</code> call. A disagreement fails the
 * leg naming both answers.</p>
 *
 * <p><b>(c) and the real default folder.</b> On Windows the default location is <code>%LOCALAPPDATA%\coai-mcp</code>, which
 * .NET reads from the shell, not from the environment — so a served run would write its log, run marker and sweeps into
 * the developer's own data, and even <code>--check-model</code> reads that folder's settings file first. There the
 * server half is NOT run (the code round's own review): the C# tests <code>TheDefaultLocation_IsNeverRefused</code> and
 * <code>ServeRefuses_OnlyAConfirmedAbsence</code> hold that decision, and the extension's half is still asked. On a
 * POSIX host <code>XDG_DATA_HOME</code> moves the default into a temporary folder and both server halves run. In every
 * case the child gets an empty <code>COAI_CREDS_KEY</code> (no vault) and no inherited <code>COAI_DATA_SIDE</code>.</p>
 */
import { promises as fsp } from 'node:fs';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { namedRoot, dataDirFinding } = await import('../out/dataDir.js');
const { PathProbes, statProbe } = await import('../out/pathProbes.js');

/** A catalog row whose runtime cannot consult: a server that is not refusing answers "unavailable" and launches nothing. */
const ROW = JSON.stringify({ row: { id: 'seam-missing-root', runtime: 'remote' } });

/** The extension's own decision for this environment: does it call the named data root missing? */
async function extensionRefuses(env) {
  const was = { dir: process.env['COAI_DATA_DIR'], side: process.env['COAI_DATA_SIDE'] };
  const put = (name, value) => { if (value === undefined) { delete process.env[name]; } else { process.env[name] = value; } };
  put('COAI_DATA_DIR', env.COAI_DATA_DIR);
  put('COAI_DATA_SIDE', env.COAI_DATA_SIDE);
  try {
    const named = namedRoot();
    if (named === undefined) {
      return false;
    }
    const probes = new PathProbes(statProbe((path) => fsp.stat(path)));
    await probes.settled(named.root, 'folder');

    return dataDirFinding(named, probes.presence(named.root, 'folder'))?.state === 'missing';
  } finally {
    put('COAI_DATA_DIR', was.dir);
    put('COAI_DATA_SIDE', was.side);
  }
}

/** The server's `--check-model`: refused means exit 74 with the folder named on stderr. */
async function checkModelRefuses({ binary, env, root, timeoutMs }) {
  const { code, stdout, stderr } = await new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--check-model'], { env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => { child.kill('SIGKILL'); broke(new Error(`--check-model did not answer within ${timeoutMs} ms\n${err}`)); }, timeoutMs);
    child.stdout.on('data', (b) => { out += String(b); });
    child.stderr.on('data', (b) => { err += String(b); });
    child.on('error', (e) => { clearTimeout(deadline); broke(e); });
    child.on('close', (exit) => { clearTimeout(deadline); done({ code: exit, stdout: out, stderr: err }); });
    child.stdin.end(ROW);
  });
  const refused = code === 74 && root.length > 0 && stderr.includes(root);

  return { refused, said: `exit ${code}${refused ? `: ${stderr.trim()}` : `, stdout ${stdout.trim().slice(0, 160)}`}` };
}

/** The served server: refused means the `providers` call is an MCP error naming the folder. */
async function serveRefuses({ sessionsFor, binary, env, root, timeoutMs }) {
  const session = sessionsFor({ command: 'dotnet', args: [binary], dataDir: env.COAI_DATA_DIR ?? '', timeoutMs })(env);
  try {
    await session.ready;
    const reply = await session.call('providers', {});
    const text = reply?.result?.content?.[0]?.text ?? '';
    const refused = reply?.result?.isError === true && root.length > 0 && text.includes(root);

    return { refused, said: refused ? text : `answered (isError ${String(reply?.result?.isError ?? false)})` };
  } finally {
    await session.close();
  }
}

/**
 * @returns {Promise<{ refusedBoth: string, lazy: string[] }>} what the two halves said, for the summary line
 */
export async function missingNamedRootSeam({ binary, sessionsFor, timeoutMs, fail }) {
  const parent = mkdtempSync(join(tmpdir(), 'coai-seam-named-root-'));
  const leave = (why) => {
    rmSync(parent, { recursive: true, force: true });
    fail(`a missing named data folder: ${why}`);
  };
  const missing = join(parent, 'named-but-missing');
  const existing = join(parent, 'named-and-there');
  mkdirSync(existing);
  const xdg = join(parent, 'xdg');
  const posix = process.platform !== 'win32';
  // No vault in any case: a COAI_CREDS_KEY inherited from the developer's shell made every run call the real `creds
  // config` (the code round's own review); an empty key is "no vault" (KeyVault.ReadAsync). No inherited side either.
  const isolated = { COAI_CREDS_KEY: '', COAI_DATA_SIDE: '' };
  const cases = [
    { name: '(a) a named root that does not exist', env: { ...isolated, COAI_DATA_DIR: missing }, root: missing, refuses: true, serve: true, server: true },
    { name: '(b) a named root with a missing side folder', env: { ...isolated, COAI_DATA_DIR: existing, COAI_DATA_SIDE: 'seamside' }, root: existing, refuses: false, serve: true, server: true, lazy: join(existing, 'seamside') },
    { name: '(c) no COAI_DATA_DIR, the default location', env: { ...isolated, COAI_DATA_DIR: '', ...(posix ? { XDG_DATA_HOME: xdg } : {}) }, root: '', refuses: false, serve: posix, server: posix, lazy: posix ? join(xdg, 'coai-mcp') : '' },
  ];
  const lazy = [];
  for (const one of cases) {
    const extension = await extensionRefuses({ COAI_DATA_DIR: one.env.COAI_DATA_DIR || undefined, COAI_DATA_SIDE: one.env.COAI_DATA_SIDE || undefined });
    const check = one.server
      ? await checkModelRefuses({ binary, env: one.env, root: one.root, timeoutMs })
      : { refused: false, said: 'not asked (the default folder of this OS is the developer\'s own; TheDefaultLocation_IsNeverRefused holds it)' };
    const served = one.serve ? await serveRefuses({ sessionsFor, binary, env: one.env, root: one.root, timeoutMs }) : { refused: check.refused, said: 'not served (the default folder of this OS is the developer\'s own)' };
    if (extension !== one.refuses || check.refused !== one.refuses || served.refused !== one.refuses) {
      leave(`${one.name}: the halves disagree — the extension ${extension ? 'refuses' : 'does not refuse'}; --check-model ${check.said}; serve ${served.said}`);
    }
    if (one.refuses && existsSync(missing)) {
      leave(`${one.name}: both halves refused, and the folder was made anyway`);
    }
    if (one.serve && (one.lazy ?? '').length > 0) {
      if (!existsSync(one.lazy)) {
        leave(`${one.name}: the folder ${one.lazy} was not made by a server that was not refusing — it is no longer made lazily`);
      }
      lazy.push(one.lazy);
    }
  }
  rmSync(parent, { recursive: true, force: true });

  return { refusedBoth: missing, lazy };
}
