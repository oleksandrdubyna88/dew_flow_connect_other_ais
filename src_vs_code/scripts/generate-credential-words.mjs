// Regenerates src/credentialWords.generated.ts from shared/credential-words.json.
//
// The list belongs to neither half. `coai-mcp` EMBEDS it as a manifest resource and the extension
// gets a generated copy, exactly as `shared/builtin-roles.json` is already handled — and for a
// sharper reason than the catalog has. Both halves REDACT before anything reaches disk, and they
// must redact on the same words: two redactors disagreeing about whether `sig` names a credential
// is not a test failure anywhere, it is a secret in a file on one path and not the other.
//
// WHY NEITHER SIDE READS THE FILE AT RUN TIME. The plan's first draft had both of them do exactly
// that, and two reviewers answered independently that a published Native-AOT binary and an
// installed VSIX both run where no `shared/` directory exists — the read throws, and because every
// notice write is best-effort and swallows its exceptions, the redaction would then run with an
// EMPTY LIST and put raw credentials into `server-notices.jsonl`. A security measure that degrades
// to nothing on the one machine that matters is worse than not having it. So: build time on both
// sides, and both fail closed.
//
//   node scripts/generate-credential-words.mjs            writes the file
//   node scripts/generate-credential-words.mjs --check    writes nothing; exits 1 if it is behind
//
// `--check` is what `generatedFilesAreCurrent.test.ts` runs, and it is the guard that the C# side
// does not need: the server embeds the JSON itself, so it has no second copy to drift from, while
// this side's module is a COPY and can go stale in silence.
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

const defaultSeed = join(here, '..', '..', 'shared', 'credential-words.json');
const defaultOut = join(here, '..', 'src', 'credentialWords.generated.ts');
const seedPath = flag('seed', defaultSeed);
const out = flag('out', defaultOut);

if (!existsSync(seedPath)) {
  console.error(`${seedPath} is not there — this script generates the extension's word list from it`);
  process.exit(1);
}

// `--out` resolving onto the seed would rename generated TypeScript over the source of truth, and
// the next run could not parse it. `existsSync` cannot see that the two paths are the same file
// when one is spelled differently, so both are resolved before they are compared.
if (resolve(out) === resolve(seedPath)) {
  console.error(`--out resolves to the seed itself (${resolve(out)}) — that would overwrite the one `
    + 'file both halves of the product are generated from');
  process.exit(1);
}

// Every other failure in this script names the file and says what to do; a raw `readFileSync` or
// `JSON.parse` throw would be the one that does not. A directory where the seed should be, and a
// trailing comma somebody left behind, arrive here identically.
let seed;
try {
  seed = JSON.parse(readFileSync(seedPath, 'utf8'));
} catch (reason) {
  console.error(`${seedPath} could not be read as JSON: ${reason.message}`);
  console.error('Repair that file — it is the source of truth both halves of the product redact on.');
  process.exit(1);
}

/**
 * One list, validated. An empty one is refused HERE as well as at the server's loader, because a
 * generated empty array is a redactor that redacts nothing — and it would do it quietly, having
 * passed every test that only checks the two sides AGREE.
 */
function words(name) {
  const list = seed[name];
  if (!Array.isArray(list) || list.length === 0) {
    console.error(`${seedPath} has no "${name}" words — a redactor with an empty list redacts nothing`);
    process.exit(1);
  }
  for (const word of list) {
    // Lower-case ASCII only: both sides lower the name before they compare, so an upper-case or
    // non-ASCII word in the seed would be a word that can never match — silently.
    if (typeof word !== 'string' || !/^[a-z0-9_-]{2,64}$/.test(word)) {
      console.error(`${seedPath}: "${word}" is not a usable word (lower-case ASCII, 2-64 characters)`);
      process.exit(1);
    }
  }

  return list;
}

const anywhere = words('anywhere');
const wholePart = words('wholePart');

// A word in BOTH lists is the over-redaction this split exists to prevent, arriving by the back
// door: `api` in `anywhere` makes `api-version` a credential however carefully `wholePart` was
// written. A reviewer named it on this story's plan round, and it is the one seed mistake that
// looks harmless in a diff.
const inBoth = anywhere.filter((word) => wholePart.includes(word));
if (inBoth.length > 0) {
  console.error(`${seedPath}: ${inBoth.join(', ')} appears in BOTH lists — the broader ANYWHERE rule `
    + 'would then decide it, so the whole-part rule that was written for it never runs');
  process.exit(1);
}

// And the same thing one step subtler, which a reviewer asked for — in ONE direction only, and
// finding out which took running it. An ANYWHERE word that sits INSIDE a whole-part word makes the
// restriction on that word pointless: the broad rule fires on a superset of the names the narrow
// rule was written for, so `api` in ANYWHERE beside `api-version` in WHOLE_PART decides
// `api-version` before the whole-part rule is ever consulted.
//
// The OTHER direction is the design and must not be refused. `auth` ⊂ `authorization` and
// `key` ⊂ `apikey` are exactly why those compounds are in the long list: a run-together spelling
// has no boundary for the short word to sit on. The first version of this check tested both
// directions and refused the correct seed — five pairs, every one of them deliberate.
const swallowed = anywhere.flatMap((broad) =>
  wholePart.filter((narrow) => narrow.includes(broad))
    .map((narrow) => `${broad} inside ${narrow}`));
if (swallowed.length > 0) {
  console.error(`${seedPath}: ${swallowed.join(', ')} overlap as substrings — the ANYWHERE word `
    + 'decides every name the whole-part word was restricted to, which is the over-redaction the '
    + 'two lists exist to prevent');
  process.exit(1);
}

// Unknown top-level keys are refused rather than ignored, because a field added for the SERVER's
// loader and silently dropped here is the two halves disagreeing about the seed while every test
// stays green. Keys beginning with `$` are prose for a reader and are deliberately allowed.
const KNOWN = ['anywhere', 'wholePart', 'cases'];
const strangers = Object.keys(seed).filter((key) => !key.startsWith('$') && !KNOWN.includes(key));
if (strangers.length > 0) {
  console.error(`${seedPath}: unknown field(s) ${strangers.join(', ')} — add them to this generator `
    + 'deliberately, or a field meant for the server is dropped here in silence');
  process.exit(1);
}

const quoted = (list) => list.map((word) => `  '${word}',`).join('\n');

const file = `// GENERATED by scripts/generate-credential-words.mjs from shared/credential-words.json.
// Do not edit. Run the script; \`generatedFilesAreCurrent.test.ts\` fails when this is behind.
//
// The server embeds the same JSON (CoaiMcp.Core.csproj) and refuses to start without it. Neither
// half reads \`shared/\` at run time: a published binary and an installed VSIX have no such
// directory, and a redactor that fell back to an empty list there would write raw credentials.

/** Words that mean a credential wherever they appear inside a name. */
export const ANYWHERE: readonly string[] = [
${quoted(anywhere)}
];

/** Words that mean a credential only when they are a WHOLE part of the name. */
export const WHOLE_PART: readonly string[] = [
${quoted(wholePart)}
];
`;

const counts = `${anywhere.length} anywhere, ${wholePart.length} whole-part`;

const rerun = [
  'node src_vs_code/scripts/generate-credential-words.mjs',
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
  // Written beside the target and renamed over it: a process killed mid-write would otherwise
  // leave a truncated file that every build imports, with the last good copy already gone.
  mkdirSync(dirname(out), { recursive: true });
  const staging = `${out}.tmp`;
  writeFileSync(staging, file, 'utf8');
  renameSync(staging, out);
  console.log(`wrote ${out}: ${counts}`);
}
