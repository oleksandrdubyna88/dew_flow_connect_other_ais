/**
 * The ledger-fields leg of the seam (E3 of todo/PLAN_the_round_counter_tail_E3.md, S1, S2 and S4).
 *
 * <p>`coai-mcp` WRITES `usage.jsonl` lines and the extension READS them through `parseUsageLine`; each side has its own suite,
 * and each suite compares its side against its own idea of the names. E3 added keys to that line — `consultation` (S4), the
 * labels `cli`, `cliVersion`, `effort`, `effortFrom`, `tier` (S1) and a local `endCause` (S2) — and a key the writer spells
 * one way and the reader another leaves both suites green while every figure that depends on it reads empty. So this runs
 * the REAL binary over stdio against the stand-in CLI its own tests use, takes the ledger lines it actually wrote, and reads
 * the same lines through the extension's OWN parser.</p>
 *
 * <p>Two things must hold, and the second is the one a renamed key breaks silently. Every key the writer put on a line comes
 * back equal from the parser (an empty value on disk is "not recorded" and reads as absent — the contract, not a loss); AND
 * every key this leg exists for was written, non-empty, by SOME line. A leg that only compared what was present would pass a
 * C# rename (the key is simply never written); one that only checked presence would pass a TS rename (the parser drops the
 * key, and nothing compares).</p>
 *
 * <p>Two real runs feed it, nothing reaches a model and nothing is billed: a consultation that answers (the consultation id and
 * the labels) and a local round whose reviewer outlives the round's one-minute clock (a cancelled line saying `roundClock`).
 * The chat lines are written by the extension itself, so there is no `coai-mcp` writer to cross for them.</p>
 *
 * <p>The client's own cancellation (`withdrawn`) and the host's stop (`hostStopped`) cannot be driven live: no review tool takes
 * the request's token, so a client's `notifications/cancelled` never reaches the round (measured while writing this leg), and
 * the host's stop only records a cause alongside a caller token that fires. Those two words are pinned by the C# tests only.</p>
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJsonWith } = await import('../out/serverSettingsFile.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { FEATURES } = await import('../out/binaryFeatures.js');
const { parseUsageLine } = await import('../out/usage.js');
const { answerOf, sessionsFor } = await import('./seam-session.mjs');

/** The keys this leg exists for: the S1 labels, the consultation of S4 and the local cause of S2. */
export const TRACKED = ['consultation', 'cli', 'cliVersion', 'effort', 'effortFrom', 'tier', 'endCause'];

const CODEX_VERSION = 'codex-cli 0.131.0';

/** A round's own clock is whole minutes at the smallest; the reply waits for it, plus the time to kill the reviewer. */
const CLOCK_WAIT_MS = 150_000;

/**
 * Removes a scratch folder with the same retries the data directory gets (a just-killed git or reviewer can still hold a handle on
 * Windows), and never lets a failure to clean up replace the leg's verdict: a leftover temp folder is not the thing under test.
 */
function removeQuietly(path) {
  try {
    rmSync(path, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
  } catch {
    // Deliberately swallowed: the verdict (or the error that produced it) is what the caller reports.
  }
}

/** A checkout with a `feature` branch ahead of `main`, which is what a round reviews. */
function scratchRepo() {
  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-ledger-repo-'));
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'ignore', 'pipe'] });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'seam@example.invalid');
  git('config', 'user.name', 'seam');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 3; }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the committed state');
  git('checkout', '-b', 'feature');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 4; }\n', 'utf8');

  return dir;
}

function writeSettings(dataDir, cli) {
  const rows = vendorsFrom([{
    id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', enabled: true, plan: true, code: true,
    baseUrl: '', executablePath: cli, pricePerMillionIn: 0, pricePerMillionOut: 0, effort: 'high',
  }]);
  writeFileSync(join(dataDir, 'settings.json'), serverSettingsJsonWith({ ...DEFAULTS, roundTimeoutMinutes: 1 }, rows, {
    writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features: Object.values(FEATURES),
  }), 'utf8');
}

/** The raw lines the writer left, each beside what the extension's parser made of it. */
export function readLedger(dataDir) {
  const path = join(dataDir, 'usage.jsonl');
  if (!existsSync(path)) {
    return [];
  }

  return readFileSync(path, 'utf8').split('\n').filter((one) => one.trim().length > 0)
    .map((text) => ({ raw: JSON.parse(text), parsed: parseUsageLine(text) }));
}

/**
 * The tracked keys a line carries whose value the extension's parser did not read back. An empty value on disk is "not
 * recorded" (the writer spells an unlabelled field as '') and the parser reads it as absent.
 */
export function disagreements(lines) {
  return lines.flatMap(({ raw, parsed }, index) => TRACKED
    .filter((key) => key in raw && parsed?.[key] !== (raw[key] === '' ? undefined : raw[key]))
    .map((key) => `line ${index + 1}: ${key} is ${JSON.stringify(raw[key])} on disk and ${JSON.stringify(parsed?.[key])} through parseUsageLine`));
}

/** The tracked keys some line wrote with a value. */
export function keysWritten(lines) {
  return TRACKED.filter((key) => lines.some(({ raw }) => typeof raw[key] === 'string' && raw[key].length > 0));
}

/** A consultation that answers: the id the server gave back, which the consult line must carry. */
async function consultation(serverSession, repoPath) {
  const session = serverSession({
    FAKECLI_MODE: 'vendor',
    FAKECLI_STDOUT: `${JSON.stringify({ type: 'thread.started', thread_id: '0199-seam-ledger' })}\n`,
    FAKECLI_OUTFILE_TEXT: 'Print the token stream: your loop stops one short.',
    FAKECLI_VERSION_STDOUT: CODEX_VERSION,
  });
  await session.ready;
  const answered = answerOf(await session.consult(repoPath));
  await session.close();
  if (typeof answered.consultationId !== 'string' || answered.consultationId.length === 0) {
    throw new Error(`the consultation did not open. The server answered: ${JSON.stringify(answered).slice(0, 400)}`);
  }

  return answered.consultationId;
}

/** A local round whose only reviewer sleeps past the round's one-minute clock: the clock is the one source that fires. */
async function roundPastItsClock(serverSession, repoPath) {
  const session = serverSession({ FAKECLI_MODE: 'vendor', FAKECLI_SLEEP_MS: '240000' });
  await session.ready;
  await session.call('open', { repoPath, branch: 'feature' });
  const planText = ['# Plan', '', 'Make Count() return 4.', ''].join('\n');
  await session.call('review_plan', { repoPath, branch: 'feature', planText });
  await session.close();
}

/** What is wrong with the ledger the real writer left, or an empty string when it crossed whole. */
export function verdictOn(lines, consultationId) {
  const wrong = disagreements(lines);
  if (wrong.length > 0) {
    return `a key the writer put on a line did not come back from parseUsageLine:\n  ${wrong.join('\n  ')}`;
  }
  const missing = TRACKED.filter((key) => !keysWritten(lines).includes(key));
  if (missing.length > 0) {
    return `the writer never wrote ${missing.join(', ')} on any of its ${lines.length} lines: ${JSON.stringify(lines.map(({ raw }) => raw)).slice(0, 1500)}`;
  }
  if (!lines.some(({ parsed }) => parsed?.consultation === consultationId)) {
    return `no line read through parseUsageLine names the consultation ${consultationId} the server answered with`;
  }
  // The outcome of a local line is the failure's sentence, not the word `Cancelled` — the cause is what names the ending.
  const cut = lines.filter(({ parsed }) => parsed?.endCause === 'roundClock');

  return cut.length > 0 ? '' : `no line reads roundClock through parseUsageLine; the causes read were ${JSON.stringify(lines.map(({ parsed }) => parsed?.endCause))}`;
}

async function run({ binary, fakeCli, dataDir, timeoutMs }) {
  const repoPath = scratchRepo();
  try {
    writeSettings(dataDir, fakeCli);
    const consultationId = await consultation(sessionsFor({ command: 'dotnet', args: [binary], dataDir, timeoutMs }), repoPath);
    await roundPastItsClock(sessionsFor({ command: 'dotnet', args: [binary], dataDir, timeoutMs: CLOCK_WAIT_MS }), repoPath);
    const lines = readLedger(dataDir);
    const verdict = verdictOn(lines, consultationId);
    if (verdict.length > 0) {
      throw new Error(verdict);
    }

    return { keys: keysWritten(lines), lines: lines.length };
  } finally {
    removeQuietly(repoPath);
  }
}

/** @returns {Promise<{ keys: string[], lines: number }>} what the leg saw, for its line */
export async function ledgerFieldsSeam({ binary, fakeCli, fail, timeoutMs }) {
  if (fakeCli === '') {
    fail('no FakeCli build found. Run: dotnet build src_mcp/tests_fakecli/FakeCli.csproj');
  }
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-ledger-'));
  let outcome;
  try {
    outcome = await run({ binary, fakeCli, dataDir, timeoutMs });
  } catch (error) {
    outcome = error instanceof Error ? error.message : String(error);
  } finally {
    removeQuietly(dataDir);
  }
  if (typeof outcome === 'string') {
    fail(`the ledger's new fields did not cross from coai-mcp to the extension: ${outcome}`);
  }

  return outcome;
}
