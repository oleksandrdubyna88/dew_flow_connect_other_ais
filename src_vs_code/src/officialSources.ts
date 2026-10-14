/**
 * The only publishers an install command may draw on — every URL it fetches and every package it installs — and the
 * check that holds a command to them.
 *
 * <p>A third-party repackaging can be the most convenient thing on the machine and it still does not go behind a
 * button: a button that installs software gets pressed without reading. Split out of `vendorTerminal.ts`, which
 * re-exports both names, when the check grew a parser of its own.</p>
 *
 * <p><b>Why the RENDERED command is checked, not a recipe of steps.</b> The epic 2 risk consultation (gpt-6-astra,
 * 2026-10-13) preferred validating a structured recipe before rendering it. What a person runs is the TEXT — typed into
 * a terminal by `panelProvider.openCliTerminal` — and the text comes from four different builders (npm lines, two
 * vendor scripts, one PowerShell line of several steps); a recipe check would vouch for the recipe and stay silent
 * about a renderer that wrote something else. So the artefact itself is held to the list, step by step, and refused
 * whole on anything it cannot read as an official step.</p>
 */

export const OFFICIAL_SOURCES = {
  /** The vendor's own installer on the vendor's own domain — the DOMAIN is the guard. */
  urls: [
    'https://antigravity.google/',
    'https://mistral.ai/vibe/',
    // Astral's installer for uv, the one Mistral's README gives as the Windows prerequisite (and the one Mistral's own
    // install.sh runs on Linux and macOS, at a pinned version).
    'https://astral.sh/uv/',
  ],
  /** What `npm install -g`, `uv tool install` and `uv tool upgrade` may be handed — by the package's full name. */
  packages: ['@openai/codex', '@google/gemini-cli', '@anthropic-ai/claude-code', 'mistral-vibe'],
} as const;

const URL_IN_A_COMMAND = /https?:\/\/[^\s"'|;)]+/gu;

/** Where one step of a command line ends and the next begins — in PowerShell, cmd and bash alike. */
const STEP_BREAK = /;|&&|\|\||\||\{|\}|\r?\n/u;

/**
 * The install verbs this check can read, each with the flags it admits and the shape of one package argument.
 *
 * <p>npm: `name` or `name@latest` / `name@1.2.3` — never `name@npm:other`, `name@file:…`, `name@github:…` or a URL,
 * which npm reads as ALIASES of other code. uv: `name` or `name==1.2.3`, and no flag at all — `--from`, `--with`,
 * `--index-url` each bring in code from somewhere else.</p>
 */
const VERBS: readonly { readonly words: readonly [string, string]; readonly flags: readonly string[]; readonly spec: RegExp }[] = [
  { words: ['npm', 'install'], flags: ['-g', '--global'], spec: /^(.+?)(?:@(?:latest|\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?))?$/u },
  { words: ['tool', 'install'], flags: [], spec: /^(.+?)(?:==\d+(?:\.\d+)*)?$/u },
  { words: ['tool', 'upgrade'], flags: [], spec: /^(.+?)(?:==\d+(?:\.\d+)*)?$/u },
];

/** A word that means "install something" — a step carrying one that no verb above reads is refused, not skipped. */
const INSTALL_WORDS: ReadonlySet<string> = new Set(['install', 'i', 'add', 'upgrade', 'update']);

/**
 * Whether a command draws ONLY on official sources: at least one source; every URL under an admitted prefix; and in
 * every step, an install verb this check reads, each of whose arguments is an admitted flag or an admitted package with
 * at most a plain version. A step that installs through anything else — pip, a second npm line with another package,
 * a uv flag — refuses the whole command.
 */
export function fromOfficialSources(command: string): boolean {
  const urls = [...command.matchAll(URL_IN_A_COMMAND)].map((match) => match[0]);
  const steps = command.split(STEP_BREAK).map(wordsOf).map(stepPackages);

  return urls.length + steps.reduce((sum, step) => sum + Math.max(step, 0), 0) > 0
    && urls.every((url) => OFFICIAL_SOURCES.urls.some((prefix) => url.startsWith(prefix)))
    && steps.every((step) => step >= 0);
}

/** A step's words, quotes stripped — what a shell would hand the program. */
function wordsOf(step: string): readonly string[] {
  return step.trim().split(/\s+/u).map(withoutOuterQuotes).filter((word) => word.length > 0);
}

/**
 * A word without the quotes around it — by index, because `^["']+|["']+$` backtracks quadratically on a word with a long
 * run of quotes inside it (SonarCloud S8786 on #803; 5.5 s for 60 000).
 */
function withoutOuterQuotes(word: string): string {
  const start = quotesFrom(word, 0, 1, word.length);
  const end = word.length - quotesFrom(word, word.length - 1, -1, word.length - start);

  return word.slice(start, end);
}

/** How many quote characters stand at `from`, walking by `step`, looking at no more than `limit` characters. */
function quotesFrom(word: string, from: number, step: 1 | -1, limit: number): number {
  let count = 0;
  while (count < limit && (word[from + count * step] === '"' || word[from + count * step] === "'")) {
    count++;
  }

  return count;
}

type Verb = (typeof VERBS)[number];

/** How many official packages a step installs: 0 for a step that installs nothing, -1 for one this check refuses. */
function stepPackages(words: readonly string[]): number {
  const at = VERBS.map((verb) => ({ verb, index: verbAt(words, verb.words) })).find((found) => found.index >= 0);

  return at === undefined ? unknownInstall(words) : packagesAfter(words.slice(at.index + 2), at.verb);
}

/** A step no verb above reads: refused when it still says "install", nothing otherwise. */
function unknownInstall(words: readonly string[]): number {
  return words.some((word) => INSTALL_WORDS.has(word)) ? -1 : 0;
}

/** A read verb's arguments: every one an admitted flag or an admitted package, and at least one package. */
function packagesAfter(words: readonly string[], verb: Verb): number {
  const args = words.filter((word) => !verb.flags.includes(word));

  return args.length > 0 && args.every((arg) => isOfficialPackage(arg, verb.spec)) ? args.length : -1;
}

/** Where a verb's two words stand together in a step, or -1. */
function verbAt(words: readonly string[], verb: readonly [string, string]): number {
  return words.findIndex((word, index) => word === verb[0] && words[index + 1] === verb[1]);
}

function isOfficialPackage(arg: string, spec: RegExp): boolean {
  const name = spec.exec(arg)?.[1] ?? '';

  return (OFFICIAL_SOURCES.packages as readonly string[]).includes(name);
}
