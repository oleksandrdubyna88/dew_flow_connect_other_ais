import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { adapterFor, CHAT_RUNTIMES } from '../cliChatLaunch';
import { CONSULTING_RUNTIMES } from '../consultSettings';
import { effortRefusal } from '../featureAvailability';
import { CHAT, CONSULTING, EFFORT, THINKING } from '../featureAvailability.generated';
import { RUNTIMES } from '../models';

/**
 * Which runtime serves which feature, and which efforts each accepts — one file both halves read
 * (`shared/feature-availability.json`, PLAN_one_model_catalog.md D4, E1.2).
 *
 * <p>Frozen to today's lists in E1: coai-mcp keeps its own copy until E2.1 reads the file, and
 * `FeatureAvailabilityTests` in `src_mcp` holds its copy to this file meanwhile.</p>
 */

// out/test at run time, so three levels reach the repository root.
const SEED = join(__dirname, '..', '..', '..', 'shared', 'feature-availability.json');
const GENERATOR = join(__dirname, '..', '..', 'scripts', 'generate-feature-availability.mjs');

interface Seed {
  runtimes: string[];
  features: { consultant: string[]; chat: string[] };
  effort: { runtime: string; source: string; levels: string[]; measuredWith: string; note: string }[];
  thinking: { runtime: string; source: string; note: string }[];
}

const seed = (): Seed => JSON.parse(readFileSync(SEED, 'utf8')) as Seed;

test('the generated copy agrees with the seed, field for field', () => {
  const s = seed();

  assert.deepEqual([...CONSULTING], s.features.consultant);
  assert.deepEqual([...CHAT], s.features.chat);
  assert.deepEqual(EFFORT.map((row) => ({ ...row, levels: [...row.levels] })), s.effort);
  assert.deepEqual(THINKING.map((row) => ({ ...row })), s.thinking);
});

test('every runtime says whether it has a thinking switch (D12): only api asks its model, and every other says why not', () => {
  assert.deepEqual(THINKING.map((row) => row.runtime), [...RUNTIMES], 'one row per runtime, in the order of the file');
  assert.deepEqual(THINKING.filter((row) => row.source === 'probe').map((row) => row.runtime), ['api']);
  assert.equal(THINKING.find((row) => row.runtime === 'claude')?.source, 'none', 'the depth of claude is its effort');
  assert.equal(THINKING.every((row) => row.note.length > 0), true, 'a card with no switch shows the note');
});

test('the seed names exactly the runtimes a row can be set to', () => {
  assert.deepEqual(seed().runtimes, [...RUNTIMES]);
});

test('the extension\'s two lists ARE the file\'s, frozen to what they were', () => {
  // `api` joined when coai-mcp gained the api consultant (E2.3) — added with the product, never ahead of it.
  assert.deepEqual([...CONSULTING_RUNTIMES], ['codex', 'claude', 'antigravity', 'local', 'api']);
  assert.deepEqual([...CHAT_RUNTIMES].sort(), ['antigravity', 'claude', 'codex']);
  assert.equal(CONSULTING_RUNTIMES, CONSULTING, 'consultSettings keeps a list of its own again');
  assert.equal(CHAT_RUNTIMES, CHAT, 'cliChatLaunch keeps a list of its own again');
});

test('the chat list and the chat adapters agree: every listed runtime has one, and no adapter is unlisted', () => {
  for (const runtime of RUNTIMES) {
    assert.equal(adapterFor(runtime) !== undefined, CHAT.includes(runtime), `${runtime}: the shared file and the adapter map disagree`);
  }
});

test('an effort the file lists for the runtime is legal; another is refused naming the legal ones', () => {
  assert.equal(effortRefusal('claude', 'high'), '');
  assert.match(effortRefusal('claude', 'extreme'), /'extreme'.*low, medium, high, xhigh, max/u);
});

test('no effort at all is always legal — absent means the default', () => {
  for (const runtime of RUNTIMES) {
    assert.equal(effortRefusal(runtime, ''), '', `${runtime} refused the default`);
  }
});

test('antigravity takes no effort, by the operator\'s ruling', () => {
  assert.match(effortRefusal('antigravity', 'high'), /antigravity takes no effort/u);
});

test('a runtime nobody has measured refuses an effort and says it is unmeasured', () => {
  assert.match(effortRefusal('codex', 'high'), /not been measured/u);
});

test('a probed runtime is judged against the levels its probe reported', () => {
  assert.equal(effortRefusal('api', 'high', ['low', 'high']), '');
  assert.match(effortRefusal('api', 'max', ['low', 'high']), /'max'.*low, high/u);
  assert.match(effortRefusal('local', 'high'), /probe/u, 'with no report there is nothing to judge by');
});

function generate(doctored: unknown): { status: number | null; stderr: string } {
  const dir = mkdtempSync(join(tmpdir(), 'coai-availability-'));
  try {
    const seedPath = join(dir, 'seed.json');
    writeFileSync(seedPath, JSON.stringify(doctored), 'utf8');
    // Both outputs into the scratch folder: a doctored seed the generator accepts must never write the real files.
    const outputs = [`--out=${join(dir, 'out.ts')}`, `--fast-out=${join(dir, 'fast.ts')}`];
    const run = spawnSync(process.execPath, [GENERATOR, `--seed=${seedPath}`, ...outputs], { encoding: 'utf8' });

    return { status: run.status, stderr: run.stderr };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('the generator refuses a field it does not know', () => {
  const run = generate({ ...seed(), extra: true });

  assert.equal(run.status, 1);
  assert.match(run.stderr, /'extra'/u);
});

test('the generator refuses a feature runtime outside the runtimes', () => {
  const s = seed();
  const run = generate({ ...s, features: { ...s.features, chat: [...s.features.chat, 'emacs'] } });

  assert.equal(run.status, 1);
  assert.match(run.stderr, /'emacs'/u);
});

test('the generator refuses an effort list on antigravity', () => {
  const s = seed();
  const effort = s.effort.map((row) => (row.runtime === 'antigravity' ? { ...row, source: 'list', levels: ['high'] } : row));
  const run = generate({ ...s, effort });

  assert.equal(run.status, 1);
  assert.match(run.stderr, /antigravity/u);
});

test('the generator refuses a runtime with no effort row, or with two', () => {
  const s = seed();

  assert.equal(generate({ ...s, effort: s.effort.filter((row) => row.runtime !== 'codex') }).status, 1);
  assert.equal(generate({ ...s, effort: [...s.effort, s.effort[0]] }).status, 1);
});

test('a listed source needs levels and a measurement; another source holds none', () => {
  const s = seed();
  const claudeEmpty = s.effort.map((row) => (row.runtime === 'claude' ? { ...row, levels: [] } : row));
  const codexListed = s.effort.map((row) => (row.runtime === 'codex' ? { ...row, levels: ['high'] } : row));

  assert.equal(generate({ ...s, effort: claudeEmpty }).status, 1);
  assert.equal(generate({ ...s, effort: codexListed }).status, 1);
});

// The codex releases that refuse `service_tier=default` (todo/PLAN_codex_tier_floor.md, change 2): coai-mcp reads the
// range, the extension does not use it yet — but both halves accept and check the same file, so a range one half would
// refuse can never reach the other. The C# twin is `FastModeIsDataTests.AMalformedRange_RefusesTheSeed_*`.
type FastRow = { runtime: string; refusesStandard?: unknown };

/** The seed with one fast-mode row's `refusesStandard` replaced — or removed, when `range` is undefined. */
function withRange(runtime: string, range: unknown): unknown {
  const s = seed() as Seed & { fastMode: FastRow[] };
  const fastMode = s.fastMode.map(({ refusesStandard: _dropped, ...row }) =>
    row.runtime === runtime && range !== undefined ? { ...row, refusesStandard: range } : row);

  return { ...s, fastMode };
}

test('the generator accepts the codex row\'s range of releases that refuse the standard tier', () => {
  const run = generate(seed());

  assert.equal(run.status, 0, run.stderr);
});

test('a malformed refusesStandard range is refused, saying what is wrong with it', () => {
  const cases: readonly (readonly [string, unknown, RegExp])[] = [
    ['codex', { from: '0.110', through: '0.130.0' }, /refusesStandard.*X\.Y\.Z/u],
    ['codex', { from: '0.110.0', through: 'latest' }, /refusesStandard.*X\.Y\.Z/u],
    ['codex', { from: '0.110.0' }, /refusesStandard.*X\.Y\.Z/u],
    ['codex', { from: '0.110.0', through: '0.130.0', why: 'x' }, /refusesStandard.*X\.Y\.Z/u],
    ['codex', { from: '0.130.0', through: '0.110.0' }, /refusesStandard ends before it starts/u],
    ['codex', { from: '0.110.0', through: '0.12.0' }, /refusesStandard ends before it starts/u],
    ['claude', { from: '0.110.0', through: '0.130.0' }, /refusesStandard.*only codex/u],
  ];
  for (const [runtime, range, said] of cases) {
    const run = generate(withRange(runtime, range));

    assert.equal(run.status, 1, `${runtime} ${JSON.stringify(range)} was accepted`);
    assert.match(run.stderr, said, `${runtime} ${JSON.stringify(range)}`);
  }
});

test('a range compares releases as numbers: 0.12.0 is below 0.110.0, so 0.12.0 to 0.110.0 is accepted', () => {
  assert.equal(generate(withRange('codex', { from: '0.12.0', through: '0.110.0' })).status, 0);
});
