// Regenerates src/questionPrompts.generated.ts from shared/question-prompts.json.
//
// The three base prompts the question consultant ships with belong to neither half
// (todo/PLAN_question_consultant.md, S2): coai-mcp EMBEDS the file (`QuestionPromptSet.Shipped`), and the
// panel gets a generated copy so the Question consultant tab can SHOW the shipped text — which is what makes
// Restore default put the same words back on both halves. `questionPrompts.test.ts` reads the seed and
// asserts the copy matches it field for field; `generatedFilesAreCurrent.test.ts` runs `--check`, which is
// the only thing that sees a copy behind the GENERATOR rather than behind the seed.
//
//   node scripts/generate-question-prompts.mjs            writes the file
//   node scripts/generate-question-prompts.mjs --check    writes nothing; exits 1 if it is behind
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

const defaultSeed = join(here, '..', '..', 'shared', 'question-prompts.json');
const defaultOut = join(here, '..', 'src', 'questionPrompts.generated.ts');
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);

function refuse(message) {
  console.error(`${seedPath}: ${message}`);
  process.exit(1);
}

if (!existsSync(seedPath)) {
  refuse('is not there — this script generates the panel’s shipped question prompts from it');
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

/** The server's own rules (`QuestionPromptSet.ShippedRow`): an override-file id, one of three capabilities, a title and a text. */
const CAPABILITIES = ['none', 'disk', 'web'];
const FIELDS = ['id', 'title', 'capability', 'text'];
const prompts = seed.prompts ?? [];
if (!Array.isArray(prompts) || prompts.length === 0) {
  refuse('ships no prompt at all');
}
for (const p of prompts) {
  for (const field of Object.keys(p)) {
    if (!FIELDS.includes(field)) {
      refuse(`the prompt '${p.id}' has a field this generator does not know: '${field}'`);
    }
  }
  if (typeof p.id !== 'string' || !/^[a-z0-9][a-z0-9-]*$/.test(p.id)) {
    refuse(`the prompt '${p.id}' has an id an override file cannot be named by`);
  }
  if (!CAPABILITIES.includes(p.capability)) {
    refuse(`the prompt '${p.id}' declares the capability '${p.capability}', outside ${CAPABILITIES.join(', ')}`);
  }
  if (typeof p.title !== 'string' || p.title.trim().length === 0 || typeof p.text !== 'string' || p.text.trim().length === 0) {
    refuse(`the prompt '${p.id}' has no title or no text`);
  }
}

const lit = (text) => (/['\\\r\n]/.test(text) ? JSON.stringify(text) : `'${text}'`);

const file = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-question-prompts.mjs\` from \`shared/question-prompts.json\`, the
// prompts coai-mcp embeds (todo/PLAN_question_consultant.md, S2). Edit the seed and run the script;
// \`questionPrompts.test.ts\` fails if this file and the seed disagree, and
// \`generatedFilesAreCurrent.test.ts\` fails if this file and the generator do.

/** One shipped base prompt: the override file's name, its title, the capability it needs, the shipped text. */
export interface ShippedQuestionPrompt {
  readonly id: string;
  readonly title: string;
  readonly capability: 'none' | 'disk' | 'web';
  readonly text: string;
}

export const SHIPPED_QUESTION_PROMPTS: readonly ShippedQuestionPrompt[] = [
${prompts.map((p) => `  {\n    id: ${lit(p.id)},\n    title: ${lit(p.title.trim())},\n    capability: ${lit(p.capability)},\n    text: ${lit(p.text.trim())},\n  },`).join('\n')}
];
`;

const rerun = [
  'node src_vs_code/scripts/generate-question-prompts.mjs',
  ...(seedPath === defaultSeed ? [] : [`--seed=${seedPath}`]),
  ...(out === defaultOut ? [] : [`--out=${out}`]),
].join(' ');

if (process.argv.includes('--check')) {
  if (!existsSync(out)) {
    console.error(`${out} does not exist — run: ${rerun}`);
    process.exit(1);
  }
  const committed = readFileSync(out, 'utf8').replace(/\r\n/g, '\n');
  if (committed !== file) {
    console.error(`${out} is behind the generator — run: ${rerun}`);
    process.exit(1);
  }
  console.log(`${out} is current (${prompts.length} prompts).`);
  process.exit(0);
}

mkdirSync(dirname(out), { recursive: true });
const temp = `${out}.${process.pid}.tmp`;
writeFileSync(temp, file);
renameSync(temp, out);
console.log(`wrote ${out} (${prompts.length} prompts).`);
