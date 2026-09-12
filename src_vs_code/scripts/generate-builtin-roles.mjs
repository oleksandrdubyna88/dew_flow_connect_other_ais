// Regenerates src/builtinRoles.generated.ts from shared/builtin-roles.json.
//
// The seed belongs to neither half. coai-mcp EMBEDS it as a manifest resource; the panel cannot
// embed anything, because it is drawn before any server has been started — a settings page that
// cannot list its own choices until a subprocess answers shows an empty box on first open. So the
// panel gets a generated copy, and `builtinRoleCatalog.test.ts` reads the seed itself and asserts
// the copy still matches. That test is also what fails when somebody edits the seed and forgets to
// run this.
//
// What this replaces is worth naming: the two catalogs used to be held together by a test that
// parsed C# SOURCE with a regular expression. It broke on every reformat and could not see a field
// it had not been taught, which is how two lists of twenty-five prompts stay level for a while and
// then quietly do not.
//
//   node scripts/generate-builtin-roles.mjs            writes the file
//   node scripts/generate-builtin-roles.mjs --check    writes nothing; exits 1 if it is behind
//
// `--check` is what `generatedFilesAreCurrent.test.ts` runs. Without it the committed file could
// only be wrong in one direction: the seed-agreement test catches a file behind the SEED, and
// nothing caught a file behind the GENERATOR — a changed mapping leaves the committed file matching
// the seed, every test green, and the next regeneration silently changing the panel's catalog.
// (codex, this story's plan round.)
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// `here` comes from this file's own URL, so every path below is absolute and the working directory
// the script was started from changes nothing. `--seed=` and `--out=` exist for the tests, which
// drive a doctored fixture through the same validation the real seed goes through.
const here = dirname(fileURLToPath(import.meta.url));
const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

const defaultSeed = join(here, '..', '..', 'shared', 'builtin-roles.json');
const defaultOut = join(here, '..', 'src', 'builtinRoles.generated.ts');
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);

/** The stages the seed may name. A third one is a deliberate change here, not a silent remapping. */
const STAGES = ['plan', 'result'];

// A missing seed is the one failure a fresh or half-deleted checkout actually produces, and
// readFileSync's ENOENT stack trace says the path without saying what it was for.
if (!existsSync(seedPath)) {
  console.error(`${seedPath} is not there — this script generates the panel's catalog from it`);
  process.exit(1);
}

const seed = JSON.parse(readFileSync(seedPath, 'utf8'));
const roles = seed.roles ?? [];
if (roles.length === 0) {
  throw new Error(`${seedPath} names no roles — a generated empty catalog would draw an empty panel`);
}

// A field this script silently renders as `undefined` is a panel drawing a role with no name, which
// typechecks and looks like a bug in the panel. Refuse at the source instead. (local, plan round.)
for (const r of roles) {
  for (const field of ['id', 'name', 'stage']) {
    if (typeof r[field] !== 'string' || r[field].length === 0) {
      throw new Error(`${seedPath}: a role has no ${field} — ${JSON.stringify(r).slice(0, 120)}`);
    }
  }
  // A stage this build has no mapping for must STOP here. `prompts.ts` turns anything that is not
  // `plan` into the panel's `code`, which is right for the two stages that exist and silently wrong
  // for the document stage a later plan adds — the panel would draw it among the code roles and
  // nothing would say so. (codex and gemini, this story's code round.)
  if (!STAGES.includes(r.stage)) {
    throw new Error(
      `${seedPath}: role '${r.id}' is of stage '${r.stage}', which this panel has no mapping for `
      + `(it knows ${STAGES.join(' and ')}) — add the mapping in prompts.ts before adding the stage here`,
    );
  }
  // Absent would render as `false`, quietly excluding the role from every programming-task round.
  if (typeof r.programmingTask !== 'boolean') {
    throw new Error(`${seedPath}: role '${r.id}' does not say whether it is a programming task`);
  }
  if (!Array.isArray(r.prompts) || r.prompts.length === 0) {
    throw new Error(`${seedPath}: role '${r.id}' has no prompts, so it has no general prompt either`);
  }
  for (const p of r.prompts) {
    for (const field of ['id', 'label', 'purpose']) {
      if (typeof p[field] !== 'string' || p[field].length === 0) {
        throw new Error(`${seedPath}: prompt '${p.id}' of role '${r.id}' has no ${field}`);
      }
    }
  }
}

/**
 * A TypeScript string literal, single-quoted where that is unambiguous.
 *
 * The rest of this codebase writes single quotes, and a generated file that does not read like its
 * neighbours invites somebody to "fix" it by hand. JSON.stringify is the fallback rather than the
 * rule, so an apostrophe or a backslash cannot produce a file that does not parse.
 */
const lit = (text) => (/['\\\r\n]/.test(text) ? JSON.stringify(text) : `'${text}'`);

const prompt = (p) =>
  `    { id: ${lit(p.id)}, label: ${lit(p.label)}, purpose: ${lit(p.purpose)} },`;

const role = (r) =>
  [
    '  {',
    `    id: ${lit(r.id)},`,
    `    name: ${lit(r.name)},`,
    `    stage: ${lit(r.stage)},`,
    `    programmingTask: ${r.programmingTask},`,
    '    prompts: [',
    ...(r.prompts ?? []).map((p) => `  ${prompt(p)}`),
    '    ],',
    '  },',
  ].join('\n');

const file = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-builtin-roles.mjs\` from \`shared/builtin-roles.json\`, the seed
// coai-mcp embeds. Edit the seed and run the script; \`builtinRoleCatalog.test.ts\` fails if this
// file and the seed disagree.

import type { RoleDefinition } from './prompts';

/** The roles this product ships, in the order a round runs them and the panel draws them. */
export const BUILTIN_ROLES: readonly RoleDefinition[] = [
${roles.map(role).join('\n')}
];
`;

const counts = `${roles.length} roles, ${roles.reduce((n, r) => n + (r.prompts?.length ?? 0), 0)} prompts`;

// The command to run when the file is behind — carrying the paths actually in use, because a
// --check over a fixture that suggests regenerating the DEFAULT file leaves the checked target
// exactly as it was. (codex, story C2's code round.)
const rerun = [
  'node src_vs_code/scripts/generate-builtin-roles.mjs',
  ...(seedPath === defaultSeed ? [] : [`--seed=${seedPath}`]),
  ...(out === defaultOut ? [] : [`--out=${out}`]),
].join(' ');

if (process.argv.includes('--check')) {
  // A file that is not there is behind by definition, and saying so beats a readFileSync stack
  // trace on a fresh checkout. (codex, this story's code round.)
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
