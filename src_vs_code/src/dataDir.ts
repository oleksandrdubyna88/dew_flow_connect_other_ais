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
