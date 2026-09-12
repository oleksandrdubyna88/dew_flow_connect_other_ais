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
//   node scripts/generate-builtin-roles.mjs
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

writeFileSync(out, file, 'utf8');
console.log(`wrote ${out}: ${roles.length} roles, ${roles.reduce((n, r) => n + (r.prompts?.length ?? 0), 0)} prompts`);
