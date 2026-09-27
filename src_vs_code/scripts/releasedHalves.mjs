/**
 * The RELEASED halves a person may still be running, obtained the way they got them — and this
 * checkout's own build beside them — for the scripts that measure one against the other.
 *
 * <p>Its own module because two checks need the same three things: the newest published `coai-mcp`
 * (downloaded with `gh`, never a draft), the newest published extension (BUILT from its tag, because
 * the `.vsix` ships one minified bundle whose functions cannot be called one by one), and a way to
 * run a one-shot mode with a deadline. `live-feature-schema-compat.mjs` (S2.1) had the first and the
 * third privately; `live-feature-vendor-compat.mjs` (S3.3) needs all three, and a second copy of a
 * downloader is a second place for "published releases only" to be forgotten.</p>
 *
 * <p>Every child here has a deadline, for the reason `live-close-consult-compat.mjs` gives: a check
 * whose failure mode is silence is a check nobody trusts the green of.</p>
 */
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, symlinkSync } from 'node:fs';
import path from 'node:path';

export const NETWORK_MS = 180_000;
export const LOCAL_MS = 60_000;
/** A compile of the whole extension, which on this machine is tens of seconds and not minutes. */
const BUILD_MS = 300_000;

export const REPO = 'oleksandrdubyna88/dew_flow_connect_other_ais';
/** This checkout's root. */
export const HERE = path.join(import.meta.dirname, '..', '..');

/** `--name=value` from the command line, or the fallback. */
export const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

/** A one-shot mode, or a serve that ends on EOF: stdout is the answer, the exit code is what happened. */
export function run(exe, args, dataDir, extraEnv = {}) {
  return new Promise((resolve) => {
    const child = spawn(exe, args, { shell: false, env: { ...process.env, COAI_DATA_DIR: dataDir, ...extraEnv } });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => {
      err += `\n(no answer within ${LOCAL_MS / 1000}s — killed)`;
      child.kill('SIGKILL');
    }, LOCAL_MS);
    deadline.unref();
    child.stdin.end();
    child.stdout.on('data', (chunk) => { out += String(chunk); });
    child.stderr.on('data', (chunk) => { err += String(chunk); });
    child.on('error', (reason) => { clearTimeout(deadline); resolve({ code: -1, out, err: String(reason) }); });
    child.on('close', (code) => { clearTimeout(deadline); resolve({ code: code ?? -1, out, err }); });
  });
}

/** This checkout's freshly built server, or an empty string. */
export function newServer() {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const built = path.join(HERE, 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);

  return existsSync(built) ? built : '';
}

/**
 * The newest PUBLISHED tag of one release line — `mcp-v`, `extension-v` — or an empty string.
 *
 * <p>Published only: a DRAFT is somebody's release in progress, with no assets a person could have
 * installed — measured 2026-09-26, when the newest tag was a draft carrying three of its six
 * platforms.</p>
 */
export function newestPublished(prefix) {
  const listed = spawnSync('gh', ['release', 'list', '--repo', REPO, '--limit', '40', '--exclude-drafts', '--json', 'tagName'], {
    encoding: 'utf8', shell: false, timeout: NETWORK_MS,
  });
  if (listed.status !== 0) {
    return '';
  }

  return JSON.parse(listed.stdout).map((one) => one.tagName).find((one) => one.startsWith(prefix)) ?? '';
}

/**
 * The released server, downloaded once — `COAI_OLD_SERVER` when it names a binary, else the tag asked
 * for, else the newest published `mcp-v*`.
 *
 * <p>The newest, because the release a person has not updated from is the newest one until the change
 * under test ships; once it has, pass the one before it.</p>
 */
export function releasedServer(into, tag = '') {
  if (process.env['COAI_OLD_SERVER'] !== undefined && existsSync(process.env['COAI_OLD_SERVER'])) {
    return process.env['COAI_OLD_SERVER'];
  }
  const wanted = tag === '' ? newestPublished('mcp-v') : tag;
  if (wanted === '') {
    return '';
  }
  const asset = process.platform === 'win32'
    ? `*win-${process.arch === 'arm64' ? 'arm64' : 'x64'}.zip`
    : `*${process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch === 'arm64' ? 'arm64' : 'x64'}.tar.gz`;
  const got = spawnSync('gh', ['release', 'download', wanted, '--repo', REPO, '--pattern', asset, '--dir', into], {
    encoding: 'utf8', shell: false, timeout: NETWORK_MS,
  });
  if (got.status !== 0) {
    console.log(`could not download ${wanted}: ${got.stderr}`);

    return '';
  }
  const archive = readdirSync(into).find((one) => one.endsWith('.zip') || one.endsWith('.tar.gz'));
  if (archive === undefined) {
    return '';
  }
  const unpacked = path.join(into, 'old');
  mkdirSync(unpacked, { recursive: true });
  const opened = unpack(path.join(into, archive), unpacked);
  if (opened.status !== 0) {
    console.log(`could not unpack ${archive}: ${opened.stderr}`);

    return '';
  }
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const found = readdirSync(unpacked, { recursive: true }).find((one) => String(one).endsWith(exe));
  console.log(`old server: ${wanted}`);

  return found === undefined ? '' : path.join(unpacked, String(found));
}

/** bsdtar by its absolute path on Windows — `tar` is two different programs there (see live-close-consult-compat.mjs). */
function unpack(archive, into) {
  const bsdtar = process.platform === 'win32'
    ? path.join(process.env['SystemRoot'] ?? 'C:\\Windows', 'System32', 'tar.exe')
    : 'tar';
  if (process.platform !== 'win32' || existsSync(bsdtar)) {
    return spawnSync(bsdtar, ['-xf', archive, '-C', into], { encoding: 'utf8', timeout: NETWORK_MS });
  }

  return spawnSync(
    'powershell',
    ['-NoProfile', '-NonInteractive', '-Command',
      `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${into}' -Force`],
    { encoding: 'utf8', timeout: NETWORK_MS },
  );
}

/** A git command in `cwd`, bounded; the result carries the exit code and both streams. */
function git(cwd, args) {
  return spawnSync('git', args, { cwd, encoding: 'utf8', shell: false, timeout: NETWORK_MS });
}

/**
 * The released EXTENSION, built from its tag exactly as its release job built it — or an empty string.
 *
 * <p><b>Built, not downloaded.</b> The `.vsix` carries one minified bundle that exports `activate`, so
 * `vendorsFrom`, `parseSession` or `snippetStatus` of the released build cannot be called from it. The
 * tag's source compiled by its own `tsconfig.json` is the same code without the minifier; the rounds-log
 * page, the one place minification has ever changed behaviour here, is bundled again by the caller.</p>
 *
 * <p><b>A local clone, not a worktree.</b> `prepare-gate.mjs` runs the conventions resolver's `check`,
 * which needs a real repository with its submodule at the pin; a `git clone --shared` gives it one
 * without registering a worktree other sessions would see. The submodule is cloned from THIS checkout's
 * mount, so no network is used when the pin is already here, and dependencies are linked from this
 * checkout when the lock files are byte-identical (installed otherwise).</p>
 */
export function releasedExtension(into, tag = '') {
  const wanted = tag === '' ? newestPublished('extension-v') : tag;
  if (wanted === '') {
    return undefined;
  }
  const root = path.join(into, 'ext');
  const steps = [
    () => git(into, ['clone', '-q', '--shared', '--no-checkout', HERE, root]),
    () => git(root, ['checkout', '-q', '--detach', wanted]),
    () => git(root, ['config', 'submodule..claude/rules/shared.url', path.join(HERE, '.agents', 'conventions')]),
    () => git(root, ['-c', 'protocol.file.allow=always', 'submodule', 'update', '--init', '-q', '.agents/conventions']),
  ];
  for (const step of steps) {
    const done = step();
    if (done.status !== 0) {
      console.log(`could not check out ${wanted}: ${done.stderr}`);

      return undefined;
    }
  }
  const linked = [
    dependencies(path.join(HERE, '.agents', 'conventions'), path.join(root, '.agents', 'conventions')),
    dependencies(path.join(HERE, 'src_vs_code'), path.join(root, 'src_vs_code')),
  ];
  if (linked.some((ok) => !ok)) {
    return undefined;
  }
  const extension = path.join(root, 'src_vs_code');
  const prepared = spawnSync(process.execPath, ['scripts/prepare-gate.mjs'], { cwd: extension, encoding: 'utf8', timeout: BUILD_MS });
  const compiled = prepared.status === 0
    ? spawnSync(process.execPath, [path.join('node_modules', 'typescript', 'bin', 'tsc'), '-p', './'], { cwd: extension, encoding: 'utf8', timeout: BUILD_MS })
    : prepared;
  if (compiled.status !== 0) {
    console.log(`could not build ${wanted}: ${compiled.stdout}${compiled.stderr}`);

    return undefined;
  }
  console.log(`old extension: ${wanted}`);

  return { tag: wanted, root: extension };
}

/**
 * `node_modules` for a released tree: a link to this checkout's when the two lock files are the same
 * bytes, a clean install otherwise — never a link to dependencies the release did not pin.
 */
function dependencies(ours, theirs) {
  const lock = (dir) => (existsSync(path.join(dir, 'package-lock.json')) ? readFileSync(path.join(dir, 'package-lock.json'), 'utf8') : '');
  if (existsSync(path.join(ours, 'node_modules')) && lock(ours) === lock(theirs)) {
    symlinkSync(path.join(ours, 'node_modules'), path.join(theirs, 'node_modules'), 'junction');

    return true;
  }
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  const installed = spawnSync(npm, ['ci', '--ignore-scripts'], { cwd: theirs, encoding: 'utf8', timeout: BUILD_MS, shell: process.platform === 'win32' });
  if (installed.status !== 0) {
    console.log(`could not install the dependencies of ${theirs}: ${installed.stderr}`);
  }

  return installed.status === 0;
}
