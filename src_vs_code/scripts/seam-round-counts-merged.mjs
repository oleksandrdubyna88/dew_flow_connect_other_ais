/**
 * The round-counts leg of the seam (E6.S5 of todo/PLAN_the_round_counter_tail_E6.md).
 *
 * <p>`coai-mcp` WRITES the rounds snapshot beside `coai.db` and ANSWERS `--round-counts` and `--round-counts-merged`; the
 * extension READS those answers through `parseRoundCounts`. Each side has its own suite, and each suite compares its side
 * against its own idea of the names — a key the answer spells one way and the parser another (`roundKeys`, `keyStore`,
 * `roundsWithoutKey`, `parts`, `stores`, `storesNotIncluded`) leaves both green while the block counts one round where two ran.
 * So this runs the REAL binary built from this tree:</p>
 *
 * <ol>
 *   <li>a real local round through the stand-in CLI (an answering reviewer — nothing reaches a model, nothing is billed) over a
 *       temp data directory: the binary writes `coai.db` AND `round-counts-snapshot.json`, the live proof of the write point;</li>
 *   <li>a HAND-WRITTEN schema-1 snapshot of another store whose one round has the SAME integer id as the real round and a
 *       different key, and a third path that does not exist;</li>
 *   <li>`--round-counts-merged --now <instant> <that snapshot> <the missing path>` and `--round-counts --now <instant>`, both
 *       stdouts through the extension's REAL parser, asserted BY VALUE: two stores, the missing path not included, two rounds
 *       from one integer id, each row's keys, the plain answer equal to the live store's part.</li>
 * </ol>
 *
 * <p>Two things must hold, and the second is the one a rename breaks silently — the two-sided rule of `seam-ledger-fields.mjs`.
 * Every key the writer put on the answer comes back from the parser; AND every key this leg exists for was written, non-empty,
 * by SOME row. A leg that only compared what is present would pass a C# rename (the key is simply never written); one that only
 * checked presence would pass a TS rename (the parser drops the key and nothing compares).</p>
 *
 * <p><b>A seam must never read the developer's own configuration</b> (#904): the server gets `HOME`, `USERPROFILE` and
 * `CODEX_HOME` pointing into a scratch folder — `homeEnvironment` of the ledger-fields leg, imported, not copied.</p>
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJsonWith } = await import('../out/serverSettingsFile.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { FEATURES } = await import('../out/binaryFeatures.js');
const { parseRoundCounts, totalRounds } = await import('../out/roundCounts.js');
const { answerOf, sessionsFor } = await import('./seam-session.mjs');
const { homeEnvironment } = await import('./seam-ledger-fields.mjs');

/** The file the owning side writes, and the store id of the hand-written snapshot (not the real round's, which the binary computes). */
export const SNAPSHOT_FILE = 'round-counts-snapshot.json';
export const OTHER_STORE = '3f9c2b7e1a4d5c6f8e9a0b1c2d3e4f50';
export const OTHER_KEY = '0c7a1e5d9b3f2a8c6e4d1b0f9a8c7e6d';

/** What the answer is expected to carry non-empty, per level — the keys this leg exists for. */
export const TRACKED_ANSWER = ['stores', 'storesNotIncluded'];
export const TRACKED_STORE = ['id', 'live', 'asOf', 'quiet', 'conflicts'];
export const TRACKED_ROW = ['roundKeys', 'keyStore', 'roundsWithoutKey', 'parts'];

/** The clean answer of an answering reviewer: no findings. */
const NO_FINDINGS = '{"findings": []}';

function removeQuietly(path) {
  try {
    rmSync(path, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
  } catch {
    // Deliberately swallowed: the verdict (or the error that produced it) is what the caller reports.
  }
}

/** A home of its own for the server: the only one it may read. */
function isolatedHome() {
  const home = mkdtempSync(join(tmpdir(), 'coai-seam-rounds-home-'));
  mkdirSync(join(home, '.codex'));

  return home;
}

/** A checkout with a `feature` branch ahead of `main`, which is what a round reviews. */
function scratchRepo() {
  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-rounds-repo-'));
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'ignore', 'pipe'] });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'seam@example.invalid');
  git('config', 'user.name', 'seam');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 3; }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the committed state');
  git('checkout', '-b', 'feature');

  return dir;
}

function writeSettings(dataDir, cli) {
  const rows = vendorsFrom([{
    id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', enabled: true, plan: true, code: true,
    baseUrl: '', executablePath: cli, pricePerMillionIn: 0, pricePerMillionOut: 0,
  }]);
  writeFileSync(join(dataDir, 'settings.json'), serverSettingsJsonWith({ ...DEFAULTS }, rows, {
    writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features: Object.values(FEATURES),
  }), 'utf8');
}

/** A real plan round through an answering reviewer; the binary records it and writes the snapshot beside coai.db. */
async function realRound(serverSession, repoPath, home) {
  const session = serverSession({
    ...homeEnvironment(home), FAKECLI_MODE: 'vendor', FAKECLI_STDOUT: NO_FINDINGS, FAKECLI_OUTFILE_TEXT: NO_FINDINGS, FAKECLI_EXIT: '0',
  });
  await session.ready;
  await session.call('open', { repoPath, branch: 'feature' });
  const reply = answerOf(await session.call('review_plan', { repoPath, branch: 'feature', planText: ['# Plan', '', 'Make Count() return 4.', ''].join('\n') }));
  await session.close();
  if (typeof reply.error === 'string') {
    throw new Error(`the real round did not run: ${reply.error}`);
  }
}

/** The snapshot the binary wrote: its store id and its one round (id and key). */
export function liveSnapshot(dataDir) {
  const path = join(dataDir, SNAPSHOT_FILE);
  if (!existsSync(path)) {
    throw new Error(`the binary recorded a round and wrote no ${SNAPSHOT_FILE} beside coai.db`);
  }
  const written = JSON.parse(readFileSync(path, 'utf8'));
  const round = written.rounds?.[0];
  if (round === undefined || typeof round.key !== 'string' || !/^[0-9a-f]{32}$/u.test(round.key)) {
    throw new Error(`the written snapshot holds no round with a 32-hex key: ${JSON.stringify(written).slice(0, 600)}`);
  }

  return { storeId: written.store.id, id: round.id, key: round.key, stage: round.stage, provider: round.reviewers?.[0]?.provider ?? '' };
}

/** A schema-1 snapshot of ANOTHER store: one round with the real round's integer id and a different key, written an hour before `now`. */
export function foreignSnapshot(live, nowMs) {
  const at = (ms) => new Date(ms).toISOString();
  const written = nowMs - 3 * 60_000;

  return JSON.stringify({
    schema: 1, writtenUtc: at(written), coaiMcpVersion: '9.9.9',
    store: { id: OTHER_STORE, platform: 'linux', host: 'wsl', wslDistro: 'SeamDistro', side: '', directory: '/seam/other/coai-mcp' },
    fromUtc: at(written - 7 * 24 * 3_600_000), truncated: false,
    rounds: [{ id: live.id, key: OTHER_KEY, startedUtc: at(nowMs - 3_600_000), stage: live.stage, reviewers: [{ provider: live.provider, model: '' }] }],
  });
}

/** One-shot mode of the real binary, with the leg's data directory and the scratch home — and nothing of the developer's. */
function ask(binary, args, dataDir, home) {
  return execFileSync('dotnet', [binary, ...args], {
    env: { ...process.env, ...homeEnvironment(home), COAI_DATA_DIR: dataDir, CLAUDE_CODE_SESSION_ID: '', CODEX_SESSION_ID: '', GEMINI_CLI_SESSION_ID: '' },
    encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
  });
}

/** The answer as the writer spelled it, and as the extension's parser read it. */
export function answerPair(output) {
  const parsed = parseRoundCounts(output);
  if (parsed === undefined) {
    throw new Error(`the extension's parser refused the binary's answer: ${output.slice(0, 600)}`);
  }

  return { raw: JSON.parse(output), parsed };
}

const rowsOf = (answer, name = '7d') => (answer.windows.find((one) => one.name === name)?.rows ?? []);

/** `key in raw` with a value that says something: a non-empty array or object, or a number. */
function written(value) {
  if (Array.isArray(value)) {
    return value.length > 0;
  }

  return typeof value === 'object' && value !== null ? Object.keys(value).length > 0 : value !== undefined && value !== '';
}

/** The keys of `rawObject` that the parsed object does not carry — the writer wrote them and the parser did not give them back. */
function dropped(rawObject, parsedObject, where) {
  return Object.keys(rawObject).filter((key) => !(key in parsedObject)).map((key) => `${where}: ${key} is on the wire and absent from the parsed answer`);
}

/** The parser's drops over a whole answer: top level, every window row, every store, every not-included entry. */
export function disagreements({ raw, parsed }) {
  const known = ['nowUtc', 'windows', 'modelsKnownSince', 'stores', 'storesNotIncluded'];
  const top = Object.keys(raw).filter((key) => known.includes(key) && !(key in parsed)).map((key) => `answer: ${key} is on the wire and absent from the parsed answer`);
  const rows = raw.windows.flatMap((window, w) => window.rows.flatMap((row, r) => dropped(row, parsed.windows[w]?.rows[r] ?? {}, `${window.name} row ${r}`)));
  const stores = (raw.stores ?? []).flatMap((store, s) => dropped(store, parsed.stores?.[s] ?? {}, `store ${s}`));
  const notIncluded = (raw.storesNotIncluded ?? []).flatMap((item, i) => dropped(item, parsed.storesNotIncluded?.[i] ?? {}, `storesNotIncluded ${i}`));

  return [...top, ...rows, ...stores, ...notIncluded];
}

/** The tracked keys NO row/store/answer wrote with a value — a C# rename leaves the key unwritten, and nothing else would notice. */
export function neverWritten({ raw }) {
  const rows = raw.windows.flatMap((window) => window.rows);
  const stores = raw.stores ?? [];

  return [
    ...TRACKED_ANSWER.filter((key) => !written(raw[key])).map((key) => `answer.${key}`),
    ...TRACKED_STORE.filter((key) => !stores.some((store) => key in store)).map((key) => `stores[].${key}`),
    ...TRACKED_ROW.filter((key) => !rows.some((row) => written(row[key]) || (key === 'roundsWithoutKey' && key in row))).map((key) => `rows[].${key}`),
  ];
}

/** What is wrong with the two crossings as a CONTRACT — the two-sided rule — or an empty string. */
export function contractVerdict(merged, plain) {
  const wrong = [...disagreements(merged), ...disagreements(plain)];
  if (wrong.length > 0) {
    return `a key the binary put on an answer did not come back from parseRoundCounts:\n  ${wrong.join('\n  ')}`;
  }
  const missing = neverWritten(merged);

  return missing.length > 0 ? `the binary never wrote ${missing.join(', ')} on its merged answer: ${JSON.stringify(merged.raw).slice(0, 1500)}` : '';
}

function same(actual, expected, what) {
  return JSON.stringify(actual) === JSON.stringify(expected) ? '' : `${what}: expected ${JSON.stringify(expected)}, read ${JSON.stringify(actual)}`;
}

/** What is wrong with the NUMBERS, BY VALUE, through the parsed answers. */
export function valueVerdict(merged, plain, live, missingPath, foreignPath) {
  const stage = live.stage === 'PlanReview' ? 'plan' : 'code';
  const mergedRow = rowsOf(merged.parsed).find((one) => one.provider === live.provider);
  const plainRow = rowsOf(plain.parsed).find((one) => one.provider === live.provider);
  const livePart = mergedRow?.parts?.find((part) => part.store === live.storeId);
  const problems = [
    same((merged.parsed.stores ?? []).map((one) => [one.live, one.id]), [[true, live.storeId], [false, OTHER_STORE]], 'the stores counted'),
    same((merged.parsed.stores ?? [])[1]?.paths, [foreignPath], 'the other store\'s paths'),
    same((merged.parsed.storesNotIncluded ?? []).map((one) => [one.path, one.reason]), [[missingPath, 'missing']], 'the stores not included'),
    same(mergedRow === undefined ? undefined : mergedRow.rounds[stage], 2, `the ${stage} rounds of ${live.provider} in 7 d (two rounds, one integer id)`),
    same([...(mergedRow?.roundKeys ?? [])].sort(), [live.key, OTHER_KEY].sort(), 'the merged row\'s round keys'),
    same(mergedRow?.roundsWithoutKey, 0, 'the merged row\'s rounds without a key'),
    same(Object.entries(mergedRow?.keyStore ?? {}).sort(), [[live.key, live.storeId], [OTHER_KEY, OTHER_STORE]].sort(), 'the merged row\'s key to store'),
    same((mergedRow?.parts ?? []).map((part) => [part.store, totalRounds(part)]), [[live.storeId, 1], [OTHER_STORE, 1]], 'the merged row\'s parts'),
    same(plain.parsed.stores, undefined, 'the plain answer\'s stores (it names none)'),
    same(plainRow === undefined ? undefined : plainRow.rounds[stage], 1, `the plain ${stage} rounds of ${live.provider}`),
    same(plainRow?.roundKeys, [live.key], 'the plain row\'s round keys (the live store\'s part)'),
    same(plainRow?.roundsWithoutKey, 0, 'the plain row\'s rounds without a key'),
    same(livePart === undefined ? undefined : totalRounds(livePart), plainRow === undefined ? undefined : totalRounds(plainRow), 'the plain count against the live store\'s part'),
  ];

  return problems.filter((one) => one.length > 0).join('\n  ');
}

async function run({ binary, fakeCli, dataDir, timeoutMs }) {
  const repoPath = scratchRepo();
  const home = isolatedHome();
  const otherDir = mkdtempSync(join(tmpdir(), 'coai-seam-rounds-other-'));
  try {
    writeSettings(dataDir, fakeCli);
    await realRound(sessionsFor({ command: 'dotnet', args: [binary], dataDir, timeoutMs }), repoPath, home);
    const live = liveSnapshot(dataDir);
    const nowMs = Date.now() + 60_000;
    const now = new Date(nowMs).toISOString();
    const foreignPath = join(otherDir, SNAPSHOT_FILE);
    const missingPath = join(otherDir, 'nowhere', SNAPSHOT_FILE);
    writeFileSync(foreignPath, foreignSnapshot(live, nowMs), 'utf8');
    const merged = answerPair(ask(binary, ['--round-counts-merged', '--now', now, foreignPath, missingPath], dataDir, home));
    const plain = answerPair(ask(binary, ['--round-counts', '--now', now], dataDir, home));
    const verdict = [contractVerdict(merged, plain), valueVerdict(merged, plain, live, missingPath, foreignPath)].filter((one) => one.length > 0).join('\n');
    if (verdict.length > 0) {
      throw new Error(verdict);
    }

    const row = rowsOf(merged.parsed).find((one) => one.provider === live.provider);

    return { stores: merged.parsed.stores.length, rounds: totalRounds(row) };
  } finally {
    removeQuietly(repoPath);
    removeQuietly(home);
    removeQuietly(otherDir);
  }
}

/** @returns {Promise<{ stores: number, rounds: number }>} what the leg saw, for its line */
export async function roundCountsMergedSeam({ binary, fakeCli, fail, timeoutMs }) {
  if (fakeCli === '') {
    fail('no FakeCli build found. Run: dotnet build src_mcp/tests_fakecli/FakeCli.csproj');
  }
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-rounds-'));
  let outcome;
  try {
    outcome = await run({ binary, fakeCli, dataDir, timeoutMs });
  } catch (error) {
    outcome = error instanceof Error ? error.message : String(error);
  } finally {
    removeQuietly(dataDir);
  }
  if (typeof outcome === 'string') {
    fail(`the round counts did not cross from coai-mcp to the extension: ${outcome}`);
  }

  return outcome;
}
