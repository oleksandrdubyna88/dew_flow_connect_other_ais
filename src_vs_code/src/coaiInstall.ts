/**
 * Deciding what "Install the MCP Server…" downloads, and where it goes — the decisions ported
 * from creds' `credsInstall.ts`, because every one of them was learned there:
 *
 * - Into the extension's own storage, never onto the `PATH`: uninstall is a file delete, and the
 *   full path is what the config block hands out anyway.
 * - The installed version is REMEMBERED, because the binary cannot be asked (`coai-mcp` answers
 *   `--help`, not `--version`) — what is on disk is paired with what we recorded putting it there.
 * - macOS is honestly absent: the release matrix builds four RIDs and no `osx-*`; a guessed
 *   nearby RID downloads a binary that cannot execute and reports a network problem instead.
 *
 * Pure and `vscode`-free, so the shape is a unit test rather than a half-working download on
 * somebody's laptop.
 */

export type CoaiRid = 'win-x64' | 'win-arm64' | 'linux-x64' | 'linux-arm64';

/** The release line: tags `mcp-v0.1.0`, assets `coai-mcp-<version>-<rid>.(zip|tar.gz)`. */
export const TAG_PREFIX = 'mcp-v';

export const BINARY = 'coai-mcp';

export const RELEASES_REPO = 'oleksandrdubyna88/dew_flow_connect_other_ais';

/** The build for this machine, or `undefined` when the release matrix has none (macOS). */
export function ridFor(platform: string, arch: string): CoaiRid | undefined {
  const os = platform === 'win32' ? 'win' : platform === 'linux' ? 'linux' : undefined;
  const cpu = arch === 'x64' || arch === 'arm64' ? arch : undefined;
  return os === undefined || cpu === undefined ? undefined : (`${os}-${cpu}` as CoaiRid);
}

/** The archive the release carries for one build — the name the workflow packages. */
export function assetNameFor(rid: CoaiRid, version: string): string {
  return `${BINARY}-${version}-${rid}${rid.startsWith('win-') ? '.zip' : '.tar.gz'}`;
}

/** The file inside that archive: the workflow puts the binary in a directory of the same name. */
export function entryPathIn(rid: CoaiRid, version: string): string {
  return `${BINARY}-${version}-${rid}/${binaryNameFor(rid)}`;
}

/** What the installed binary is called once it is in place. */
export function binaryNameFor(rid: CoaiRid): string {
  return rid.startsWith('win-') ? `${BINARY}.exe` : BINARY;
}

/** `mcp-v0.1.0` → `0.1.0`; any other tag line yields nothing rather than a wrong version. */
export function versionFromTag(tag: string): string | undefined {
  return tag.startsWith(TAG_PREFIX) && tag.length > TAG_PREFIX.length
    ? tag.slice(TAG_PREFIX.length)
    : undefined;
}

/** Newer / same / older by numeric parts; a malformed part compares as 0 rather than throwing. */
export function compareVersions(a: string, b: string): number {
  const pa = a.split('.').map((p) => Number.parseInt(p, 10) || 0);
  const pb = b.split('.').map((p) => Number.parseInt(p, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff !== 0) {
      return diff;
    }
  }
  return 0;
}

/** Whether the published tag is an update over what this machine recorded installing. */
export function updateAvailable(installed: string | undefined, publishedTag: string): boolean {
  const published = versionFromTag(publishedTag);
  if (published === undefined) {
    return false;
  }
  return installed === undefined || compareVersions(published, installed) > 0;
}
