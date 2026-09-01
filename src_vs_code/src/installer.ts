import * as vscode from 'vscode';
import { execFile } from 'node:child_process';
import * as crypto from 'node:crypto';
import {
  COAI_RIDS,
  CoaiRid,
  RELEASES_REPO,
  assetNameFor,
  binaryNameFor,
  entryPathIn,
  ridFor,
  newestServerTag,
  updateAvailable,
  versionFromTag,
} from './coaiInstall';

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

const STATE_KEY = 'coai.installedVersion';

export function binaryPath(storage: vscode.Uri, rid: CoaiRid): vscode.Uri {
  return vscode.Uri.joinPath(storage, binaryNameFor(rid));
}

export function installedVersion(state: vscode.Memento): string | undefined {
  return state.get<string>(STATE_KEY);
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
    await makeExecutable(target);
    await state.update(STATE_KEY, version);
    return target;
  } finally {
    await vscode.workspace.fs.delete(scratch, { recursive: true, useTrash: false });
  }
}

export async function updateIsAvailable(state: vscode.Memento): Promise<boolean> {
  try {
    return updateAvailable(installedVersion(state), await latestTag());
  } catch {
    return false; // an offline machine is not an update prompt
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
