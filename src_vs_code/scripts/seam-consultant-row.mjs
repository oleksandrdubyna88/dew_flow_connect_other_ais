/**
 * The consultant-row leg of the settings seam (research/PLAN_one_model_catalog.md, finding C2 of the epics 1–3 consultation).
 *
 * <p>A consultant entry that refers to a catalog row carries that row as `row` — written by the extension's own
 * `rowOnTheWire`, read by the server with the reviewer row's own parser. The two halves have to agree on every field's
 * TYPE: a row the server cannot parse refuses the consultant by name. So the settings file is written through the
 * extension's own writer, with the effort, system prompt and timeout set, and the REAL binary's `--consultants` must
 * call that consultant available; and a binary that does not list `consultantRow` must not be handed the member.</p>
 */
import { spawn } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJsonWith } = await import('../out/serverSettingsFile.js');
const { settingsFrom } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { FEATURES } = await import('../out/binaryFeatures.js');

const ROWS = [
  { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true },
  {
    id: 'consult-codex', runtime: 'claude', model: 'opus', enabled: false, uses: ['consultant'], vaultKeyName: 'claude',
    effort: 'high', systemPrompt: 'Answer in plain English.', timeoutMinutes: 9,
  },
];

const ALL = [FEATURES.consultantRow, FEATURES.systemPrompt, FEATURES.timeoutMinutes, FEATURES.cliEffort, FEATURES.apiConsultant];

/** @returns {Promise<{ effort: string }>} what the leg saw, for its line */
export async function consultantRowSeam({ binary, fail, timeoutMs }) {
  const crossed = await writtenAndAsked(binary, ALL, timeoutMs);
  if (!crossed.carried) {
    fail('the settings file did not carry the consultant\'s catalog row to a binary that lists consultantRow');
  }
  if (crossed.claude?.available !== true) {
    fail(`the server could not take the row the extension wrote — the codex caller's consultant is ${JSON.stringify(crossed.claude)}`);
  }
  const held = await writtenAndAsked(binary, ALL.filter((one) => one !== FEATURES.consultantRow), timeoutMs);
  if (held.carried) {
    fail('a binary without consultantRow was handed the row anyway');
  }

  return { effort: 'high' };
}

async function writtenAndAsked(binary, features, timeoutMs) {
  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-consultant-row-'));
  try {
    const stored = { vendors: ROWS, consultants: { codex: { vendor: 'consult-codex' } } };
    const read = (key) => stored[key];
    const write = { writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features };
    writeFileSync(join(dir, 'settings.json'), serverSettingsJsonWith(settingsFrom(read), vendorsFrom(ROWS), write), 'utf8');
    // The env values are JSON written INTO the settings file as strings: parse twice, never search the text.
    const consultants = JSON.parse(JSON.parse(readFileSync(join(dir, 'settings.json'), 'utf8'))['COAI_CONSULTANTS'] ?? '{}');
    const carried = consultants['codex']?.['row']?.['effort'] === 'high';
    const answer = await consultantsIn(binary, dir, timeoutMs);

    return { carried, claude: (answer.consultants ?? []).find((one) => one.callerKind === 'codex') };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function consultantsIn(binary, dataDir, timeoutMs) {
  return new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--consultants'], {
      env: { ...process.env, COAI_DATA_DIR: dataDir, COAI_VENDORS: '', COAI_PROVIDERS: '', COAI_CONSULTANTS: '' },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`--consultants did not answer within ${timeoutMs} ms`));
    }, timeoutMs);
    child.stdout.on('data', (b) => { out += String(b); });
    child.stderr.on('data', (b) => { err += String(b); });
    child.on('error', (e) => { clearTimeout(deadline); broke(e); });
    child.on('close', (code) => {
      clearTimeout(deadline);
      if (code !== 0) {
        broke(new Error(`--consultants exited ${code}\n${err}`));
        return;
      }
      done(JSON.parse(out));
    });
  });
}
