import { Platform } from './vendorTerminal';

/**
 * Which version of a reviewer's CLI is installed, which one the vendor publishes, and therefore
 * whether the update button has anything to offer.
 *
 * <p><b>There is no "update command" here, and that is a finding rather than an omission.</b> Every
 * vendor this build ships updates by re-running its own installer: OpenAI's page prints the same
 * `curl … install.sh | sh` under *Install Codex* and under *Update Codex*; Anthropic's native
 * install is the same `install.sh` / `install.ps1`; `agy` has no `update` subcommand at all
 * (checked against the binary's own `--help`, not assumed). So the button reuses
 * {@link vendorInstall} verbatim, and the only new knowledge needed is the pair of version
 * numbers.</p>
 *
 * <p><b>Every source below was checked at the vendor's own site on 2026-09-01.</b> That is the
 * standing rule for anything this panel offers to run — and it is not ceremony: the last vendor
 * fact taken on trust here ("Antigravity publishes no Linux CLI") was false, shipped, and held in
 * place by a test written from the same belief.</p>
 */

/** What `process.arch` reports, for the two the vendors actually build for. */
export type Arch = 'x64' | 'arm64';

/** Where a runtime's newest published version can be read from. */
export type VersionSource =
  | { readonly kind: 'npm'; readonly package: string }
  | { readonly kind: 'manifest'; readonly url: string };

/** The npm package each CLI publishes — queried live, not recalled. */
const NPM_PACKAGE: Record<string, string> = {
  codex: '@openai/codex',
  gemini: '@google/gemini-cli',
  claude: '@anthropic-ai/claude-code',
};

/**
 * Antigravity's release manifest, which is Google's own auto-updater endpoint.
 *
 * <p>Line 99 of `antigravity.google/cli/install.sh` builds
 * `$DOWNLOAD_BASE_URL/manifests/$platform.json` and reads `.version` from it. Same base, same
 * naming — `${os}_${arch}`, with `amd64` rather than `x64` and `windows` rather than `win32` —
 * so this cannot drift from what the install button installs. All six were fetched and all six
 * answered.</p>
 */
const ANTIGRAVITY_MANIFESTS = 'https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests';

const MANIFEST_OS: Record<Platform, string> = {
  linux: 'linux',
  darwin: 'darwin',
  win32: 'windows',
};

const MANIFEST_ARCH: Record<Arch, string> = {
  x64: 'amd64',
  arm64: 'arm64',
};

/**
 * Where to read the newest published version of one runtime, or nothing when this build does not
 * know the vendor.
 *
 * <p>Nothing rather than a guess. An unknown runtime rides the Codex CLI for REVIEWS, which is a
 * deliberate fallback — but "which version is installed" is a different question, and answering it
 * with codex's number for a vendor that is not codex would be a confident lie.</p>
 */
export function versionSourceFor(runtime: string, platform: Platform, arch: Arch): VersionSource | undefined {
  if (runtime === 'antigravity') {
    return { kind: 'manifest', url: `${ANTIGRAVITY_MANIFESTS}/${MANIFEST_OS[platform]}_${MANIFEST_ARCH[arch]}.json` };
  }
  const npmPackage = NPM_PACKAGE[runtime];

  return npmPackage === undefined ? undefined : { kind: 'npm', package: npmPackage };
}

/**
 * The version out of whatever a CLI prints for `--version`.
 *
 * <p>Every one of them says it differently: `codex-cli 0.152.0`, a bare `1.1.23`,
 * `2.1.211 (Claude Code)`. The node banner is excluded by name because a node CLI that FAILS prints
 * its own version last, and taking it would report the runtime's version as the vendor's — the same
 * trap the reviewer summaries already learned, where `exit 1: Node.js v20.20.2` hid the real
 * cause.</p>
 */
export function parseCliVersion(output: string): string {
  const withoutNodeBanner = output
    .split('\n')
    .filter((line) => !/node\.js/i.test(line))
    .join('\n');

  return /\d+\.\d+\.\d+/.exec(withoutNodeBanner)?.[0] ?? '';
}

/**
 * Whether the published version is newer than the installed one.
 *
 * <p>Compared as NUMBERS. As text `'0.9.0' > '0.10.0'`, so a `<` here would have shipped a button
 * that goes grey exactly when an update matters most.</p>
 *
 * <p>An unknown version on either side is never an update: a button that lights up because a fetch
 * failed is worse than one that never lights up.</p>
 */
export function updateAvailable(installed: string, latest: string): boolean {
  if (installed.length === 0 || latest.length === 0) {
    return false;
  }
  const here = parts(installed);
  const there = parts(latest);
  for (let i = 0; i < Math.max(here.length, there.length); i += 1) {
    const a = here[i] ?? 0;
    const b = there[i] ?? 0;
    if (a !== b) {
      return b > a;
    }
  }

  return false;
}

function parts(version: string): number[] {
  return version.split('.').map((p) => Number.parseInt(p, 10) || 0);
}

/**
 * The newest version the vendor publishes, or an empty string when it cannot be read.
 *
 * <p>Empty rather than an error: an offline machine is not a failure worth showing, which is how
 * the server's own update check already behaves.</p>
 */
export async function latestCliVersion(source: VersionSource): Promise<string> {
  const url =
    source.kind === 'npm'
      ? `https://registry.npmjs.org/${source.package.replace('/', '%2f')}/latest`
      : source.url;
  try {
    const response = await fetch(url, { headers: { accept: 'application/json' } });
    if (!response.ok) {
      return '';
    }
    const body = (await response.json()) as { version?: unknown };

    return typeof body.version === 'string' ? body.version : '';
  } catch {
    return '';
  }
}

/**
 * The names to try when asking a CLI its version, in order.
 *
 * <p>On Windows an npm global is a `.cmd` shim — `codex` is `codex.cmd` — and `spawn` without a
 * shell does no PATHEXT resolution, so a bare name finds nothing. The panel reported "could not be
 * read" for a CLI that answers perfectly from a terminal, which is the same trap this project met
 * driving vendor CLIs from code: on Windows, PATHEXT is yours to apply.</p>
 *
 * <p>A path that already names its extension is left alone: it is an answer, not a guess.</p>
 */
export function versionProbeCandidates(executable: string, platform: Platform): string[] {
  if (platform !== 'win32' || /\.[a-z]{2,4}$/i.test(executable)) {
    return [executable];
  }

  return [`${executable}.cmd`, `${executable}.exe`, executable];
}

/** What the panel shows for one vendor's CLI. Both empty means "could not tell", which is grey. */
export interface CliStatus {
  readonly installed: string;
  readonly latest: string;
}

export const UNKNOWN_CLI: CliStatus = { installed: '', latest: '' };

/** The tooltip, which is where the two numbers actually reach a person. */
export function cliStatusNote(id: string, status: CliStatus): string {
  if (status.installed.length === 0) {
    return `Update the ${id} CLI. Its installed version could not be read on this machine — the button runs the vendor's own installer, which is also how every one of them updates.`;
  }
  if (status.latest.length === 0) {
    return `${id} ${status.installed} is installed. The published version could not be read just now (offline, or the vendor's endpoint did not answer), so this button cannot say whether there is anything newer.`;
  }

  return updateAvailable(status.installed, status.latest)
    ? `${id} ${status.installed} is installed and ${status.latest} is published. Press to open a terminal with the vendor's own installer typed — re-running it IS the update, for every CLI here.`
    : `${id} ${status.installed} is the newest version published. Nothing to update.`;
}
