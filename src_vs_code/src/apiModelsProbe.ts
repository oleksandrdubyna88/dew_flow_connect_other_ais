import { probeModels, ProbedModels } from './apiKeyVendors';
import { serverEnv } from './dataDir';
import { capture } from './versionProbe';

/**
 * Asking an endpoint which models a vault key can call — through `coai-mcp --probe-api`, never from
 * here (PLAN_feature_review.md S3.6).
 *
 * <p><b>Why the server asks and this side does not.</b> The key is in the vault, and the vault is read
 * by the server; this extension never holds one. The probe's own `GET /models` is the call — one HTTP
 * client for it, in the process that has the key — and without `--model` the probe stops after the
 * list, so no completion is paid for. Its report is an allowlist that never carries the key
 * (`ProbeApiModeTests`).</p>
 *
 * <p>Apart from {@link probeModels} for the reason `providersProbe.ts` gives: a spawn beside pure
 * functions would drag `node:child_process` into a webview bundle.</p>
 */

/** Long enough for the vault read and one slow `/models` over a real network; short enough to wait on in a pick. */
const CAP_MS = 45_000;

/** The model ids the endpoint lists for the key filed under `keyName`, or why there are none. */
export async function modelsForKey(executable: string, keyName: string, baseUrl: string): Promise<ProbedModels> {
  if (executable.length === 0) {
    return { ids: [], reason: 'coai-mcp is not installed, so nothing can ask the endpoint' };
  }
  // With this window's data directory, for the reason `readProviders` gives; COAI_CREDS_KEY comes the same way.
  const { code, output } = await capture(
    executable, ['--probe-api', '--vendor', keyName, '--endpoint', baseUrl, '--timeout-seconds', '30'],
    false, CAP_MS, undefined, serverEnv());

  // Anything but 0 is a reason, never a list. `capture` keeps stdout only, so the probe's own sentence
  // (on stderr) is not here; its exit code says which kind of reason it was.
  return code === 0 ? probeModels(output) : { ids: [], reason: whyNot(code, keyName) };
}

/** The probe's exit codes in words (`ProbeApiMode`: 65 bad arguments, 78 no vault or no key, -1 the cap). */
function whyNot(code: number, keyName: string): string {
  return EXIT_REASONS[code]?.(keyName) ?? `coai-mcp --probe-api exited ${code}`;
}

const EXIT_REASONS: Readonly<Record<number, (keyName: string) => string>> = {
  78: (keyName) => `the vault could not be read, or holds no key under '${keyName}'`,
  65: () => 'coai-mcp refused the request (65) — the base URL may not be an http(s) address',
  64: () => 'the installed coai-mcp does not know --probe-api — update it',
  [-1]: () => 'coai-mcp --probe-api did not finish in time',
};
