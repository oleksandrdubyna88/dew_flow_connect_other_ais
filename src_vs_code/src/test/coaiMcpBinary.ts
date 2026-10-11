import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

/**
 * Where the coai-mcp this repository just built is — for the checks that drive the REAL binary: the seam
 * (`scripts/run-seam.mjs`) and the providers-answer contract (`providersAnswer.contract.ts`).
 *
 * <p>RELEASE first, because that is what CI builds — `dotnet build … -c Release` — and a finder that only looked under
 * `Debug` would refuse on its very first run there. Debug second, because that is what a person has locally.
 * `COAI_MCP_DLL` overrides both, for a published binary. One finder for both callers: the seam carried this as a
 * private function, and a second copy beside the contract test is the duplication `reuse-first.md` names.</p>
 */

/** The repository root, from this compiled file's place under `src_vs_code/out/test`. */
export const REPOSITORY = resolve(__dirname, '..', '..', '..');

/** The built `coai-mcp.dll`, or '' when nothing is built and nothing was named. */
export function coaiMcpBinary(): string {
  const named = process.env['COAI_MCP_DLL'] ?? '';
  if (named.length > 0) {
    return named;
  }
  for (const configuration of ['Release', 'Debug']) {
    const candidate = join(REPOSITORY, 'src_mcp', 'src', 'bin', configuration, 'net10.0', 'coai-mcp.dll');
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}
