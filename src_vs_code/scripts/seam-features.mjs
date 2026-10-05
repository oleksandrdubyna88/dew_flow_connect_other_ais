/**
 * The capability leg of the settings seam (PLAN_one_model_catalog.md, epic 2: "capability, not version numbers").
 *
 * <p>The extension sends a catalog field, or passes a flag, only when the installed coai-mcp lists it in `--features`.
 * That rule is only as good as the two lists agreeing: a capability the extension knows (`FEATURES` in
 * `binaryFeatures.ts`) that this repository's binary does not list is a field nobody will ever send — or, the day the
 * check is loosened, one sent to a binary that ignores it. So the REAL binary is asked, through the extension's own
 * reader, and every capability the extension knows must be in its answer.</p>
 *
 * <p>A binary named by `COAI_MCP_DLL` and declared older (`COAI_SEAM_OLDER_SERVER=1`) may answer 64; that is a skip,
 * said as one. Any other failure is the leg failing.</p>
 */
import { spawn } from 'node:child_process';

const { FEATURES, readBinaryFeatures } = await import('../out/binaryFeatures.js');

/** @returns {Promise<{ listed: string[], older: boolean }>} */
export async function featuresSeam({ binary, olderExpected, fail, timeoutMs }) {
  const answer = await readBinaryFeatures(dotnetRun(binary, timeoutMs));
  if (answer.why !== '') {
    if (olderExpected && /older/u.test(answer.why)) {
      return { listed: [], older: true };
    }
    fail(`--features did not answer the list: ${answer.why}`);
  }
  const missing = Object.values(FEATURES).filter((feature) => !answer.features.includes(feature));
  if (missing.length > 0) {
    fail(`the extension knows ${missing.join(', ')}, which this coai-mcp does not list — it would never be sent`);
  }

  return { listed: [...answer.features], older: false };
}

/** The extension's `Run`, over `dotnet <binary>` — the shape every other leg starts the server in. */
function dotnetRun(binary, timeoutMs) {
  return (args) => new Promise((done) => {
    const child = spawn('dotnet', [binary, ...args], { stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    const deadline = setTimeout(() => child.kill('SIGKILL'), timeoutMs);
    child.stdout.on('data', (chunk) => { output += String(chunk); });
    child.on('error', () => { clearTimeout(deadline); done({ code: -1, output: '' }); });
    child.on('close', (code) => { clearTimeout(deadline); done({ code: code ?? -1, output }); });
  });
}
