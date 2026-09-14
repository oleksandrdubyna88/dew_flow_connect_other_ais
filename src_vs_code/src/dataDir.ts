import { resolve } from 'node:path';

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
  const configured = (process.env['COAI_DATA_DIR'] ?? '').trim();
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;

  // Whitespace is not a configured directory, and the server agrees: its own check is `Length > 0`
  // on a trimmed value, so `COAI_DATA_DIR=' '` must mean "unset" on both sides or the extension
  // writes the token somewhere the server never looks. Raised on the code round.
  if (configured.length === 0) {
    return `${localAppData}/coai-mcp`;
  }

  // ABSOLUTE, the way `Path.GetFullPath` makes it absolute there. A relative COAI_DATA_DIR resolved
  // against the extension host's working directory and against the server's would be two different
  // places, and the symptom is a sign-in that silently is not there.
  const root = resolve(configured);

  // A chosen directory can be PARTITIONED per side, so two installations — Windows and WSL — can be
  // pointed at one NAS without writing the same SQLite file (issue #115). The rule is the server's,
  // `PanelSettings.ResolveDataDir`, and this is the half that must agree with it: the paragraph
  // above is about exactly this file writing a token the shim then reads.
  //
  // The side is a NAME, never derived, and that is why the two halves can agree at all: deriving it
  // would mean computing one string twice, here from `os.hostname()` and there from
  // `Environment.MachineName`, which differ in case and in whether they carry a domain.
  const asked = (process.env['COAI_DATA_SIDE'] ?? '').trim().toLowerCase();

  if (asked.length === 0) {
    return root;
  }

  // A side that was ASKED FOR and refused must never fall back to the shared root — that is the
  // finding seven reviewers raised on the server's half, and it is the feature inverted:
  // `COAI_DATA_SIDE=wsl/node1` is a plausible thing to type, it fails the grammar, and falling back
  // would put this installation and every other one on the root's single database. The server
  // refuses to start on it; this half refuses to guess a path for it.
  if (!isSafeSide(asked)) {
    throw new Error(
      `COAI_DATA_SIDE='${asked}' is not a usable directory name. A side may contain ${SIDE_GRAMMAR}.`);
  }

  return `${root}/${asked}`;
}

/** The side-name grammar, spelled exactly as `PanelSettings.IsSafeSide` spells it in C#. */
const SIDE_GRAMMAR = 'lower-case letters, digits, dot, dash and underscore';

/** The name this window's side was given, lower-cased — empty when none was asked for. */
export function dataSideName(): string {
  return (process.env['COAI_DATA_SIDE'] ?? '').trim().toLowerCase();
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
  const configured = (process.env['COAI_DATA_DIR'] ?? '').trim();
  const side = dataSideName();

  // A side that cannot be used is reported, never guessed at. The server REFUSES TO START on this,
  // so a panel that threw here would hide the one sentence that explains why nothing works.
  if (configured.length > 0 && side.length > 0 && !isSafeSide(side)) {
    return {
      directory: '',
      side,
      refusal: `COAI_DATA_SIDE='${side}' is not a usable directory name, so the server refuses to `
        + `start. A side may contain ${SIDE_GRAMMAR}.`,
      notes: [],
    };
  }

  const directory = coaiDataDir();
  if (configured.length === 0) {
    // The default has not moved, and nothing about it is worth warning anybody over.
    return { directory, side, refusal: '', notes: [] };
  }

  const root = resolve(configured);
  const notes: string[] = [];

  if (directory !== root && exists(`${root}/${DATABASE_FILE}`)) {
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

  return { directory, side, refusal: '', notes };
}

/** What `whereData` answers: where this window reads, and what to say about it. */
export interface DataLocation {
  /** The resolved directory — empty only when the side was refused. */
  readonly directory: string;
  /** The side name, or empty when none was asked for. */
  readonly side: string;
  /** Why there is no directory, or empty. A refusal is a state, not an exception, on this surface. */
  readonly refusal: string;
  /** What a person should be told — a loose database, a directory that is not there yet. */
  readonly notes: readonly string[];
}

/** The server's own name for it (`RoundsDb.FileName`), and the file a person would move. */
const DATABASE_FILE = 'coai.db';

/**
 * The server's own rule, and deliberately narrower than any filesystem's.
 *
 * <p>An explicit allowlist rather than a list of forbidden characters, because the C# half cannot
 * use `Path.GetInvalidFileNameChars()` for this: it is platform-dependent — a colon is refused on
 * Windows and accepted on Linux — so `a:b` would resolve to two different directories on the two
 * sides of one installation, and the symptom is a token written where the server does not read it.
 * Four reviewers found that independently. A rule simple enough to write twice without drifting is
 * the point.</p>
 */
function isSafeSide(side: string): boolean {
  return side !== '.' && side !== '..' && /^[a-z0-9._-]+$/u.test(side);
}
