// Regenerates src/commandTexts.generated.ts from shared/commands/*.md (issue #467, Epic B).
//
// The gate's command texts belong to neither half. coai-mcp EMBEDS the folder; the Edit commands page
// is drawn before any server answers, and it shows each shipped text as the placeholder of the box a
// person overrides it in — so the page gets a generated copy, the same bargain the role catalog makes
// (`generate-builtin-roles.mjs`). `commandTexts.test.ts` reads the folder itself and asserts the copy
// still matches, which is what fails when somebody edits a text and forgets to run this.
//
//   node scripts/generate-command-texts.mjs            writes the file
//   node scripts/generate-command-texts.mjs --check    writes nothing; exits 1 if it is behind
//
// The bodies are written with JSON.stringify, never as template literals: a backtick or a `${` in a
// text would otherwise end the literal (prepare-gate.mjs says how that broke a build once).
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, '..', '..', 'shared', 'commands');
const out = join(here, '..', 'src', 'commandTexts.generated.ts');

if (!existsSync(dir)) {
  console.error(`${dir} is not there — this script generates the commands page's texts from it`);
  process.exit(1);
}

const ids = readdirSync(dir).filter((name) => name.endsWith('.md')).map((name) => name.slice(0, -3)).sort();
if (ids.length === 0) {
  throw new Error(`${dir} holds no command texts — a generated empty map would draw an empty page`);
}

// The server trims the file's trailing newline and nothing else; so does this.
const entries = ids.map((id) => {
  const text = readFileSync(join(dir, `${id}.md`), 'utf8').replace(/[\r\n]+$/, '');
  if (text.trim().length === 0) {
    throw new Error(`${dir}/${id}.md is empty — a shipped order must say something`);
  }

  return `  ${JSON.stringify(id)}: ${JSON.stringify(text)},`;
});

const file = `// GENERATED FILE — do not edit by hand.
//
// Written by \`node scripts/generate-command-texts.mjs\` from \`shared/commands/*.md\`, the texts coai-mcp
// embeds. Edit a text there and run the script; \`commandTexts.test.ts\` fails if this file and the
// folder disagree.

/** The gate's shipped command texts, by id — what an override replaces and Restore brings back. */
export const SHIPPED_COMMAND_TEXTS: Readonly<Record<string, string>> = {
${entries.join('\n')}
};
`;

const rerun = 'node src_vs_code/scripts/generate-command-texts.mjs';

if (process.argv.includes('--check')) {
  if (!existsSync(out)) {
    console.error(`${out} does not exist — run: ${rerun}`);
    process.exit(1);
  }
  const committed = readFileSync(out, 'utf8').replace(/\r\n/g, '\n');
  if (committed !== file) {
    console.error(`${out} is not what this script produces — run: ${rerun}`);
    process.exit(1);
  }
  console.log(`up to date: ${out} (${ids.length} texts)`);
} else {
  mkdirSync(dirname(out), { recursive: true });
  const staging = `${out}.tmp`;
  writeFileSync(staging, file, 'utf8');
  renameSync(staging, out);
  console.log(`wrote ${out}: ${ids.length} texts`);
}
