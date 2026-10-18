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
 *
 * <p><b>What it is run over.</b> This check runs only in tests, over coai's own constants (the lines `vendorTerminal.ts` builds). A
 * command composed from RUNTIME data — a path a person typed, a value read from settings — would not be checked by it at all, and
 * its `sudo` rule reads a word as plainly spelled (see {@link SUDO}).</p>
 */

export const OFFICIAL_SOURCES = {
  /** The vendor's own installer on the vendor's own domain — the DOMAIN is the guard. */
  urls: [
    'https://antigravity.google/',
    'https://mistral.ai/vibe/',
    // Astral's installer for uv, the one Mistral's README gives as the Windows prerequisite (and the one Mistral's own
    // install.sh runs on Linux and macOS, at a pinned version).
    'https://astral.sh/uv/',
    // Moonshot's own installer scripts for Kimi Code (getting-started page, read 2026-10-14): the domain AND the path. Not
    // cdn.kimi.com, which no command names — the 302 from code.kimi.com is the publisher's own business.
    'https://code.kimi.com/kimi-code/',
  ],
  /** What `npm install -g`, `uv tool install` and `uv tool upgrade` may be handed — by the package's full name. */
  packages: ['@openai/codex', '@google/gemini-cli', '@anthropic-ai/claude-code', 'mistral-vibe'],
  /**
   * What `winget install --id` may be handed — the Windows Kimi line provisions what Kimi needs, from the projects' own
   * winget packages (verified 2026-10-14 by `winget show --id <id> -e`; todo/PLAN_vibe_and_kimi_runtimes_E4.md E4.5 §6):
   * Git for Windows (Kimi runs its shell through Git Bash), ripgrep (BurntSushi's own build, 15.2.0) and fd (David Peter's
   * own, 10.5.0). Their installers are the projects' own GitHub release assets — nobody's repackaging.
   */
  wingetIds: ['Git.Git', 'BurntSushi.ripgrep.MSVC', 'sharkdp.fd'],
  /**
   * What `apt-get install` and `dnf install` may be handed — the Linux Kimi line provisions the same two tools the Windows
   * line does (DL12 of todo/PLAN_kimi_and_vibe_on_linux.md), from the distribution's OWN packages, named as the projects'
   * READMEs name them: `ripgrep`, and `fd-find` (Debian and Ubuntu install its program as `fdfind`, Fedora as `fd`). Nothing
   * else: a package of the caller's own, a local `.deb` and a pinned release are each outside the list.
   */
  distroPackages: ['ripgrep', 'fd-find'],
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
/** A distribution package argument: a plain name — never `name=version`, `name/release`, a path or a URL. */
const DISTRO_PACKAGE = /^([a-z0-9][a-z0-9.+-]*)$/u;

const VERBS: readonly {
  readonly words: readonly [string, string];
  readonly flags: readonly string[];
  readonly spec: RegExp;
  /** Which list of {@link OFFICIAL_SOURCES} the verb's arguments are drawn from. */
  readonly admitted: 'packages' | 'wingetIds' | 'distroPackages';
  /** The most arguments one step may carry — one for winget, whose second id is a second thing to install. */
  readonly most: number;
  /** Flag-and-value pairs admitted EXACTLY as written: `--source winget` pins winget's community repository, any other value is a different source. */
  readonly pairs: readonly (readonly [string, string])[];
  /** Groups of spellings of which at least one must be present: `-e` / `--exact`, so an id is matched whole and not as a search. */
  readonly required: readonly (readonly string[])[];
}[] = [
  { words: ['npm', 'install'], flags: ['-g', '--global'], spec: /^(.+?)(?:@(?:latest|\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?))?$/u, admitted: 'packages', most: Infinity, pairs: [], required: [] },
  { words: ['tool', 'install'], flags: [], spec: /^(.+?)(?:==\d+(?:\.\d+)*)?$/u, admitted: 'packages', most: Infinity, pairs: [], required: [] },
  { words: ['tool', 'upgrade'], flags: [], spec: /^(.+?)(?:==\d+(?:\.\d+)*)?$/u, admitted: 'packages', most: Infinity, pairs: [], required: [] },
  // winget: an EXACT id from the list (-e / --exact is required) out of the community repository (only the pair --source winget is
  // admitted), and no flag that points the install at anything else. --manifest, --location, -v / --version, any other --source and
  // a second id are each refused by construction: the argument is then neither a flag listed here nor an id of the list, or there is
  // more than one. --id itself is a flag here; its value is the one argument.
  { words: ['winget', 'install'], flags: ['--id', '-e', '--exact', '--accept-source-agreements', '--accept-package-agreements', '--silent', '-h'], spec: /^([A-Za-z0-9][A-Za-z0-9.+-]*)$/u, admitted: 'wingetIds', most: 1,
    pairs: [['--source', 'winget']], required: [['-e', '--exact']] },
  // The distribution's own package manager, for the two tools Kimi needs (DL12): `-y` is the only flag, and the arguments are the
  // package names of `distroPackages` — a version pin (`ripgrep=1.0`, `ripgrep/unstable`), a local file or URL, `-o`,
  // `--allow-unauthenticated`, `--nogpgcheck` and `--repofrompath` are each outside the list by construction, as winget's flags are.
  { words: ['apt-get', 'install'], flags: ['-y'], spec: DISTRO_PACKAGE, admitted: 'distroPackages', most: 2, pairs: [], required: [] },
  { words: ['dnf', 'install'], flags: ['-y'], spec: DISTRO_PACKAGE, admitted: 'distroPackages', most: 2, pairs: [], required: [] },
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
    && urls.every(urlIsAdmitted)
    && steps.every((step) => step >= 0);
}

/** A parent segment, plain or percent-encoded (any case), leaves the folder a prefix names — `/kimi-code/../x` still STARTS with `/kimi-code/`. */
const LEAVES_THE_FOLDER = /\.\.|%2e/iu;

/** Under an admitted prefix, and not climbing out of it. */
function urlIsAdmitted(url: string): boolean {
  return !LEAVES_THE_FOLDER.test(url) && OFFICIAL_SOURCES.urls.some((prefix) => url.startsWith(prefix));
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

/**
 * How many official packages a step installs: 0 for a step that installs nothing, -1 for one this check refuses.
 *
 * <p>A step that runs through `sudo` is read by {@link privilegedStep} instead, and that is a rule of its own: until it
 * existed `sudo` was invisible here, because a step is refused only when it carries an install word — so `sudo rm -rf …` or
 * `sudo chmod …` beside one admitted URL was ADMITTED (S-K10 of todo/PLAN_kimi_and_vibe_on_linux.md, red against that parser).</p>
 */
function stepPackages(words: readonly string[]): number {
  const sudo = words.findIndex((word) => SUDO.test(word));
  if (words.some((word) => OTHER_ESCALATOR.test(word))) {
    return -1;
  }

  return sudo < 0 ? unprivilegedStep(words) : privilegedStep(words, sudo);
}

/** A step with no `sudo` in it: an install verb this check reads, or nothing it needs to read. */
function unprivilegedStep(words: readonly string[]): number {
  const at = VERBS.map((verb) => ({ verb, index: verbAt(words, verb.words) })).find((found) => found.index >= 0);

  return at === undefined ? unknownInstall(words) : packagesAfter(words.slice(at.index + 2), at.verb);
}

/**
 * `sudo` as a shell reads it — alone, by its path (`/usr/bin/sudo`), or opening a command substitution (`$(sudo`, a backtick,
 * a parenthesis). A WORD that ends in `sudo` and is anything else (`pseudo`) is not it.
 *
 * <p><b>The honest boundary of this rule, and of {@link OTHER_ESCALATOR}.</b> They read a word as PLAINLY spelled. A quote or an
 * escape inside the word (a backslash, `su'do'`, `su""do`), a `$'sudo'` string, or a variable that holds the word (`s=sudo; $s …`) is
 * a spelling this check does not read — `officialSourcesLinux.test.ts` pins those as documented NOT-caught rows. What stands behind
 * the rule is where the text comes from: the check runs only over coai's own constants.</p>
 */
const SUDO = /^[$(`\\]*(?:[^/\s]*\/)*sudo$/u;

/**
 * The other plain privilege escalators — `su`, `doas`, `pkexec`, `run0` — by name or by path, anywhere in a step. Unlike `sudo` they
 * are never admitted: only `sudo` carries the distribution shape (the plan's DL12 names only it), so any step with one of these words
 * is refused whole.
 */
const OTHER_ESCALATOR = /^[$(`\\]*(?:[^/\s]*\/)*(?:su|doas|pkexec|run0)$/u;

/** The shell words that may open a step without being a command: `then sudo …` inside an `if`, `do sudo …` inside a loop. */
const OPENS_A_STEP: ReadonlySet<string> = new Set(['then', 'else', 'do', '!']);

/**
 * A step that runs through `sudo`: refused unless what follows it is one of the two distribution package steps — `apt-get
 * install` or `dnf install` for the packages of {@link OFFICIAL_SOURCES} — and nothing but a step-opening shell word stands
 * in front of it. `sudo -u root …`, `sudo rm …`, `sudo npm install …` and a second `sudo` all fail the first half.
 */
function privilegedStep(words: readonly string[], at: number): number {
  const rest = words.slice(at + 1);
  const carriesADistroVerb = VERBS.some((verb) => verb.admitted === 'distroPackages' && rest[0] === verb.words[0] && rest[1] === verb.words[1]);

  return carriesADistroVerb && words.slice(0, at).every((word) => OPENS_A_STEP.has(word)) ? unprivilegedStep(rest) : -1;
}

/** A step no verb above reads: refused when it still says "install", nothing otherwise. */
function unknownInstall(words: readonly string[]): number {
  return words.some((word) => INSTALL_WORDS.has(word)) ? -1 : 0;
}

/** A read verb's arguments: every one an admitted flag or an admitted package, and at least one package. */
function packagesAfter(words: readonly string[], verb: Verb): number {
  const left = withoutPairs(words, verb.pairs);
  const args = left.filter((word) => !verb.flags.includes(word));

  return hasRequired(left, verb.required) && admitted(args, verb) ? args.length : -1;
}

/** The words with each admitted flag-and-value pair taken out — only the pair as written, so `--source msstore` stays and is then refused. */
function withoutPairs(words: readonly string[], pairs: Verb['pairs']): readonly string[] {
  return pairs.reduce((left, [flag, value]) => left.filter((word, at) => !(word === flag && left[at + 1] === value) && !(word === value && left[at - 1] === flag)), words);
}

/** Every group of spellings has at least one present. */
function hasRequired(words: readonly string[], required: Verb['required']): boolean {
  return required.every((spellings) => spellings.some((spelling) => words.includes(spelling)));
}

/** Whether a step's non-flag arguments are all the verb's to install, and no more of them than it may carry. */
function admitted(args: readonly string[], verb: Verb): boolean {
  return args.length > 0 && args.length <= verb.most && args.every((arg) => isOfficialPackage(arg, verb));
}

/** Where a verb's two words stand together in a step, or -1. */
function verbAt(words: readonly string[], verb: readonly [string, string]): number {
  return words.findIndex((word, index) => word === verb[0] && words[index + 1] === verb[1]);
}

function isOfficialPackage(arg: string, verb: Verb): boolean {
  const name = verb.spec.exec(arg)?.[1] ?? '';

  return (OFFICIAL_SOURCES[verb.admitted] as readonly string[]).includes(name);
}
