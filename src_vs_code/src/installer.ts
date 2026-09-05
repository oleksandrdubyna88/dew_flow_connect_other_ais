import * as vscode from 'vscode';
import { execFile } from 'node:child_process';
import * as crypto from 'node:crypto';
import { hostname } from 'node:os';
import {
  COAI_RIDS,
  CoaiRid,
  RELEASES_REPO,
  ServerStatus,
  Side,
  assetNameFor,
  binaryNameFor,
  entryPathIn,
  installedKey,
  ridFor,
  newestServerTag,
  serverStatus,
  versionFromTag,
} from './coaiInstall';
import { askVersion } from './versionProbe';

/**
 * Putting the published `coai-mcp` on this machine. The decisions are next door in
 * `coaiInstall.ts` (pure, tested); this half touches the network, the disk and a person.
 *
 * <p><b>The download is verified.</b> Every release publishes a `.sha256` beside each asset;
 * a mismatch refuses. A release without one is reported out loud rather than skipped silently —
 * a quiet skip is indistinguishable from a check that passed.</p>
 *
 * <p><b>Extraction runs `tar`.</b> Node ships gzip but no zip reader, and this extension keeps
 * zero runtime dependencies. Windows 10+ ships bsdtar as `tar.exe`, which reads a zip; the
 * alternative was hand-writing a zip reader to save one process launch that happens once.</p>
 */

export function binaryPath(storage: vscode.Uri, rid: CoaiRid): vscode.Uri {
  return vscode.Uri.joinPath(storage, binaryNameFor(rid));
}

/**
 * The binary this SIDE of the machine installs to, or nothing when the matrix has no build for it.
 *
 * <p>`globalStorageUri` is a path on the extension host that is running — the Windows profile
 * directory in a local window, `~/.vscode-server/…` in a WSL one. That is why every question here
 * is a question about one side and never about "this machine".</p>
 */
export function serverPath(storage: vscode.Uri): vscode.Uri | undefined {
  const rid = ridFor(process.platform, process.arch);

  return rid === undefined ? undefined : binaryPath(storage, rid);
}

/**
 * This side of the machine, as the running extension host can describe it.
 *
 * <p>`remoteAuthority` would have been one field instead of three, and it is not public API — so
 * the three that ARE public are folded together. See {@link installedKey}.</p>
 */
function thisSide(storage: vscode.Uri): Side {
  return {
    remoteName: vscode.env.remoteName,
    distro: process.env['WSL_DISTRO_NAME'],
    hostname: hostname(),
    storagePath: storage.fsPath,
  };
}

/** What this side's record says it installed, or nothing. A fallback — never the truth. */
export function installedVersion(state: vscode.Memento, storage: vscode.Uri): string | undefined {
  return state.get<string>(installedKey(thisSide(storage)));
}

/**
 * What is on this side's disk, asked of the disk and then of the binary.
 *
 * <p><b>`stat` runs every time; only the PROBE is cached.</b> A cached `stat` would keep claiming a
 * file somebody deleted, and the probe is the expensive half — a process launch, which a panel that
 * repaints on every keystroke must not do per render.</p>
 *
 * <p><b>The cache stores failures too.</b> Every release up to 0.12.2 exits 64 on `--version`, so
 * caching only successes would re-spawn a doomed process on every five-second watcher tick for
 * exactly the binaries that need updating. Raised by Gemini in this plan's round.</p>
 */
export async function serverOnThisSide(
  storage: vscode.Uri,
  state: vscode.Memento,
  published: string,
): Promise<ServerStatus> {
  const remembered = installedVersion(state, storage) ?? '';
  const target = serverPath(storage);
  if (target === undefined) {
    return serverStatus({ fileExists: false, reported: '', remembered, published });
  }

  let stat: vscode.FileStat;
  try {
    stat = await vscode.workspace.fs.stat(target);
  } catch {
    return serverStatus({ fileExists: false, reported: '', remembered, published });
  }

  return serverStatus({
    fileExists: true,
    reported: await reportedVersion(target, stat),
    remembered,
    published,
  });
}

/**
 * What the probe answered for a file, keyed by path — and what it is answering right now.
 *
 * <p>Both halves came out of this change's gate. A single slot was clobbered by any second path
 * (a test, or a host that asks about more than one), and without the in-flight promise every render
 * started during the 8-second timeout of a HANGING binary launched a process of its own: ten
 * keystrokes, ten waits. Callers now join the one probe in flight.</p>
 */
const probed = new Map<string, { mtime: number; size: number; version: string }>();
const probing = new Map<string, Promise<string>>();

async function reportedVersion(target: vscode.Uri, stat: vscode.FileStat): Promise<string> {
  const path = target.fsPath;
  const cached = probed.get(path);
  if (cached?.mtime === stat.mtime && cached.size === stat.size) {
    return cached.version;
  }

  const running = probing.get(path);
  if (running !== undefined) {
    return running;
  }

  const attempt = askVersion(path)
    .then((version) => {
      // The FAILURE is stored too: a pre-0.12.3 binary must be asked once, not on every tick.
      probed.set(path, { mtime: stat.mtime, size: stat.size, version });
      return version;
    })
    .finally(() => probing.delete(path));
  probing.set(path, attempt);

  return attempt;
}

/**
 * Whether this side has the binary at all — the disk, and nothing else.
 *
 * <p>Separate from {@link serverOnThisSide} because the config block only needs to know whether the
 * path it hands out exists, and going through the full status would launch a `--version` process to
 * answer a question `stat` had already answered. (gemini, this change's gate.)</p>
 */
export async function serverExists(storage: vscode.Uri): Promise<boolean> {
  const target = serverPath(storage);
  if (target === undefined) {
    return false;
  }
  try {
    await vscode.workspace.fs.stat(target);
    return true;
  } catch {
    return false;
  }
}

export async function installLatest(
  storage: vscode.Uri,
  state: vscode.Memento,
): Promise<vscode.Uri> {
  const rid = ridFor(process.platform, process.arch);
  if (rid === undefined) {
    throw new Error(
      // The list is BUILT from the RIDs rather than typed, so the sentence cannot name a matrix
      // that has moved on — which is exactly what it did the day macOS was added.
      `there is no published build for ${process.platform}/${process.arch} — ` +
        `the release matrix builds ${COAI_RIDS.join(', ')}.`,
    );
  }

  const tag = await latestTag();
  const version = versionFromTag(tag);
  if (version === undefined) {
    throw new Error(`the latest release is tagged '${tag}', which is not this product's line`);
  }

  await vscode.workspace.fs.createDirectory(storage);
  const scratch = vscode.Uri.joinPath(storage, `.download-${Date.now()}`);
  await vscode.workspace.fs.createDirectory(scratch);
  try {
    const asset = assetNameFor(rid, version);
    const base = `https://github.com/${RELEASES_REPO}/releases/download/${tag}`;
    const archive = vscode.Uri.joinPath(scratch, asset);
    const bytes = await download(`${base}/${asset}`);
    await verify(bytes, `${base}/${asset}.sha256`, asset);
    await vscode.workspace.fs.writeFile(archive, bytes);

    await extract(archive, scratch);
    const extracted = vscode.Uri.joinPath(scratch, ...entryPathIn(rid, version).split('/'));
    const target = binaryPath(storage, rid);
    await vscode.workspace.fs.copy(extracted, target, { overwrite: true });
    // And whatever else the archive carried, beside it. The server is Native AOT and still
    // dlopens SQLite at run time, through the OS loader, which searches the directory the
    // executable sits in — so a copy of the binary alone is a server that cannot open its own
    // database, and the failure is silent because that write is best-effort. Copying the archive's
    // contents rather than a named file means the next native dependency needs no change here.
    await copyCompanions(vscode.Uri.joinPath(scratch, entryPathIn(rid, version).split('/')[0]!), storage, rid);
    await makeExecutable(target);
    await state.update(installedKey(thisSide(storage)), version);
    // The new file has a new mtime, so the cache would miss anyway — but saying so beats relying on
    // a filesystem's timestamp resolution for correctness.
    probed.delete(target.fsPath);
    return target;
  } finally {
    await vscode.workspace.fs.delete(scratch, { recursive: true, useTrash: false });
  }
}

/**
 * The newest published SERVER version, or undefined when GitHub cannot be reached.
 *
 * <p>The list endpoint, not <c>/releases/latest</c>: that one answers with the newest release of
 * ANY tag shape, and this repository publishes extension releases too. An extension release was
 * therefore answering "is there a newer server" with a tag that is not a server version, so the
 * update check concluded no — every time, silently, since the day the extension line started.</p>
 */
export async function latestServerVersion(): Promise<string | undefined> {
  try {
    return versionFromTag(await latestTag());
  } catch {
    return undefined; // an offline machine is not an error worth showing
  }
}

async function latestTag(): Promise<string> {
  const response = await fetch(`https://api.github.com/repos/${RELEASES_REPO}/releases?per_page=30`, {
    headers: { accept: 'application/vnd.github+json' },
  });
  if (!response.ok) {
    throw new Error(`GitHub answered ${response.status} for the release list`);
  }
  const body = (await response.json()) as { tag_name?: string }[];
  const tags = Array.isArray(body)
    ? body.map((r) => r.tag_name).filter((t): t is string => typeof t === 'string')
    : [];
  const newest = newestServerTag(tags);
  if (newest === undefined) {
    throw new Error('no server release is published yet');
  }
  return newest;
}

async function download(url: string): Promise<Uint8Array> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`downloading ${url} answered ${response.status}`);
  }
  return new Uint8Array(await response.arrayBuffer());
}

async function verify(bytes: Uint8Array, sumUrl: string, asset: string): Promise<void> {
  const response = await fetch(sumUrl);
  if (!response.ok) {
    throw new Error(
      `${asset} has no published checksum (${response.status}) — refusing to install an ` +
        'unverified binary. A release cut before checksums existed needs a newer one.',
    );
  }
  const published = (await response.text()).trim().split(/\s+/)[0]?.toLowerCase();
  const actual = crypto.createHash('sha256').update(bytes).digest('hex');
  if (published !== actual) {
    throw new Error(`${asset} failed its checksum — expected ${published}, got ${actual}`);
  }
}

/**
 * Every other file the archive brought, put beside the binary.
 *
 * <p>The binary itself is copied by name because that name is what the panel launches; everything
 * else is copied because the archive would not carry it otherwise. Today that is one native SQLite
 * library per platform (`e_sqlite3.dll`, `libe_sqlite3.so`, `libe_sqlite3.dylib`).</p>
 *
 * <p>A file that cannot be copied is not fatal: the binary is already in place and a missing
 * companion is a feature that degrades, not a server that will not start.</p>
 */
async function copyCompanions(from: vscode.Uri, storage: vscode.Uri, rid: CoaiRid): Promise<void> {
  const binary = binaryNameFor(rid);
  for (const [name, kind] of await vscode.workspace.fs.readDirectory(from)) {
    if (kind !== vscode.FileType.File || name === binary) {
      continue;
    }
    try {
      await vscode.workspace.fs.copy(
        vscode.Uri.joinPath(from, name),
        vscode.Uri.joinPath(storage, name),
        { overwrite: true });
    } catch {
      // Nothing to do about it here, and the server still starts.
    }
  }
}

function extract(archive: vscode.Uri, into: vscode.Uri): Promise<void> {
  return new Promise((resolve, reject) => {
    execFile('tar', ['-xf', archive.fsPath, '-C', into.fsPath], (error) =>
      error ? reject(new Error(`extracting the archive failed: ${error.message}`)) : resolve(),
    );
  });
}

async function makeExecutable(target: vscode.Uri): Promise<void> {
  if (process.platform === 'win32') {
    return;
  }
  await new Promise<void>((resolve, reject) => {
    execFile('chmod', ['+x', target.fsPath], (error) =>
      error ? reject(new Error(`chmod +x failed: ${error.message}`)) : resolve(),
    );
  });
}
