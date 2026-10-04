// Regenerates src/featureAvailability.generated.ts from shared/feature-availability.json.
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
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);

function refuse(message) {
  console.error(`${seedPath}: ${message}`);
  process.exit(1);
}

if (!existsSync(seedPath)) {
  refuse('is not there — this script generates the panel\'s feature availability from it');
}
if (resolve(out) === resolve(seedPath)) {
  refuse(`--out resolves to the seed itself (${resolve(out)})`);
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
const SEED_FIELDS = ['why', 'runtimes', 'features', 'effort'];
const FEATURES = ['consultant', 'chat'];
const EFFORT_FIELDS = ['runtime', 'source', 'levels', 'measuredWith', 'note'];
const SOURCES = ['list', 'probe', 'unmeasured', 'none'];
const LEVEL = /^[a-z][a-z0-9-]{0,31}$/u;

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
`;

const counts = `${features.consultant.length} consulting, ${features.chat.length} chat, ${effort.length} effort rows`;
const rerun = [
  'node src_vs_code/scripts/generate-feature-availability.mjs',
  ...(seedPath === defaultSeed ? [] : [`--seed=${seedPath}`]),
  ...(out === defaultOut ? [] : [`--out=${out}`]),
].join(' ');

if (process.argv.includes('--check')) {
  if (!existsSync(out)) {
    console.error(`${out} does not exist — run: ${rerun}`);
    process.exit(1);
  }
  // Line endings normalised on both sides: a Windows checkout may hold CRLF, which is not drift.
  if (readFileSync(out, 'utf8').replace(/\r\n/gu, '\n') !== file) {
    console.error(`${out} is not what this script produces — run: ${rerun}`);
    process.exit(1);
  }
  console.log(`up to date: ${out} (${counts})`);
} else {
  // Written beside the target and renamed over it, so a killed process never leaves a truncated file.
  mkdirSync(dirname(out), { recursive: true });
  const staging = `${out}.tmp`;
  writeFileSync(staging, file, 'utf8');
  renameSync(staging, out);
  console.log(`wrote ${out}: ${counts}`);
}
