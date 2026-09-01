import { Vendor } from './vendors';

/**
 * Opening a vendor's own CLI in a terminal — for checking an account, signing in, or reading what
 * you have spent.
 *
 * <p>Pure, so the argv and the usage command are tests rather than something verified by watching
 * a terminal. None of these CLIs has a non-interactive "show my usage" subcommand (checked against
 * codex 0.147.0, claude, gemini 0.55.1 — there is no such flag on any of them), so the honest move
 * is to start the CLI the person already trusts and put its OWN usage command at the prompt.</p>
 */
export interface VendorTerminal {
  /** The command line that starts the CLI. */
  readonly command: string;
  /** The vendor's own in-CLI usage command, left typed at the prompt for the person to send. */
  readonly usageCommand: string;
  /** Shown before the CLI starts when there is something the person needs to know. */
  readonly note: string;
}

/** Each vendor's own name for "what have I spent" — verified in each CLI, not guessed. */
const USAGE_COMMAND: Record<string, string> = {
  claude: '/usage',
  codex: '/status',
  gemini: '/stats',
  // agy prints its own accounting under this one; it is a subcommand, not a slash command, so it
  // is typed at the shell prompt rather than inside a session.
  antigravity: 'agy usage',
};

/** Which binary each runtime actually is. A default here runs somebody else's CLI under this name. */
const EXECUTABLE: Record<string, string> = {
  codex: 'codex',
  gemini: 'gemini',
  claude: 'claude',
  antigravity: 'agy',
};

/**
 * Which binary this vendor actually is, on this machine.
 *
 * <p>A CLI path somebody set wins over the runtime's default name. The whole point of that field is
 * that PATH could not answer — in WSL, `codex` resolves to the WINDOWS npm shim through the interop
 * PATH and dies on a missing Linux binary — so anything that runs this vendor and ignores the field
 * runs the wrong software under the right name.</p>
 *
 * <p>UNQUOTED. Quoting is a command-LINE concern: a path with a space is two arguments there and one
 * argument to `spawn`, which would look for a directory literally named with quotes.</p>
 *
 * <p>An unknown runtime rides the Codex CLI, which is the deliberate fallback everywhere else too —
 * a runtime this build KNOWS must be in the table above, because the chain this replaced quietly
 * opened codex for `antigravity` and the sign-in button started a different vendor's CLI.</p>
 */
export function executableFor(vendor: Vendor): string {
  return vendor.executablePath.length > 0
    ? vendor.executablePath
    : (EXECUTABLE[vendor.runtime] ?? 'codex');
}

export function vendorTerminal(vendor: Vendor): VendorTerminal {
  // An unknown runtime rides the Codex CLI against its own base URL, which is the deliberate
  // fallback everywhere else too. A runtime this build KNOWS must be listed above: the chain this
  // replaced quietly opened codex for `antigravity`, so the button meant for signing a vendor in
  // started a different vendor's CLI under that vendor's name.
  //
  // A CLI path somebody set wins over both. The whole point of the field is that PATH could not
  // answer; a button that then runs the bare name ignores the one thing they told it. Quoted,
  // because a path with a space is otherwise two arguments.
  const executable = quoteIfNeeded(executableFor(vendor));
  const model =
    vendor.model.length === 0
      ? []
      : vendor.runtime === 'claude' || vendor.runtime === 'antigravity'
        ? ['--model', vendor.model]
        : ['-m', vendor.model];
  const provider = vendor.baseUrl.length === 0 ? [] : providerOverrides(vendor);

  return {
    command: [executable, ...provider, ...model].join(' '),
    // agy's usage command is a SHELL command rather than a slash command inside a session, so it
    // must run the same binary the button just launched — which, with a CLI path set, is not the
    // bare name.
    usageCommand:
      vendor.runtime === 'antigravity'
        ? `${executable} usage`
        : (USAGE_COMMAND[vendor.runtime] ?? ''),
    note:
      vendor.baseUrl.length === 0
        ? ''
        : `${vendor.id} reaches ${vendor.baseUrl}. Reviews take its key from the vault; this terminal ` +
          `does not, so export ${keyVariable(vendor.id)} first if the CLI asks for one.`,
  };
}

/** The same overrides the review runtime builds — one shape, so the terminal is what reviews. */
function providerOverrides(vendor: Vendor): readonly string[] {
  return [
    '-c',
    `model_provider=${vendor.id}`,
    '-c',
    `model_providers.${vendor.id}.name=${vendor.id}`,
    '-c',
    `model_providers.${vendor.id}.base_url=${vendor.baseUrl}`,
    '-c',
    `model_providers.${vendor.id}.env_key=${keyVariable(vendor.id)}`,
  ];
}

/** A path with a space is two arguments unless it is one string. */
function quoteIfNeeded(path: string): string {
  return path.includes(' ') ? `"${path}"` : path;
}

/** `mistral` → `MISTRAL_API_KEY`, matching the server's own derivation exactly. */
export function keyVariable(id: string): string {
  return `${id.toUpperCase().replace(/[-.]/g, '_')}_API_KEY`;
}

/**
 * How to install the CLI a reviewer needs, for the shell the person is actually in.
 *
 * <p>This exists because the answer is elsewhere every time: a fresh WSL box has none of these,
 * and the panel is where somebody is standing when they find that out. Hunting a vendor's docs to
 * paste one npm line is the kind of small friction that stops a reviewer being added at all.</p>
 */
/** The operating systems the buttons can answer for — what `process.platform` reports. */
export type Platform = 'win32' | 'linux' | 'darwin';

/**
 * How to install the CLI a reviewer needs, for the operating system the panel is actually on.
 *
 * <p>The platform is an argument rather than a lookup so this stays pure — and because it is the
 * fact that changes everything: in a VS Code window connected to WSL the extension host IS linux,
 * whatever the machine's badge says, and the answers must be the linux ones.</p>
 */
export interface VendorInstall {
  /** The install command for this platform, or empty when the vendor publishes none for it. */
  readonly command: string;
  /** Getting to the point where the command works — how THIS platform gets node. */
  readonly prerequisite: string;
  /** Where to read more, and the whole answer when there is no command. */
  readonly docs: string;
  /** Anything a person must know before running it, in this platform's terms. */
  readonly note: string;
}

/** The npm package each CLI publishes. Verified against what is installed here, not guessed. */
const PACKAGE: Record<string, string> = {
  codex: '@openai/codex',
  gemini: '@google/gemini-cli',
  claude: '@anthropic-ai/claude-code',
};

const DOCS: Record<string, string> = {
  codex: 'https://developers.openai.com/codex/cli',
  gemini: 'https://github.com/google-gemini/gemini-cli',
  claude: 'https://docs.claude.com/en/docs/claude-code',
  antigravity: 'https://antigravity.google',
};

/** How each platform gets node, which is the actual reason somebody reads this on a fresh box. */
const NODE_PREREQUISITE: Record<Platform, string> = {
  win32: 'winget install OpenJS.NodeJS.LTS',
  linux: 'sudo apt install -y nodejs npm   # or: nvm install --lts',
  darwin: 'brew install node',
};

/**
 * <p><b>Official sources only — an operator decision, and this is where it bites.</b> There IS an
 * `antigravity-cli` snap for Linux at the version Google ships; its publisher is a third party, and
 * it was briefly offered here. A button that installs software gets pressed without reading, so it
 * may only ever offer what the vendor itself publishes — `OFFICIAL_SOURCES` and its test are that
 * rule in a form a future change cannot quietly break.</p>
 */
export function vendorInstall(vendor: Vendor, platform: Platform): VendorInstall {
  if (vendor.runtime === 'antigravity') {
    return antigravityInstall(platform);
  }

  const runtime = PACKAGE[vendor.runtime] !== undefined ? vendor.runtime : 'codex';

  return {
    command: `npm install -g ${PACKAGE[runtime]}`,
    prerequisite: NODE_PREREQUISITE[platform],
    docs: DOCS[vendor.runtime] ?? DOCS['codex']!,
    note:
      vendor.baseUrl.length === 0
        ? ''
        : `${vendor.id} rides the Codex CLI against ${vendor.baseUrl}, so this installs codex.`,
  };
}

/**
 * Antigravity: Google's own installer, on every platform.
 *
 * <p><b>This entry was wrong twice before it was right, and both errors are worth keeping.</b>
 * First it offered a third-party snap, which the operator rejected — rightly: a button that
 * installs software may only offer what the vendor itself publishes. Then it claimed Google
 * publishes no Linux CLI at all and told people to use codex or claude instead. That was simply
 * false. There are official installer scripts at `antigravity.google/cli/`, one shell and one
 * PowerShell, and the operator found them by reading the vendor's site rather than trusting me.</p>
 *
 * <p>Verified before this was written: both URLs return the real scripts; `install.sh` branches on
 * `uname` itself and handles Darwin AND Linux, amd64 and arm64, musl included — so it is ONE
 * command for both, not one per platform; and the binary it installed on Linux answered a
 * review-shaped call with exit 0.</p>
 *
 * <p>A `curl | bash` is a supply-chain shape worth naming, so the note names it. It is the vendor's
 * own documented installer on the vendor's own domain, which is what official means here.</p>
 */
function antigravityInstall(platform: Platform): VendorInstall {
  return {
    command:
      platform === 'win32'
        ? 'irm https://antigravity.google/cli/install.ps1 | iex'
        : 'curl -fsSL https://antigravity.google/cli/install.sh | bash',
    // Nothing to get first: the CLI is a single Go binary, so there is no node in the way.
    prerequisite: '',
    docs: DOCS['antigravity']!,
    note:
      "Google's own installer, from antigravity.google. It is a piped script — read it first if that "
      + 'matters to you. It drops a single binary (`~/.local/bin/agy` on Linux and macOS) and needs '
      + 'one interactive sign-in afterwards: run `agy` once.',
  };
}

/**
 * The only publishers an install command may come from.
 *
 * <p>One line per vendor, and nothing else is allowed — see the test. A third-party repackaging can
 * be the most convenient thing on the machine and it still does not go behind a button.</p>
 */
export const OFFICIAL_SOURCES: readonly string[] = [
  'npm install -g @openai/',
  'npm install -g @google/',
  'npm install -g @anthropic-ai/',
  // The vendor's own installer on the vendor's own domain. The DOMAIN is the guard: anything else
  // piped into a shell is exactly what this list exists to keep out.
  'curl -fsSL https://antigravity.google/',
  'irm https://antigravity.google/',
];
