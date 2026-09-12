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
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const seedPath = join(here, '..', '..', 'shared', 'builtin-roles.json');
const out = join(here, '..', 'src', 'builtinRoles.generated.ts');

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
    `    programmingTask: ${r.programmingTask === true},`,
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

if (process.argv.includes('--check')) {
  // Line endings normalised on both sides: the committed file is LF, a Windows checkout may hold
  // CRLF, and that is not the drift this is looking for.
  const committed = readFileSync(out, 'utf8').replace(/\r\n/g, '\n');
  if (committed !== file.replace(/\r\n/g, '\n')) {
    console.error(`${out} is not what this script produces — run: node scripts/generate-builtin-roles.mjs`);
    process.exit(1);
  }
  console.log(`up to date: ${out} (${counts})`);
} else {
  writeFileSync(out, file, 'utf8');
  console.log(`wrote ${out}: ${counts}`);
}
