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
};

export function vendorTerminal(vendor: Vendor): VendorTerminal {
  const executable = vendor.runtime === 'gemini' ? 'gemini' : vendor.runtime === 'claude' ? 'claude' : 'codex';
  const model = vendor.model.length === 0 ? [] : vendor.runtime === 'claude' ? ['--model', vendor.model] : ['-m', vendor.model];
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
