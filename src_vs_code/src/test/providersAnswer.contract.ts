import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { parseProviderNotes } from '../providers';
import { serverSettingsJsonWith } from '../serverSettingsFile';
import { settingsFrom } from '../settingsShape';
import { vendorsFrom } from '../vendors';
import { coaiMcpBinary } from './coaiMcpBinary';

/**
 * The SHAPE of `--providers`' `settingProblems`, read off the REAL coai-mcp (todo/PLAN_paths_per_side.md E5.2).
 *
 * <p>Two implementations hold this field — the C# that prints it and the parser in `providers.ts` that reads it — and
 * two suites agreeing with themselves is not a check (`testing.md`, *a contract with TWO IMPLEMENTATIONS*). So the binary
 * this repository built is asked, over a settings file the extension's OWN writer produced with one existing root and
 * one that is not there, and what comes back is asserted field by field against the wire shape, then read through the
 * extension's parser. `npm run test:contract` runs it; it refuses — never skips — when no binary is built, exactly as
 * the Team-server contract beside it refuses without a server.</p>
 */

const TIMEOUT_MS = 30_000;

/** `dotnet <coai-mcp.dll> --providers` over a data directory, as the panel's probe runs it. */
function providersIn(binary: string, dataDir: string): Promise<string> {
  return new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--providers'], {
      env: { ...process.env, COAI_DATA_DIR: dataDir, COAI_VENDORS: '', COAI_PROVIDERS: '' },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`--providers did not answer within ${TIMEOUT_MS} ms`));
    }, TIMEOUT_MS);
    child.stdout.on('data', (chunk) => { out += String(chunk); });
    child.stderr.on('data', (chunk) => { err += String(chunk); });
    child.on('error', (reason) => { clearTimeout(deadline); broke(reason); });
    child.on('close', (code) => {
      clearTimeout(deadline);
      if (code !== 0) {
        broke(new Error(`--providers exited ${code}\n${err}`));

        return;
      }
      done(out);
    });
  });
}

test('--providers names a missing root as a settingProblem of the wire shape — key, kind, value, sentence — and the parser reads it back', async () => {
  const binary = coaiMcpBinary();
  assert.ok(binary.length > 0, 'no coai-mcp build found. Run: dotnet build src_mcp/src/CoaiMcp.csproj — a contract check that quietly passes without its binary is the green tick this file exists to refuse');

  const dataDir = mkdtempSync(join(tmpdir(), 'coai-contract-problems-'));
  // The roots live OUTSIDE the data directory: a root inside it is refused for WHERE it is, with no kind — the first
  // version of this test put them under `dataDir` and the server answered `settingProblems: []` for a reason that had
  // nothing to do with the field (`testing.md`, *a fixture the code REJECTS proves nothing*).
  const roots = mkdtempSync(join(tmpdir(), 'coai-contract-roots-'));
  const here = join(roots, 'here');
  mkdirSync(here);
  const gone = join(roots, 'gone');
  try {
    const stored: Record<string, unknown> = { qconsultRoots: [here, gone] };
    const write = { writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features: [] as string[] };
    writeFileSync(join(dataDir, 'settings.json'), serverSettingsJsonWith(settingsFrom((key) => stored[key]), vendorsFrom([]), write), 'utf8');

    const output = await providersIn(binary, dataDir);
    const answer = JSON.parse(output) as { settingProblems?: unknown; unrecognised?: unknown };

    // The control first: the existing root is accepted and the gone one refused, so `unrecognised` holds exactly the
    // one sentence — if it does not, the roots never reached the server and nothing below is about the field.
    assert.ok(Array.isArray(answer.unrecognised) && answer.unrecognised.length === 1, `the control failed — one refused root expected in unrecognised, the server said ${JSON.stringify(answer.unrecognised)}`);
    assert.ok(Array.isArray(answer.settingProblems), `settingProblems is not a list: ${output.slice(0, 300)}`);
    const problems = answer.settingProblems as Record<string, unknown>[];
    assert.equal(problems.length, 1, `one missing root is one problem; the server said ${JSON.stringify(problems)}`);
    const [problem] = problems;
    assert.deepEqual(Object.keys(problem!).sort(), ['key', 'kind', 'sentence', 'value'], 'the wire shape is these four fields and no other');
    assert.equal(problem!['key'], 'COAI_QCONSULT_ROOTS');
    assert.equal(problem!['kind'], 'missing-folder');
    assert.equal(problem!['value'], gone, 'the STORED spelling, as the extension wrote it');
    assert.ok(Array.isArray(answer.unrecognised) && answer.unrecognised.includes(problem!['sentence']), 'the sentence is the one unrecognised carries, so the toast can drop it');

    const notes = parseProviderNotes(output);
    assert.deepEqual(notes.settingProblems.map((one) => [one.key, one.kind, one.value]), [['COAI_QCONSULT_ROOTS', 'missing-folder', gone]]);
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
    rmSync(roots, { recursive: true, force: true });
  }
});
