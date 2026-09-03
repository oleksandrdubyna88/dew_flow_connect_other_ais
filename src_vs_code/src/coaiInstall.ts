/**
 * Deciding what "Install the MCP Server…" downloads, and where it goes — the decisions ported
 * from creds' `credsInstall.ts`, because every one of them was learned there:
 *
 * - Into the extension's own storage, never onto the `PATH`: uninstall is a file delete, and the
 *   full path is what the config block hands out anyway.
 * - The installed version is REMEMBERED, because the binary cannot be asked (`coai-mcp` answers
 *   `--help`, not `--version`) — what is on disk is paired with what we recorded putting it there.
 * - A platform the matrix does not build is refused BY NAME rather than guessed at: a nearby RID
 *   downloads a binary that cannot execute and then reports a network problem instead.
 *
 * Pure and `vscode`-free, so the shape is a unit test rather than a half-working download on
 * somebody's laptop.
 */

export type CoaiRid = 'win-x64' | 'win-arm64' | 'linux-x64' | 'linux-arm64' | 'osx-x64' | 'osx-arm64';

/**
 * Every build the release publishes, in one list.
 *
 * <p>It exists so the "no build for your platform" message cannot name a matrix that no longer
 * matches the workflow: the sentence is BUILT from this, and this is what `ridFor` maps onto.</p>
 */
export const COAI_RIDS: readonly CoaiRid[] = [
  'win-x64',
  'win-arm64',
  'linux-x64',
  'linux-arm64',
  'osx-x64',
  'osx-arm64',
];

/** The release line: tags `mcp-v0.1.0`, assets `coai-mcp-<version>-<rid>.(zip|tar.gz)`. */
export const TAG_PREFIX = 'mcp-v';

export const BINARY = 'coai-mcp';

export const RELEASES_REPO = 'oleksandrdubyna88/dew_flow_connect_other_ais';

/** The build for this machine, or `undefined` when the release matrix has none. */
export function ridFor(platform: string, arch: string): CoaiRid | undefined {
  // `darwin` is what node calls macOS and `osx` is what .NET calls it; the mapping is the whole
  // reason a Mac was told there was no build for it while the runtime had supported one all along.
  const os =
    platform === 'win32' ? 'win' : platform === 'linux' ? 'linux' : platform === 'darwin' ? 'osx' : undefined;
  const cpu = arch === 'x64' || arch === 'arm64' ? arch : undefined;
  if (os === undefined || cpu === undefined) {
    return undefined;
  }
  const rid = `${os}-${cpu}` as CoaiRid;
  return COAI_RIDS.includes(rid) ? rid : undefined;
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

/**
 * The newest SERVER tag out of a release list.
 *
 * <p>This repository publishes two tag shapes — <c>mcp-v*</c> and <c>extension-v*</c> — and the
 * update check used to ask GitHub for "the latest release", which is the newest of EITHER. An
 * extension release therefore answered the question "is there a newer server", produced a tag
 * that is not a server version, and the check quietly concluded no. It never fired once.</p>
 */
export function newestServerTag(tags: readonly string[]): string | undefined {
  return tags
    .filter((t) => versionFromTag(t) !== undefined)
    .sort((a, b) => compareVersions(versionFromTag(b)!, versionFromTag(a)!))[0];
}

/**
 * What to DO about an install that failed, when the failure is one we recognise.
 *
 * <p>Overwriting a binary that is RUNNING is refused by Windows, and an MCP client holding
 * `coai-mcp.exe` open is the normal case at the exact moment somebody presses Update — the client
 * was started by the server they are updating. The raw error is an errno; what a person needs is
 * the sentence naming which program to quit.</p>
 *
 * <p>Empty for anything else: dressing an unrelated failure up as a lock would send the reader to
 * close a program that was never the problem.</p>
 */
export function installFailureHint(message: string, code = ''): string {
  if (!onTheBinary(message)) {
    return '';
  }

  if (SHARING_VIOLATION.includes(code) || message.includes('used by another process')) {
    return `${binaryNameFor('win-x64')} is in use — quit or restart the MCP client that is running it (it holds the file open), then press Update again.`;
  }

  return ACCESS_DENIED.includes(code)
    ? `${binaryNameFor('win-x64')} could not be replaced: writing to it was denied. If an MCP client is running it, quit that client; otherwise check the file's permissions — it may be read-only.`
    : '';
}

/**
 * Unambiguous: something HAS the file open. Nothing else produces these on a copy.
 *
 * <p>Windows' own sentence is listed with them at the call site because it is the one case that
 * carries no machine-readable code — it arrives inside the message of a wrapped error.</p>
 */
const SHARING_VIOLATION = ['EBUSY', 'ETXTBSY', 'Unavailable'];

/**
 * Ambiguous, and the reason these are separated at all.
 *
 * <p>Overwriting a running executable on Windows can surface as `EPERM`, and so can a read-only
 * attribute or an ACL on the same file with no process holding it. Both reviewers named the cost
 * of collapsing the two: somebody is sent to close a program that was never the problem, retries
 * fail with the same confident sentence, and the real cause — a permission on disk — is hidden by
 * the message that was supposed to explain it. So this branch names BOTH possibilities instead of
 * asserting one.</p>
 */
const ACCESS_DENIED = ['EPERM', 'EACCES', 'NoPermissions'];

/** The hint is only ever about the target binary; a failure on the scratch directory is not this. */
function onTheBinary(message: string): boolean {
  return message.includes(binaryNameFor('win-x64')) || message.includes(BINARY);
}

/** What an extension host knows about the side of a machine it is running on. */
export interface Side {
  /** `vscode.env.remoteName` — `wsl`, `ssh-remote`, … or nothing in a local window. */
  readonly remoteName?: string | undefined;
  /** `WSL_DISTRO_NAME`, which the subsystem sets inside every distro. */
  readonly distro?: string | undefined;
  /** `os.hostname()` — the discriminator for remotes that are not WSL. */
  readonly hostname?: string | undefined;
  /** `context.globalStorageUri.fsPath`: the directory the binary is actually written to. */
  readonly storagePath: string;
}

/**
 * Which SIDE of a machine a remembered version belongs to.
 *
 * <p><b>This is the whole defect this key shape exists to fix.</b> `globalState` is the CLIENT's
 * storage — one database, shared by every window of a profile, remote windows included — while
 * `globalStorageUri`, where the binary is written, is a path on the extension host that is running.
 * So on a machine with a Windows window and a WSL window there is one record and two disks:
 * measured 2026-09-03, the WSL side ran 0.12.1 while the panel there read the record a Windows
 * press had left at 0.12.2, said "you are up to date", and hid the only button that could have
 * fixed it.</p>
 *
 * <p><b>Three ingredients, and each is here because the ones before it are not enough.</b> The
 * plan's round said to key this on `vscode.env.remoteAuthority` — right about the collision it was
 * worried about, wrong about the cure, because that property is not in the public API. The storage
 * path alone is not enough either: two WSL distros with the same user name mount the same
 * `/home/<user>/.vscode-server/…`, which is exactly the two-distro collision the finding named. So
 * the remote KIND, the distro (or the hostname, for remotes that have no distro) and the storage
 * path are folded together.</p>
 *
 * <p>A local window uses the storage path alone: it is this machine, and folding a hostname in
 * would discard the record every time the machine is renamed.</p>
 *
 * <p>Case and separators are folded, so one directory cannot produce two keys on a filesystem that
 * reports either.</p>
 */
export function installedKey(side: Side): string {
  const remote = (side.remoteName ?? '').trim();
  const target = ((side.distro ?? '').trim().length > 0 ? side.distro : side.hostname) ?? '';
  const parts = remote.length === 0 ? ['local', side.storagePath] : [remote, target, side.storagePath];
  const slug = slugify(parts.join('|'));

  return `coai.installedVersion@${slug.length === 0 ? 'unknown-side' : slug}`;
}

function slugify(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/**
 * The side, named the way VS Code's own remote indicator names it — empty for a local window.
 *
 * <p>Empty is deliberate: somebody on a plain Windows machine has one side and does not need to
 * read the word for it. A remote kind this build does not recognise is printed VERBATIM rather than
 * prettified into something that might be wrong.</p>
 *
 * <p>The distro comes from `WSL_DISTRO_NAME`, which the subsystem sets inside every distro — the
 * one cheap way to say `WSL: Ubuntu` rather than `WSL`, since `remoteName` is only ever `wsl`.</p>
 */
export function sideLabel(remoteName: string | undefined, distro: string | undefined = ''): string {
  const kind = (remoteName ?? '').trim();
  const target = (distro ?? '').trim();
  if (kind.length === 0) {
    return '';
  }
  if (kind === 'wsl') {
    return target.length === 0 ? 'WSL' : `WSL: ${target}`;
  }

  return kind === 'ssh-remote' ? 'SSH' : kind;
}

/** What the panel can say about the server binary on the side it is running on. */
export type ServerStatusKind =
  /** No file at the path this side installs to. Whatever any record says. */
  | 'absent'
  /** A file, and a version for it. */
  | 'known'
  /** A file that cannot state its version — every release up to 0.12.2 exits 64 on `--version`. */
  | 'unknown';

export interface ServerStatus {
  readonly kind: ServerStatusKind;
  /** Empty unless `kind` is `known`. */
  readonly version: string;
  /** The version came from the RECORD rather than from the binary. */
  readonly remembered: boolean;
  /** Whether there is something newer to install over what is there. */
  readonly updateOffered: boolean;
}

export interface ServerFacts {
  /** `stat` said there is a file at this side's install path. */
  readonly fileExists: boolean;
  /** What `coai-mcp --version` printed, or empty when it could not be asked. */
  readonly reported: string;
  /** What this SIDE's record says, or empty. A fallback, never the truth. */
  readonly remembered: string;
  /** The newest published version, or empty when GitHub could not be read. */
  readonly published: string;
}

/**
 * What the Server section states, from what is actually on this side's disk.
 *
 * <p><b>Ordering, and why it is this way round.</b> The binary's own answer wins, because it is the
 * only source that cannot belong to another machine. The record is consulted ONLY when the file is
 * there but could not be asked — a binary the OS refuses to spawn, which Smart App Control does to
 * a freshly written executable — and it is marked as remembered when it is used. A file with no
 * answer and no record is `unknown`, which offers an update rather than claiming currency.</p>
 *
 * <p><b>`absent` never offers an update</b>, and that is not cosmetic: `offerUpdate` runs at
 * activation, so a fresh machine would otherwise be told to "update" something it does not have.
 * Installing is the panel's button, not a notification. (Gemini, plan round 1.)</p>
 */
export function serverStatus(facts: ServerFacts): ServerStatus {
  if (!facts.fileExists) {
    return { kind: 'absent', version: '', remembered: false, updateOffered: false };
  }

  const version = facts.reported.length > 0 ? facts.reported : facts.remembered;
  if (version.length === 0) {
    return { kind: 'unknown', version: '', remembered: false, updateOffered: true };
  }

  return {
    kind: 'known',
    version,
    remembered: facts.reported.length === 0,
    updateOffered: facts.published.length > 0 && compareVersions(facts.published, version) > 0,
  };
}

/**
 * One run at a time, however many callers ask.
 *
 * <p>The panel's Update button and the ⋯ menu invoke the same command, and a download takes long
 * enough that a second click lands mid-flight. Two installs racing on one destination path is a
 * corrupt binary or a checksum that fails for a reason nobody could reconstruct — and pressing a
 * button twice because the first press seemed to do nothing is exactly the habit this button
 * taught its users.</p>
 *
 * <p>The second caller JOINS the first rather than being refused: they asked for the same thing,
 * and an error telling somebody their own click was too fast is noise.</p>
 *
 * <p><b>The reference is cleared whether the work succeeded or threw</b>, which is the half both
 * reviewers went looking for: a download that fails on a dropped connection must not leave every
 * later click joining that same rejected promise until the window is reloaded. A retry after a
 * failure is the most likely next thing a person does.</p>
 */
export class SingleFlight<T> {
  private inFlight: Promise<T> | undefined;

  public get isRunning(): boolean {
    return this.inFlight !== undefined;
  }

  public async run(work: () => Promise<T>): Promise<T> {
    if (this.inFlight !== undefined) {
      return this.inFlight;
    }

    this.inFlight = work();
    try {
      return await this.inFlight;
    } finally {
      this.inFlight = undefined;
    }
  }
}
