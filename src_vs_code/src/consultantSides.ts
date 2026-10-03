import { sideLabel } from './coaiInstall';
import { usableDirs, watchedDirs } from './escalationDirs';

/**
 * Which sides of this machine the Consultant tab shows, and what each one is called (E5.1 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`).
 *
 * <p><b>Why there is more than one.</b> A Claude Code or Codex session inside WSL spawns its own Linux `coai-mcp`,
 * with its own data directory and its own vendor CLIs — so a consultant can work on one side and not the other, and a
 * plain Windows window would otherwise know nothing about the WSL one. The server on each side writes its facts into
 * its own `consultations/health/`; a window reads its own, and the folders named in `coai.alsoWatchDataDirectories`
 * READ-ONLY, labelled with the side they belong to.</p>
 *
 * <p>Pure. The directory rules — which two spellings are one place, which paths cannot be reached from this host —
 * are `escalationDirs.ts`'s, the module that already answers them for the questions surface; a second copy here
 * would be the drift `reuse-first.md` names. A folder that rule refuses is left out: the MCP server tab already names
 * it with its reason.</p>
 */

/** One side the tab shows: this window's own store, or another installation's, read-only. */
export interface HealthSide {
  readonly kind: 'this' | 'other';
  readonly label: string;
  /** The data directory, as this host reaches it. */
  readonly dir: string;
}

/**
 * This window's side, named the way the MCP server tab names it (`sideLabel`: `WSL: Ubuntu`) — and, in a local
 * window, where that says nothing because there is only one side, by the operating system the host runs on.
 */
export function thisSideLabel(remoteName: string | undefined, distro: string, platform: string): string {
  const remote = sideLabel(remoteName, distro);
  if (remote.length > 0) {
    return remote;
  }

  return platform === 'win32' ? 'Windows' : platform === 'darwin' ? 'macOS' : 'Linux';
}

/** `\\wsl.localhost\<distro>\…` or `\\wsl$\<distro>\…`, either slash: a WSL store reached from Windows. */
const WSL_SHARE = /^[\\/]{2}wsl(?:\.localhost|\$)[\\/]([^\\/]+)/iu;

/** `/mnt/<drive>/…`: the Windows store reached from WSL. */
const WINDOWS_DRIVE = /^\/mnt\/[a-z](?:\/|$)/iu;

/**
 * Another installation's side, from the shape of the path a person named.
 *
 * <p>Only two shapes say which side a folder is: a WSL share names its distribution, and `/mnt/<drive>` is the
 * Windows disk. Anything else — a NAS, a second checkout — is named by its path, because calling it a side it might not
 * be would put a wrong word on every fact read from it.</p>
 */
export function otherSideLabel(path: string): string {
  const share = WSL_SHARE.exec(path);
  if (share !== null) {
    return `WSL: ${share[1]}`;
  }

  return WINDOWS_DRIVE.test(path) ? 'Windows' : `another installation at ${path}`;
}

/** This side first, then every usable folder named in the setting — each folder once, this one never twice. */
export function healthSides(own: { label: string; dir: string }, extras: readonly string[], platform: NodeJS.Platform): readonly HealthSide[] {
  const [, ...others] = usableDirs(watchedDirs(own.dir, extras, platform));

  return [
    { kind: 'this', label: own.label, dir: own.dir },
    ...others.map((dir): HealthSide => ({ kind: 'other', label: otherSideLabel(dir), dir })),
  ];
}
