import { join, resolve } from 'node:path';
import { WatchedDir } from './escalationDirs';

/**
 * The server's own name for it (`RoundsDb.FileName`), and the file a person would move.
 *
 * <p>Exported because "does this folder already hold a history" is the question the install flow
 * asks of a folder somebody picked, and asking it needs this name rather than a second copy of it.</p>
 */
export const DATABASE_FILE = 'coai.db';

/**
 * Where `coai-mcp` keeps its state, and the ONE answer to that question.
 *
 * <p>Its own module rather than a function in `extension.ts`, because the panel needs it and
 * `extension.ts` constructs the panel — so importing it from there is a cycle. It is also the wrong
 * shape: which directory holds a token file is a fact about this installation, not a part of
 * activation. Raised twice on the code round.</p>
 *
 * <p><b>It must agree with `PanelSettings.DefaultDataDir` in C#</b>, which is
 * `Environment.SpecialFolder.LocalApplicationData` + `coai-mcp` — `%LOCALAPPDATA%` on Windows and
 * `$HOME/.local/share` elsewhere, which is what the two expressions below spell out. The extension
 * WRITES the Team-server token file under this directory and the MCP shim READS it, so a divergence
 * here is the same silent "not signed in" as a divergence in the filename — and the shared vector
 * fixture covers only the filename half.</p>
 */
export function coaiDataDir(): string {
  const chosen = currentChoice();

  // Whitespace is not a configured directory, and the server agrees: its own check is `Length > 0`
  // on a trimmed value, so `COAI_DATA_DIR=' '` must mean "unset" on both sides or the extension
  // writes the token somewhere the server never looks. Raised on the code round. `chooseStorage`
  // trims every layer for the same reason.
  if (chosen.directory.length === 0) {
    return defaultDataDir();
  }

  // ABSOLUTE, the way `Path.GetFullPath` makes it absolute there. A relative COAI_DATA_DIR resolved
  // against the extension host's working directory and against the server's would be two different
  // places, and the symptom is a sign-in that silently is not there.
  const root = resolve(chosen.directory);

  // A chosen directory can be PARTITIONED per side, so two installations — Windows and WSL — can be
  // pointed at one NAS without writing the same SQLite file (issue #115). The rule is the server's,
  // `PanelSettings.ResolveDataDir`, and this is the half that must agree with it: the paragraph
  // above is about exactly this file writing a token the shim then reads.
  //
  // The side is a NAME, never derived, and that is why the two halves can agree at all: deriving it
  // would mean computing one string twice, here from `os.hostname()` and there from
  // `Environment.MachineName`, which differ in case and in whether they carry a domain.
  if (chosen.side.length === 0) {
    return root;
  }

  // A side that was ASKED FOR and refused must never fall back to the shared root — that is the
  // finding seven reviewers raised on the server's half, and it is the feature inverted:
  // `COAI_DATA_SIDE=wsl/node1` is a plausible thing to type, it fails the grammar, and falling back
  // would put this installation and every other one on the root's single database. The server
  // refuses to start on it; this half refuses to guess a path for it.
  if (!usableSideName(chosen.side)) {
    throw new Error(
      `COAI_DATA_SIDE='${chosen.side}' is not a usable directory name. A side may contain ${SIDE_GRAMMAR}.`);
  }

  // `join`, not string concatenation: `resolve` gives a NATIVE root (C:\srv\coai on Windows) and a
  // forward slash after it produced a mixed-separator path that the C# half, which uses
  // Path.Combine, never writes. Both resolve to the same directory, so nothing was broken — but the
  // two halves printed different strings for one place, which is precisely what the shared vectors
  // exist to catch and what the panel now puts on screen. (codex, code round.)
  return directoryFor(root, chosen.side);
}

/**
 * Where a root and a side resolve to, which is the rule `coaiDataDir` applies to whatever it read.
 *
 * <p>Its own function because the INSTALL flow needs the same answer before anything is saved. It
 * probed the chosen root instead, so "this folder already holds a database" could be true of the
 * root and false of `<root>/<side>` — the directory actually about to be used — and a history
 * already sitting in `<root>/<side>` was not found at all. (codex, plan round.) One rule, asked by
 * both, rather than the install flow composing a path of its own.</p>
 *
 * <p>An unusable side is the caller's to refuse; this composes what was asked for. `coaiDataDir`
 * throws on one and `whereData` renders it, which are the two right answers for those two
 * surfaces.</p>
 */
export function directoryFor(root: string, side: string): string {
  const absolute = resolve(root.trim());
  const named = side.trim().toLowerCase();

  return named.length === 0 ? absolute : join(absolute, named);
}

/**
 * `%LOCALAPPDATA%\coai-mcp`, or its equivalent — what `PanelSettings.DefaultDataDir` answers.
 *
 * <p>Exported because "is the default what this window is actually using" is a question the install
 * flow has to ask: a shared setting made on another side is what a window with no override resolves,
 * so being TOLD to keep the default is not the same as already being on it.</p>
 */
export function defaultDataDir(): string {
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;

  return `${localAppData}/coai-mcp`;
}

/** The side-name grammar, spelled exactly as `PanelSettings.IsSafeSide` spells it in C#. */
export const SIDE_GRAMMAR = 'lower-case letters, digits, dot, dash and underscore';

/**
 * The side this window was GIVEN, lower-cased — empty when none was asked for anywhere.
 *
 * <p>The name asked for, not the one in effect, and the difference matters to its one caller: the
 * panel names it while reporting that it could not work out where the data lives. A side asked for
 * while no directory is named partitions nothing — but it is still what somebody typed, and a
 * diagnostic that omits it is a diagnostic missing the thing to correct.</p>
 */
export function dataSideName(): string {
  const chosen = currentChoice();

  return chosen.side.length > 0 ? chosen.side : chosen.ignoredSide;
}

// ---------------------------------------------------------------------------------------------
// Which layer answers "where does the data live"
// ---------------------------------------------------------------------------------------------

/**
 * A directory and the side that partitions it, as one layer named them.
 *
 * <p>Empty strings mean "this layer names nothing", never "the default" — which layer the default
 * comes from is {@link chooseStorage}'s answer, not a layer's.</p>
 */
export interface StorageChoice {
  readonly directory: string;
  readonly side: string;
}

/** A layer that names nothing. */
export const NOTHING_CHOSEN: StorageChoice = { directory: '', side: '' };

/** Which layer answered, so a person can be told rather than left to deduce it. */
export type StorageSource = 'environment' | 'this side' | 'shared setting' | 'default';

/** What the layers between them decided, before the side is applied to the path. */
export interface ChosenStorage extends StorageChoice {
  /** A side named in a layer that named no directory, so it partitions nothing. */
  readonly ignoredSide: string;
  readonly source: StorageSource;
}

/**
 * Which layer names the directory this window uses.
 *
 * <p><b>The environment first</b>, because a window launched from a shell that exports
 * `COAI_DATA_DIR` should agree with a server launched from that same shell — and because it is the
 * server's own precedence, where a variable beats the settings file key by key.</p>
 *
 * <p><b>Then this side's own choice, then the shared setting.</b> `Z:\coai` and `/mnt/z/coai` are
 * one NAS and not one string, so a value shared by every window of a profile is wrong on at least
 * one side of a machine that has two; the shared layer is the fallback for a side that has never
 * chosen, not the place a choice belongs.</p>
 *
 * <p><b>The pair travels together.</b> Whichever layer names the DIRECTORY names the side as well: a
 * side partitions a particular directory, and composing one layer's root with another's side builds
 * a path nobody configured. That path then exists, is empty, and says nothing about why.</p>
 */
export function chooseStorage(
  environment: StorageChoice,
  thisSide: StorageChoice,
  shared: StorageChoice,
): ChosenStorage {
  const layers: readonly (readonly [StorageSource, StorageChoice])[] = [
    ['environment', environment],
    ['this side', thisSide],
    ['shared setting', shared],
  ];

  for (const [source, layer] of layers) {
    const directory = layer.directory.trim();
    if (directory.length > 0) {
      return { directory, side: sideNameIn(layer), ignoredSide: '', source };
    }
  }

  // Nobody named a directory, so nobody's side partitions anything. Naming the side that was asked
  // for anyway is the difference between "you have no side" and "you set one and it is doing
  // nothing", and only the second sentence tells somebody what to change.
  const named = layers.map(([, layer]) => sideNameIn(layer)).find((side) => side.length > 0) ?? '';

  return { directory: '', side: '', ignoredSide: named, source: 'default' };
}

/** A layer's side, in the shape the server compares: trimmed and lower-cased. */
function sideNameIn(layer: StorageChoice): string {
  return layer.side.trim().toLowerCase();
}

/** The two variables, as this process has them. */
function environmentChoice(): StorageChoice {
  return { directory: process.env['COAI_DATA_DIR'] ?? '', side: process.env['COAI_DATA_SIDE'] ?? '' };
}

/**
 * One settings layer, read through whatever answers for it.
 *
 * <p>Anything that is not a string is nothing: a number, an array or a `null` left by a hand-edited
 * `settings.json` must not become part of a path. The reader is a parameter so this stays free of
 * `vscode` — the panel and the webview page both compile against this module.</p>
 */
export function storageChoiceFrom(read: (section: string) => unknown): StorageChoice {
  const text = (section: string): string => {
    const value = read(section);

    return typeof value === 'string' ? value : '';
  };

  return { directory: text('dataDirectory'), side: text('dataSide') };
}

/**
 * The two settings layers this window read for itself.
 *
 * <p>Module state, deliberately, and with the same justification as `reloadOffered` in
 * `sideConfig.ts`: the alternative is a parameter on `coaiDataDir()`, which has fifteen call sites
 * across four modules, and a parameter fifteen callers must remember is a directory fourteen of them
 * will eventually get right. There is ONE answer to "where does this window keep its data"; this is
 * where it is kept.</p>
 *
 * <p>Reading a setting needs `vscode`, which this module must not import — the panel and the page
 * both compile against it. So the host reads the two layers and installs them here, once, at
 * activation and again whenever the configuration changes.</p>
 */
let installed: { readonly thisSide: StorageChoice; readonly shared: StorageChoice } = {
  thisSide: NOTHING_CHOSEN,
  shared: NOTHING_CHOSEN,
};

/**
 * Install what this window read. Called at activation BEFORE anything resolves a path — the chat
 * store is constructed from one — and again on every configuration change.
 */
export function useStorageSettings(thisSide: StorageChoice, shared: StorageChoice): void {
  installed = { thisSide, shared };
}

/** Every layer, in order. */
function currentChoice(): ChosenStorage {
  return chooseStorage(environmentChoice(), installed.thisSide, installed.shared);
}

/**
 * What a client entry needs in its `env` so the server it spawns reads what this window reads.
 *
 * <p>The ROOT and the side, never the resolved path: handing `<root>/<side>` as `COAI_DATA_DIR`
 * while also naming the side resolves to `<root>/<side>/<side>` on the server. Five reviewers
 * reached that by different routes on the predecessor's code round.</p>
 *
 * <p>Empty when there is nothing to say — the default directory needs no variable, and a refused
 * side has no configuration worth offering — and the side key is omitted rather than pasted empty,
 * because a key that means nothing invites the question of what it is for. This is also what the
 * install flow puts in the block it copies, and what every spawned read of the database is given.</p>
 */
/**
 * The ROOT a layer named, absolute — empty when nothing named one and the default is in use.
 *
 * <p>The root rather than the resolved directory: a side lives inside it, and both the "there is a
 * loose database in the shared root" note and the probe that feeds it are questions about the root
 * itself. The panel used to read `COAI_DATA_DIR` directly for this, which stopped being the whole
 * answer the moment a directory could be chosen in a setting — and the failure is quiet, because a
 * note that never fires looks exactly like a note with nothing to say.</p>
 */
export function chosenRoot(): string {
  const chosen = currentChoice();

  return chosen.directory.length === 0 ? '' : resolve(chosen.directory);
}

export function serverEnv(): Readonly<Record<string, string>> {
  const chosen = currentChoice();
  if (chosen.directory.length === 0 || (chosen.side.length > 0 && !usableSideName(chosen.side))) {
    return {};
  }

  return {
    COAI_DATA_DIR: resolve(chosen.directory),
    ...(chosen.side.length === 0 ? {} : { COAI_DATA_SIDE: chosen.side }),
  };
}

/**
 * Where THIS WINDOW reads and writes, and what a person should be told about it.
 *
 * <p><b>"This window" is the load-bearing part of that sentence.</b> The extension host has its own
 * environment, and the MCP server's comes from the client entry that spawns it — a
 * `COAI_DATA_DIR` in a `.mcp.json` reaches the server and never reaches this process. The two
 * CAN differ, and the product already knows it: the help page says, in five languages, that a
 * rounds list reading *Nothing is running* while your assistant says it is reviewing means "the
 * server it talks to is writing somewhere else — a COAI_DATA_DIR in its config that this window
 * does not share".</p>
 *
 * <p>Until now that was a thing you inferred from an empty list. This makes it visible: the panel
 * says which directory this window resolved and offers the line that makes a client's server agree
 * with it. Raised as Blocking by gemini on the plan round, and it is the finding that reframed the
 * whole feature — the panel is not reporting the server's answer, it is reporting its own, and
 * saying so is what makes the comparison possible.</p>
 *
 * @param exists Whether a path is there. Injected so the rule is testable without a filesystem,
 *   which is why `PanelSettings.StorageNotes` takes its environment the same way.
 */
export function whereData(exists: (path: string) => boolean): DataLocation {
  const chosen = currentChoice();

  // A side that cannot be used is reported, never guessed at. The server REFUSES TO START on this,
  // so a panel that threw here would hide the one sentence that explains why nothing works. It is
  // refused wherever it was named: a setting can hold `wsl/node1` exactly as a variable can.
  if (chosen.directory.length > 0 && chosen.side.length > 0 && !usableSideName(chosen.side)) {
    return {
      directory: '',
      side: chosen.side,
      ignoredSide: '',
      refusal: `COAI_DATA_SIDE='${chosen.side}' is not a usable directory name, so the server refuses to `
        + `start. A side may contain ${SIDE_GRAMMAR}.`,
      notes: [],
      alsoWatched: [],
      env: {},
      source: chosen.source,
    };
  }

  const directory = coaiDataDir();

  // A side named while NO directory is configured partitions nothing: `coaiDataDir` ignores it and
  // returns the default. Reporting it as the side in use would be a lie about the one thing this
  // section exists to state — "this window keeps its database apart" — and it would then be handed
  // to a paste block as though it meant something. Named separately so the panel can say what is
  // actually true: you set a side, and nothing is using it. (gemini, code round.)
  if (chosen.directory.length === 0) {
    return {
      directory,
      side: '',
      ignoredSide: chosen.ignoredSide,
      refusal: '',
      notes: [],
      alsoWatched: [],
      env: {},
      source: chosen.source,
    };
  }

  const root = resolve(chosen.directory);
  const notes: string[] = [];

  if (directory !== root && exists(join(root, DATABASE_FILE))) {
    notes.push(
      `There is a ${DATABASE_FILE} directly in ${root}, from the layout before this directory was `
      + `shared between sides. It is NOT being used — this window reads and writes ${directory}. `
      + 'Move that database and its sessions into a side directory to keep their history.');
  }

  if (!exists(directory)) {
    notes.push(
      `${directory} is not there yet, so this side starts with no history. If that is a surprise, `
      + 'check COAI_DATA_DIR for a typo before recording into it.');
  }

  return {
    directory,
    side: chosen.side,
    ignoredSide: '',
    refusal: '',
    notes,
    // Filled by the PANEL, which is the half that can read the setting; this function is pure.
    alsoWatched: [],
    // Built from the LAYER that named them — never taken back out of the rendered path. Five
    // reviewers reached the same conclusion by different routes: slicing a side name off the end of
    // a resolved directory is arithmetic that is wrong the moment a side maps to anything but
    // `<root>/<side>`, and silently wrong on a drive root.
    env: serverEnv(),
    source: chosen.source,
  };
}

/** What `whereData` answers: where this window reads, and what to say about it. */
export interface DataLocation {
  /** The resolved directory — empty only when the side was refused. */
  readonly directory: string;
  /** The side actually in effect, or empty. */
  readonly side: string;
  /**
   * A side that was named and is doing nothing, because no directory is configured.
   *
   * <p>Its own field rather than a silence: `COAI_DATA_SIDE` without `COAI_DATA_DIR` is a setting
   * somebody made on purpose and that has no effect, which is exactly the state this section exists
   * to make visible.</p>
   */
  readonly ignoredSide: string;
  /** Why there is no directory, or empty. A refusal is a state, not an exception, on this surface. */
  readonly refusal: string;
  /** What a person should be told — a loose database, a directory that is not there yet. */
  readonly notes: readonly string[];
  /**
   * Every directory whose questions this window answers — its own first, then what was named.
   *
   * <p>On this surface because the failure it guards against is SILENCE: a named directory that
   * cannot be read contributes no questions and throws nothing, which is indistinguishable from an
   * installation that has asked nothing. That is the symptom `coai.alsoWatchDataDirectories` exists
   * to end, and it would be the symptom again, one level up, if a mistyped path were nowhere on
   * screen.</p>
   */
  readonly alsoWatched: readonly WatchedDir[];
  /**
   * What a client entry needs so its server reads the same directory this window does.
   *
   * <p>Empty when there is nothing to say: the default directory needs no variable, and a refused
   * side has no configuration worth offering. `COAI_DATA_SIDE` is present only when a side is in
   * effect — a key pasted empty means nothing and invites the question of what it is for.</p>
   */
  readonly env: Readonly<Record<string, string>>;
  /**
   * Which layer answered — the environment, this side's own choice, the shared setting, or nothing
   * at all.
   *
   * <p>A person whose panel and server disagree is entitled to read why rather than deduce it. It
   * is also the one thing that separates "I chose this" from "another side chose it and this window
   * inherited the string", which on a NAS reached from two mounts is the difference between a path
   * that exists and one that does not.</p>
   */
  readonly source: StorageSource;
}

/**
 * What moves when the directory does, and — the half that is easy to get wrong — what does not.
 *
 * <p>Here rather than in the renderer because it is a fact about the STORAGE, not about the page:
 * the day the server writes a new persistent directory, this is the list that has to grow, and a
 * sentence buried in a panel is not where anybody would look for it. (codex, code round.)</p>
 *
 * <p><b>It named four of these for a release, and the other twelve were lost in silence.</b> A copy
 * that misses a file does not fail, so somebody following the panel's own instructions kept their
 * rounds and lost their edited prompts, their whole spending history, the entire chat half of the
 * product and the rounds still sitting in the database's write-ahead log. The list is now checked
 * against `shared/data-inventory.json`, which a suite compares with every path either half composes
 * under this directory — so the next persistent directory is a red test rather than a later
 * audit.</p>
 *
 * <p>The two sidecars are not an implementation detail to be tidied away: the journal mode is WAL,
 * so the transactions committed most recently live in `coai.db-wal` until a clean shutdown moves
 * them, and a copy of the database alone loses exactly the newest rounds.</p>
 */
export const DATA_TO_MOVE: readonly string[] = [
  DATABASE_FILE, `${DATABASE_FILE}-wal`, `${DATABASE_FILE}-shm`,
  'sessions/', 'unparseable/', 'empty/',
  'prompts/', 'usage.jsonl',
  'documents/', 'escalations/', 'consultations/', 'callers/',
  'chat-conversations/', 'chat-usage.jsonl', 'chat-doors.jsonl', 'pictures/',
  // The logs, by the operator's decision of 2026-09-15, and `rounds.md`, which a real installation
  // turned out to be holding. Both had been filed as "written again by itself" and neither is: a new
  // run writes a new log and says nothing about the runs already recorded, and NOTHING writes
  // `rounds.md` any more — it is the rounds log as it stood before the database took over, which is
  // exactly the shape of thing that gets left behind for ever on a machine about to be reformatted.
  //
  // `logs/` carries a caveat the fixture states in full: on a side-partitioned installation the
  // server writes them to the ROOT rather than the side directory, because `SettingsFile.DataDirFrom`
  // applies no side — so a partitioned move finds nothing here until that defect ships its own fix.
  'logs/', 'rounds.md',
];

/**
 * What makes a destination REFUSE a move, which is not the same list.
 *
 * <p>Moving a thing and being destroyed by a thing are two questions, and `logs/` is where they come
 * apart: it carries history worth taking, and a destination that already has one loses nothing when
 * a second installation's logs arrive beside its own — a log file is named for its run and its pid,
 * so nothing is overwritten. Refusing on it would refuse every folder a server had ever been pointed
 * at, which after this change is most of the folders anybody would pick.</p>
 *
 * <p>Everything else that moves DOES clash: one `coai.db`, one `usage.jsonl`, one `rounds.md`, one
 * file per session id. A second installation's copy lands on top.</p>
 *
 * <p><b>The exclusion depends on a filename shape, so here it is.</b> A log is
 * `logs/<yyyy-MM-dd>/<app>-<HH-mm-ss>-<pid>.log` — the day, the second and the process id. If that
 * ever becomes a fixed name (`latest.log`), this exclusion becomes a silent overwrite and `logs/`
 * belongs back in the list. Two reviewers asked for the assumption to be written down rather than
 * relied on.</p>
 *
 * <p><b>And the residual, stated rather than claimed away.</b> "Overwrites nothing" is stronger than
 * the facts: two machines writing into one shared folder can produce the same name in the same second
 * with the same pid. It is unlikely, the loss is one log file, and namespacing every log by machine
 * is not worth building for it — but it is not impossible, and saying so costs a sentence. (gemini,
 * plan round.)</p>
 */
/**
 * The one entry that MOVES and does not CLASH, named once so neither list repeats it.
 *
 * <p>A filter with a magic string inside it cannot be changed independently of the list it filters —
 * a reviewer's point on the code round, and right. `shared/data-inventory.json` carries `move` and
 * `clash` per entry as the source of truth, and `theInventoryIsComplete.test.ts` asserts BOTH lists
 * against those two fields — so removing `logs/` from what moves cannot leave a stale exception
 * behind here, and adding a second append-only entry is a red test rather than a silent refusal of
 * every destination that already holds one.</p>
 */
export const MOVES_WITHOUT_CLASHING: readonly string[] = ['logs/'];

export const HISTORY_THAT_CLASHES: readonly string[] =
  DATA_TO_MOVE.filter((entry) => !MOVES_WITHOUT_CLASHING.includes(entry));

/**
 * What to leave behind, with the reason each one is a trap.
 *
 * <p>Both are silent when got wrong, which is why they are named rather than left to judgement:
 * `worktrees/` is scratch pruned on every `open`, so copying it moves a checkout of somebody's
 * repository onto a NAS for no benefit; and a token belongs to the side that signed in, so copying
 * one hands a machine's sign-in to another — the thing the per-side layout exists to prevent.</p>
 */
export const DATA_TO_LEAVE: readonly string[] = ['worktrees/', 'servers/'];


/**
 * The server's own rule, and deliberately narrower than any filesystem's.
 *
 * <p>An explicit allowlist rather than a list of forbidden characters, because the C# half cannot
 * use `Path.GetInvalidFileNameChars()` for this: it is platform-dependent — a colon is refused on
 * Windows and accepted on Linux — so `a:b` would resolve to two different directories on the two
 * sides of one installation, and the symptom is a token written where the server does not read it.
 * Four reviewers found that independently. A rule simple enough to write twice without drifting is
 * the point.</p>
 *
 * <p>Exported because the install flow OFFERS a side name and must not offer one the server would
 * refuse — one rule, asked by whoever needs it, rather than a second copy in the asking.</p>
 */
export function usableSideName(side: string): boolean {
  return side !== '.' && side !== '..' && /^[a-z0-9._-]+$/u.test(side);
}
