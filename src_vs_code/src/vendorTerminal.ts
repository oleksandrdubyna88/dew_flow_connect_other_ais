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

export function vendorTerminal(vendor: Vendor): VendorTerminal {
  // An unknown runtime rides the Codex CLI against its own base URL, which is the deliberate
  // fallback everywhere else too. A runtime this build KNOWS must be listed above: the chain this
  // replaced quietly opened codex for `antigravity`, so the button meant for signing a vendor in
  // started a different vendor's CLI under that vendor's name.
  const executable = EXECUTABLE[vendor.runtime] ?? 'codex';
  const model =
    vendor.model.length === 0
      ? []
      : vendor.runtime === 'claude' || vendor.runtime === 'antigravity'
        ? ['--model', vendor.model]
        : ['-m', vendor.model];
  const provider = vendor.baseUrl.length === 0 ? [] : providerOverrides(vendor);

  return {
    command: [executable, ...provider, ...model].join(' '),
    usageCommand: USAGE_COMMAND[vendor.runtime] ?? '',
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
export interface VendorInstall {
  /**
   * The install command for a shell-agnostic vendor, or empty when the shells differ.
   *
   * <p>They differ for exactly one vendor, and that is why the two fields below exist: `agy` has a
   * snap package on Linux and nothing on Windows, where it ships with the Antigravity app.</p>
   */
  readonly command: string;
  /** What PowerShell would run, or empty when there is nothing to run there. */
  readonly powershell: string;
  /** What bash would run, or empty when there is nothing to run there. */
  readonly bash: string;
  /** Getting to the point where that command works — this is where the shells actually differ. */
  readonly prerequisite: { readonly powershell: string; readonly bash: string };
  /** Where to read more, and the whole answer for a CLI with no install command. */
  readonly docs: string;
  /** Anything a person must know before running it. */
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

/**
 * <p><b>A CLI npm does not publish is pointed at, never invented.</b> `agy` ships as a Go binary
 * with the Antigravity app; a plausible-looking npm line for it would be a command that fails, in
 * the one place somebody came to precisely because they did not know the answer.</p>
 */
export function vendorInstall(vendor: Vendor): VendorInstall {
  if (vendor.runtime === 'antigravity') {
    return ANTIGRAVITY_INSTALL;
  }

  const runtime = PACKAGE[vendor.runtime] !== undefined ? vendor.runtime : 'codex';
  const command = `npm install -g ${PACKAGE[runtime]}`;

  return {
    command,
    powershell: command,
    bash: command,
    // The command itself is identical in both shells — npm does not care. What differs is getting
    // node in the first place, which is the actual reason somebody is reading this on a fresh box.
    prerequisite: {
      powershell: 'winget install OpenJS.NodeJS.LTS',
      bash: 'sudo apt install -y nodejs npm   # or: nvm install --lts',
    },
    docs: DOCS[vendor.runtime] ?? DOCS['codex']!,
    note:
      vendor.baseUrl.length === 0
        ? ''
        : `${vendor.id} rides the Codex CLI against ${vendor.baseUrl}, so this installs codex.`,
  };
}

/**
 * Antigravity: pointed at, never installed by a command from here.
 *
 * <p><b>Official sources only — an operator decision, and this is where it bites.</b> There IS a
 * `antigravity-cli` snap for Linux, at the version Google ships, and it was briefly offered here.
 * Its publisher is not Google: it is a third party repackaging the real CLI. A button that installs
 * software is a button people press without reading, so it may only ever offer what the vendor
 * itself publishes. `OFFICIAL_SOURCES` and the test over it are that rule in a form a future change
 * cannot quietly break.</p>
 *
 * <p>So on Linux there is no command to give, because the vendor publishes none: `agy` ships as a
 * Go binary with the Antigravity app, and `npm install -g antigravity` is a 404. The honest answer
 * is the documentation, and — measured — the Windows `agy.exe` DOES launch from WSL through
 * interop, so a Windows-side install plus the new CLI-path field is a real route for anyone whose
 * repository lives on a Windows drive.</p>
 */
const ANTIGRAVITY_INSTALL: VendorInstall = {
  command: '',
  powershell: '',
  bash: '',
  prerequisite: {
    powershell: 'install the Antigravity app; the CLI comes with it',
    bash: 'install the Antigravity app on the Windows side — its agy.exe runs from WSL through interop',
  },
  docs: 'https://antigravity.google',
  note:
    'The Antigravity CLI has no install command from a source Google publishes: `agy` ships with the Antigravity app, and npm has no package for it. Install the app, then sign in once. On WSL its Windows agy.exe does run through interop — put its path in this reviewer\u2019s CLI path field.',
};

/**
 * The only publishers an install command may come from.
 *
 * <p>One line per vendor, and nothing else is allowed — see the test. A third-party repackaging can
 * be the most convenient thing on the machine and it still does not go behind a button.</p>
 */
export const OFFICIAL_SOURCES: readonly string[] = ['npm install -g @openai/', 'npm install -g @google/', 'npm install -g @anthropic-ai/'];
