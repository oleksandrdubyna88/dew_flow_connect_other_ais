// Regenerates src/runtimeCapabilities.generated.ts from shared/runtime-capabilities.json.
//
// The table belongs to neither half (PLAN_question_consultant.md, D3). coai-mcp EMBEDS it as a
// manifest resource and refuses a table that leaves a runtime × capability pair undecided; the panel
// cannot embed anything, because the settings tab is drawn before any server has been started — so
// the panel gets a generated copy, and `capabilityAdmission.test.ts` reads the seed itself and asserts
// the copy still matches, field for field. Both halves then answer the same
// `shared/capability-matrix-vectors.json`: A3's "blocked in the UI AND refused by the server" is one
// rule only while they do.
//
// WHAT A STALE COPY COSTS, said plainly: a pair the panel lets a person save and the server refuses —
// or, worse, admits — with nothing on either side saying so. Which is why `--check` is run by
// `generatedFilesAreCurrent.test.ts`: the seed-agreement test catches a file behind the SEED, and only
// `--check` catches a file behind the GENERATOR.
//
//   node scripts/generate-runtime-capabilities.mjs            writes the file
//   node scripts/generate-runtime-capabilities.mjs --check    writes nothing; exits 1 if it is behind
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// `here` comes from this file's own URL, so the working directory the script was started from
// changes nothing. `--seed=` and `--out=` exist for the tests, which drive a doctored fixture
// through the same validation the real seed goes through.
const here = dirname(fileURLToPath(import.meta.url));
const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

const defaultSeed = join(here, '..', '..', 'shared', 'runtime-capabilities.json');
const defaultOut = join(here, '..', 'src', 'runtimeCapabilities.generated.ts');
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);

if (!existsSync(seedPath)) {
  console.error(`${seedPath} is not there — this script generates the panel's capability table from it`);
  process.exit(1);
}

if (resolve(out) === resolve(seedPath)) {
  console.error(`--out resolves to the seed itself (${resolve(out)}) — that would overwrite the one `
    + 'file both halves of the product are generated from');
  process.exit(1);
}

let seed;
try {
  seed = JSON.parse(readFileSync(seedPath, 'utf8'));
} catch (error) {
  console.error(`${seedPath} could not be read as JSON — ${error.message}`);
  process.exit(1);
}

/**
 * What the file may say. The same five words and three capabilities the server's loader admits; a
 * field or a word this script does not know is REFUSED rather than dropped in silence, so a field
 * added for the server's loader becomes a deliberate act on both sides.
 */
const STANDINGS = ['confined', 'unconfined', 'default-deny', 'unsupported', 'unmeasured'];
const ROW_FIELDS = ['runtime', 'capability', 'standing', 'measuredWith'];
const MEASURED_FIELDS = ['cli', 'version', 'date', 'resultRef', 'cells', 'note'];

const runtimes = seed.runtimes ?? [];
const capabilities = seed.capabilities ?? [];
const rows = seed.rows ?? [];

function refuse(message) {
  console.error(`${seedPath}: ${message}`);
  process.exit(1);
}

if (!Array.isArray(runtimes) || runtimes.length === 0 || !Array.isArray(capabilities) || capabilities.length === 0) {
  refuse('names no runtimes or no capabilities — a generated empty table would block every row');
}
if (!Array.isArray(rows) || rows.length === 0) {
  refuse('names no rows — every pair would be undecided');
}

for (const r of rows) {
  const where = `row '${r.runtime}' × '${r.capability}'`;
  for (const field of Object.keys(r)) {
    if (!ROW_FIELDS.includes(field)) {
      refuse(`${where} has a field this generator does not know: '${field}'. Add it to the panel's RuntimeCapabilityRow and to this script, or take it out of the seed.`);
    }
  }
  if (!runtimes.includes(r.runtime)) {
    refuse(`${where} names a runtime outside the matrix (${runtimes.join(', ')})`);
  }
  if (!capabilities.includes(r.capability)) {
    refuse(`${where} names a capability outside the matrix (${capabilities.join(', ')})`);
  }
  if (!STANDINGS.includes(r.standing)) {
    refuse(`${where} has standing '${r.standing}', which neither half knows (${STANDINGS.join(', ')})`);
  }
  const m = r.measuredWith;
  if (typeof m !== 'object' || m === null) {
    refuse(`${where} has no measuredWith`);
  }
  for (const field of Object.keys(m)) {
    if (!MEASURED_FIELDS.includes(field)) {
      refuse(`${where}: measuredWith has a field this generator does not know: '${field}'`);
    }
  }
  for (const field of ['cli', 'version', 'date', 'resultRef', 'note']) {
    if (typeof m[field] !== 'string') {
      refuse(`${where}: measuredWith.${field} is not a string`);
    }
  }
  if (!Array.isArray(m.cells) || m.cells.some((c) => typeof c !== 'string')) {
    refuse(`${where}: measuredWith.cells is not an array of cell ids`);
  }
  if (m.cells.length === 0 && m.note.length === 0) {
    refuse(`${where} cites no cell and gives no note saying why`);
  }
}

// Every pair exactly once — a pair decided by nothing, or by two rows, is what the table exists to end.
for (const runtime of runtimes) {
  for (const capability of capabilities) {
    const count = rows.filter((r) => r.runtime === runtime && r.capability === capability).length;
    if (count !== 1) {
      refuse(`the pair '${runtime}' × '${capability}' has ${count} rows; every pair has exactly one`);
    }
  }
}

/**
 * A TypeScript string literal, single-quoted where that is unambiguous — the rest of this codebase
 * writes single quotes, and a generated file that does not read like its neighbours invites a hand
 * edit. JSON.stringify is the fallback, so a quote, a backslash or a line break cannot produce a file
 * that does not parse.
 */
const lit = (text) => (/['\\\r\n]/.test(text) ? JSON.stringify(text) : `'${text}'`);

const cells = (list) => (list.length === 0 ? '[]' : `[\n${list.map((c) => `        ${lit(c)},`).join('\n')}\n      ]`);

const row = (r) =>
  [
    '  {',
    `    runtime: ${lit(r.runtime)},`,
    `    capability: ${lit(r.capability)},`,
    `    standing: ${lit(r.standing)},`,
    '    measuredWith: {',
    `      cli: ${lit(r.measuredWith.cli)},`,
    `      version: ${lit(r.measuredWith.version)},`,
    `      date: ${lit(r.measuredWith.date)},`,
    `      resultRef: ${lit(r.measuredWith.resultRef)},`,
    `      cells: ${cells(r.measuredWith.cells)},`,
    `      note: ${lit(r.measuredWith.note)},`,
    '    },',
    '  },',
  ].join('\n');

const union = (words) => words.map(lit).join(' | ');

const file = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-runtime-capabilities.mjs\` from \`shared/runtime-capabilities.json\`,
// the table coai-mcp embeds (PLAN_question_consultant.md, D3). Edit the seed and run the script;
// \`capabilityAdmission.test.ts\` fails if this file and the seed disagree, and
// \`generatedFilesAreCurrent.test.ts\` fails if this file and the generator do.

/** The runtimes the question consultant can launch, in the file's order. */
export type QuestionRuntime = ${union(runtimes)};

/** What a prompt needs of its runtime. */
export type Capability = ${union(capabilities)};

/** What a measurement said about one runtime × capability pair. */
export type CapabilityStanding = ${union(STANDINGS)};

/** Which probe cells a row rests on, and where the record says it. */
export interface MeasuredWith {
  readonly cli: string;
  readonly version: string;
  readonly date: string;
  readonly resultRef: string;
  /** Cell ids of the full run; empty only when \`note\` says why. */
  readonly cells: readonly string[];
  readonly note: string;
}

export interface RuntimeCapabilityRow {
  readonly runtime: QuestionRuntime;
  readonly capability: Capability;
  readonly standing: CapabilityStanding;
  readonly measuredWith: MeasuredWith;
}

export const RUNTIMES: readonly QuestionRuntime[] = [${runtimes.map(lit).join(', ')}];

export const CAPABILITIES: readonly Capability[] = [${capabilities.map(lit).join(', ')}];

export const STANDINGS: readonly CapabilityStanding[] = [${STANDINGS.map(lit).join(', ')}];

/** One row per runtime × capability, in the file's order — every pair, decided by data. */
export const RUNTIME_CAPABILITIES: readonly RuntimeCapabilityRow[] = [
${rows.map(row).join('\n')}
];
`;

const counts = `${rows.length} rows over ${runtimes.length} runtimes × ${capabilities.length} capabilities`;

// The command to run when the file is behind — carrying the paths actually in use.
const rerun = [
  'node src_vs_code/scripts/generate-runtime-capabilities.mjs',
  ...(seedPath === defaultSeed ? [] : [`--seed=${seedPath}`]),
  ...(out === defaultOut ? [] : [`--out=${out}`]),
].join(' ');

if (process.argv.includes('--check')) {
  if (!existsSync(out)) {
    console.error(`${out} does not exist — run: ${rerun}`);
    process.exit(1);
  }
  // Line endings normalised on both sides: the committed file is LF, a Windows checkout may hold
  // CRLF, and that is not the drift this is looking for.
  const committed = readFileSync(out, 'utf8').replace(/\r\n/g, '\n');
  if (committed !== file.replace(/\r\n/g, '\n')) {
    console.error(`${out} is not what this script produces — run: ${rerun}`);
    process.exit(1);
  }
  console.log(`up to date: ${out} (${counts})`);
} else {
  // Written beside the target and renamed over it: a process killed mid-write would otherwise leave
  // a truncated file that every build imports, with the last good copy already gone.
  mkdirSync(dirname(out), { recursive: true });
  const staging = `${out}.tmp`;
  writeFileSync(staging, file, 'utf8');
  renameSync(staging, out);
  console.log(`wrote ${out}: ${counts}`);
}
