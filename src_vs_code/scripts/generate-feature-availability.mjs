// Regenerates src/featureAvailability.generated.ts and src/fastMode.generated.ts from shared/feature-availability.json.
//
// The file belongs to neither half (PLAN_one_model_catalog.md, D4). The panel cannot read it at run time —
// the settings tab is drawn before any server has started — so it gets a generated copy, and
// `featureAvailability.test.ts` reads the seed itself and asserts the copy still matches. `--check` is run
// by `generatedFilesAreCurrent.test.ts`, which catches a file behind the GENERATOR rather than the seed.
//
// What a stale copy costs: a picker offering a runtime the feature cannot run on, or an effort the CLI
// refuses — with nothing on either side saying so.
//
//   node scripts/generate-feature-availability.mjs            writes the file
//   node scripts/generate-feature-availability.mjs --check    writes nothing; exits 1 if it is behind
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

const defaultSeed = join(here, '..', '..', 'shared', 'feature-availability.json');
const defaultOut = join(here, '..', 'src', 'featureAvailability.generated.ts');
const defaultFastOut = join(here, '..', 'src', 'fastMode.generated.ts');
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);
const fastOut = flag('fast-out', defaultFastOut);

function refuse(message) {
  console.error(`${seedPath}: ${message}`);
  process.exit(1);
}

if (!existsSync(seedPath)) {
  refuse('is not there — this script generates the panel\'s feature availability from it');
}
for (const [name, target] of [['--out', out], ['--fast-out', fastOut]]) {
  if (resolve(target) === resolve(seedPath)) {
    refuse(`${name} resolves to the seed itself (${resolve(target)})`);
  }
}

let seed;
try {
  seed = JSON.parse(readFileSync(seedPath, 'utf8'));
} catch (error) {
  refuse(`could not be read as JSON — ${error.message}`);
}

/**
 * What the file may say. A field or a word this script does not know is REFUSED rather than dropped, so a
 * field added for coai-mcp's loader becomes a deliberate act on both sides.
 */
const SEED_FIELDS = ['why', 'runtimes', 'features', 'effort', 'thinking', 'fastMode'];
const FEATURES = ['consultant', 'chat'];
const EFFORT_FIELDS = ['runtime', 'source', 'levels', 'measuredWith', 'note'];
const SOURCES = ['list', 'probe', 'unmeasured', 'none'];
const LEVEL = /^[a-z][a-z0-9-]{0,31}$/u;
// Thinking (D12): whether a runtime has an on/off switch — no `list`: a switch has two positions, not levels.
const THINKING_FIELDS = ['runtime', 'source', 'note'];
const THINKING_SOURCES = ['probe', 'unmeasured', 'none'];
// Fast mode (research/PLAN_fast_mode.md): a tier on every model of the runtime, on the listed models only, or none.
const FAST_FIELDS = ['runtime', 'source', 'models', 'measuredWith', 'note', 'refusesStandard'];
const FAST_SOURCES = ['every-model', 'models', 'none'];
// The codex releases that refuse to be told the standard tier (research/PLAN_codex_tier_floor.md): `{ from, through }`, both
// ends included, each a release as `codex --version` prints it. coai-mcp reads it (`FeatureAvailability.RangeOf`) and
// checks it exactly as below; this half does not use it yet, and does not generate it — it only refuses a range the
// server would refuse, so neither half can ship a file the other cannot start with.
const RANGE_FIELDS = ['from', 'through'];
const RELEASE = /^[0-9]+\.[0-9]+\.[0-9]+$/u;

/**
 * The effort rules, in the order they are checked: what must hold, and what is said when it does not. A table rather
 * than a run of ifs so the check stays one decision however many rules it gains (PR #681's code round); the first
 * rule that fails is the refusal, as before — `refuse` exits — so a later rule may rely on an earlier one.
 */
const EFFORT_RULES = [
  [(row) => unknown(row, EFFORT_FIELDS) === undefined, (row) => ` has a field this generator does not know: '${unknown(row, EFFORT_FIELDS)}'`],
  [(row) => runtimes.includes(row.runtime), () => ` is not one of the runtimes (${runtimes.join(', ')})`],
  [(row) => SOURCES.includes(row.source), (row) => ` has source '${row.source}'; the sources are ${SOURCES.join(', ')}`],
  [(row) => strings(row.levels) && row.levels.every((level) => LEVEL.test(level)), () => ': levels is not a list of lower-case words'],
  [(row) => typeof row.measuredWith === 'string' && typeof row.note === 'string', () => ': measuredWith and note are strings'],
  [(row) => (row.source === 'list') === (row.levels.length > 0), () => ": a 'list' source carries its levels, and every other source carries none"],
  [(row) => row.source !== 'list' || row.measuredWith.length > 0, () => ': a listed effort says where it was read (measuredWith)'],
  [(row) => row.source === 'list' || row.note.length > 0, () => ': a runtime with no list says why (note)'],
  [(row) => row.runtime !== 'antigravity' || row.source === 'none', () => ": antigravity takes no effort (the operator's ruling, 2026-10-04)"],
];

/** The fast-mode rules, as a table for the reason the effort rules are one. */
const FAST_RULES = [
  [(row) => unknown(row, FAST_FIELDS) === undefined, (row) => ` has a field this generator does not know: '${unknown(row, FAST_FIELDS)}'`],
  [(row) => runtimes.includes(row.runtime), () => ` is not one of the runtimes (${runtimes.join(', ')})`],
  [(row) => FAST_SOURCES.includes(row.source), (row) => ` has source '${row.source}'; the sources are ${FAST_SOURCES.join(', ')}`],
  [(row) => strings(row.models) && typeof row.measuredWith === 'string' && typeof row.note === 'string' && row.note.length > 0, () => ': models is a list, measuredWith a string, and the note says why'],
  [(row) => (row.source === 'models') === (row.models.length > 0), () => ": a 'models' source lists its models, and every other source lists none"],
  [(row) => row.refusesStandard === undefined || row.runtime === 'codex', () => ': refusesStandard is a range only codex may carry — the one runtime told a tier it can refuse'],
  [(row) => row.refusesStandard === undefined || isRange(row.refusesStandard), () => ': refusesStandard is { from, through }, each a release (X.Y.Z)'],
  [(row) => row.refusesStandard === undefined || notAfter(row.refusesStandard.from, row.refusesStandard.through), () => ': refusesStandard ends before it starts'],
];

/** A `{ from, through }` object and nothing else, each end a release. */
const isRange = (range) => typeof range === 'object' && range !== null && unknown(range, RANGE_FIELDS) === undefined
  && RANGE_FIELDS.every((end) => typeof range[end] === 'string' && RELEASE.test(range[end]));

/**
 * Whether release `a` is not after release `b`, compared as NUMBERS part by part — `0.12.0` is below `0.110.0`, which a
 * string comparison gets backwards. Equal releases are not after each other.
 */
function notAfter(a, b) {
  const [left, right] = [a.split('.').map(Number), b.split('.').map(Number)];
  const differs = left.findIndex((part, at) => part !== right[at]);

  return differs < 0 || left[differs] < right[differs];
}

const unknown = (fields, known) => Object.keys(fields).find((field) => !known.includes(field));
const strings = (list) => Array.isArray(list) && list.every((one) => typeof one === 'string');

const extraField = unknown(seed, SEED_FIELDS);
if (extraField !== undefined) {
  refuse(`has a field this generator does not know: '${extraField}'`);
}
const runtimes = seed.runtimes;
if (!strings(runtimes) || runtimes.length === 0) {
  refuse('names no runtimes');
}
const features = seed.features ?? {};
const extraFeature = unknown(features, FEATURES);
if (extraFeature !== undefined) {
  refuse(`names a feature this generator does not know: '${extraFeature}'`);
}
for (const feature of FEATURES) {
  const list = features[feature];
  if (!strings(list) || list.length === 0) {
    refuse(`features.${feature} names no runtime`);
  }
  const stranger = list.find((runtime) => !runtimes.includes(runtime));
  if (stranger !== undefined) {
    refuse(`features.${feature} names '${stranger}', which is not one of the runtimes (${runtimes.join(', ')})`);
  }
}

const effort = seed.effort;
if (!Array.isArray(effort)) {
  refuse('has no effort rows');
}
for (const row of effort) {
  checkEffortRow(row);
}
for (const runtime of runtimes) {
  const count = effort.filter((row) => row.runtime === runtime).length;
  if (count !== 1) {
    refuse(`runtime '${runtime}' has ${count} effort rows; every runtime has exactly one`);
  }
}

const thinking = seed.thinking;
if (!Array.isArray(thinking)) {
  refuse('has no thinking rows');
}
for (const row of thinking) {
  checkThinkingRow(row);
}
for (const runtime of runtimes) {
  const count = thinking.filter((row) => row.runtime === runtime).length;
  if (count !== 1) {
    refuse(`runtime '${runtime}' has ${count} thinking rows; every runtime has exactly one`);
  }
}

const fastMode = seed.fastMode;
if (!Array.isArray(fastMode)) {
  refuse('has no fastMode rows');
}
for (const row of fastMode) {
  checkFastRow(row);
}
for (const runtime of runtimes) {
  const count = fastMode.filter((row) => row.runtime === runtime).length;
  if (count !== 1) {
    refuse(`runtime '${runtime}' has ${count} fastMode rows; every runtime has exactly one`);
  }
}

/** One fast-mode row: known fields, runtime and source, models only for `models`, and always a reason. */
function checkFastRow(row) {
  const broken = FAST_RULES.find(([holds]) => !holds(row));
  if (broken !== undefined) {
    refuse(`fastMode row '${row.runtime}'${broken[1](row)}`);
  }
}

/** One thinking row: known fields, a known runtime and source, and always a reason a card can show. */
function checkThinkingRow(row) {
  const stranger = unknown(row, THINKING_FIELDS);
  const broken = stranger !== undefined ? ` has a field this generator does not know: '${stranger}'`
    : !runtimes.includes(row.runtime) ? ` is not one of the runtimes (${runtimes.join(', ')})`
      : !THINKING_SOURCES.includes(row.source) ? ` has source '${row.source}'; the sources are ${THINKING_SOURCES.join(', ')}`
        : typeof row.note !== 'string' || row.note.length === 0 ? ': every thinking row says why (note), which the card shows' : '';
  if (broken.length > 0) {
    refuse(`thinking row '${row.runtime}'${broken}`);
  }
}

/** One effort row: known fields, a known source, levels only for `list`, and a reason where there are none. */

function checkEffortRow(row) {
  const broken = EFFORT_RULES.find(([holds]) => !holds(row));
  if (broken !== undefined) {
    refuse(`effort row '${row.runtime}'${broken[1](row)}`);
  }
}

/** A TypeScript string literal, single-quoted where unambiguous, as the rest of this codebase writes them. */
const lit = (text) => (/['\\\r\n]/u.test(text) ? JSON.stringify(text) : `'${text}'`);
const list = (words) => `[${words.map(lit).join(', ')}]`;

const effortRow = (row) => [
  '  {',
  `    runtime: ${lit(row.runtime)},`,
  `    source: ${lit(row.source)},`,
  `    levels: ${list(row.levels)},`,
  `    measuredWith: ${lit(row.measuredWith)},`,
  `    note: ${lit(row.note)},`,
  '  },',
].join('\n');

const fastRow = (row) => [
  '  {',
  `    runtime: ${lit(row.runtime)},`,
  `    source: ${lit(row.source)},`,
  `    models: ${list(row.models)},`,
  `    measuredWith: ${lit(row.measuredWith)},`,
  `    note: ${lit(row.note)},`,
  '  },',
].join('\n');

const thinkingRow = (row) => [
  '  {',
  `    runtime: ${lit(row.runtime)},`,
  `    source: ${lit(row.source)},`,
  `    note: ${lit(row.note)},`,
  '  },',
].join('\n');

const file = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-feature-availability.mjs\` from \`shared/feature-availability.json\`
// (PLAN_one_model_catalog.md, D4). Edit the seed and run the script; \`featureAvailability.test.ts\` fails
// if this file and the seed disagree, and \`generatedFilesAreCurrent.test.ts\` fails if this file and the
// generator do.
import type { Runtime } from './models';

/** Where a runtime's legal efforts come from. */
export type EffortSource = ${SOURCES.map(lit).join(' | ')};

/** One runtime's efforts: the levels when \`source\` is \`list\`, and why when it is not. */
export interface EffortRow {
  readonly runtime: Runtime;
  readonly source: EffortSource;
  readonly levels: readonly string[];
  readonly measuredWith: string;
  readonly note: string;
}

/** The runtimes a consultant can run on, in the file's order. */
export const CONSULTING: readonly Runtime[] = ${list(features.consultant)};

/** The runtimes a chat can speak to, in the file's order. */
export const CHAT: readonly Runtime[] = ${list(features.chat)};

/** One row per runtime. */
export const EFFORT: readonly EffortRow[] = [
${effort.map(effortRow).join('\n')}
];

/** Whether a runtime has a thinking switch (D12): \`probe\` asks the model's report, the others say why there is none. */
export type ThinkingSource = ${THINKING_SOURCES.map(lit).join(' | ')};

/** One runtime's thinking switch — and, always, the sentence a card shows when there is none. */
export interface ThinkingRow {
  readonly runtime: Runtime;
  readonly source: ThinkingSource;
  readonly note: string;
}

/** One row per runtime. */
export const THINKING: readonly ThinkingRow[] = [
${thinking.map(thinkingRow).join('\n')}
];
`;

// The fast-mode rows in a file of their OWN, importing nothing: the stored field (\`catalogFields\`) asks the tier rule,
// and \`vendors\` imports \`catalogFields\` — so a rule reached through the main file, which imports \`models\`, closed a
// new import cycle (the fast-mode code round, \`importCycles.test.mjs\`). \`runtime\` is a plain string here for the same
// reason; \`fastModeIsData.test.ts\` holds every row to a runtime the seed names.
const fastFile = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-feature-availability.mjs\` from the \`fastMode\` block of
// \`shared/feature-availability.json\` (research/PLAN_fast_mode.md). Edit the seed and run the script;
// \`generatedFilesAreCurrent.test.ts\` fails if this file and the generator disagree. It imports nothing, on purpose.

/** Where a runtime's fast tier is (research/PLAN_fast_mode.md): on every model, on the listed models, or none. */
export type FastSource = ${FAST_SOURCES.map(lit).join(' | ')};

/** One runtime's fast tier — and, always, why. */
export interface FastModeRow {
  readonly runtime: string;
  readonly source: FastSource;
  readonly models: readonly string[];
  readonly measuredWith: string;
  readonly note: string;
}

/** One row per runtime. */
export const FAST_MODE: readonly FastModeRow[] = [
${fastMode.map(fastRow).join('\n')}
];
`;

const counts = `${features.consultant.length} consulting, ${features.chat.length} chat, ${effort.length} effort rows, ${thinking.length} thinking rows, ${fastMode.length} fast-mode rows`;
const rerun = [
  'node src_vs_code/scripts/generate-feature-availability.mjs',
  ...(seedPath === defaultSeed ? [] : [`--seed=${seedPath}`]),
  ...(out === defaultOut ? [] : [`--out=${out}`]),
  ...(fastOut === defaultFastOut ? [] : [`--fast-out=${fastOut}`]),
].join(' ');

/** Whether one target holds exactly what this script produces — line endings set aside, as CRLF is not drift. */
function current(target, text) {
  if (!existsSync(target)) {
    console.error(`${target} does not exist — run: ${rerun}`);

    return false;
  }
  if (readFileSync(target, 'utf8').replace(/\r\n/gu, '\n') !== text) {
    console.error(`${target} is not what this script produces — run: ${rerun}`);

    return false;
  }

  return true;
}

/** Written beside the target and renamed over it, so a killed process never leaves a truncated file. */
function write(target, text) {
  mkdirSync(dirname(target), { recursive: true });
  const staging = `${target}.tmp`;
  writeFileSync(staging, text, 'utf8');
  renameSync(staging, target);
}

if (process.argv.includes('--check')) {
  const both = [current(out, file), current(fastOut, fastFile)];
  if (both.includes(false)) {
    process.exit(1);
  }
  console.log(`up to date: ${out}, ${fastOut} (${counts})`);
} else {
  write(out, file);
  write(fastOut, fastFile);
  console.log(`wrote ${out}, ${fastOut}: ${counts}`);
}
