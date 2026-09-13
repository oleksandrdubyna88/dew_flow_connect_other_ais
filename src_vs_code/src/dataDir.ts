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
  const configured = process.env['COAI_DATA_DIR'];
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;

  if (configured === undefined) {
    return `${localAppData}/coai-mcp`;
  }

  // A chosen directory can be PARTITIONED per side, so two installations — Windows and WSL — can be
  // pointed at one NAS without writing the same SQLite file (issue #115). The rule is the server's,
  // `PanelSettings.ResolveDataDir`, and this is the half that must agree with it: the paragraph
  // above is about exactly this file writing a token the shim then reads.
  //
  // The side is a NAME, never derived, and that is why the two halves can agree at all: deriving it
  // would mean computing one string twice, here from `os.hostname()` and there from
  // `Environment.MachineName`, which differ in case and in whether they carry a domain.
  const side = pathSafeSide(process.env['COAI_DATA_SIDE']);

  return side.length === 0 ? configured : `${configured}/${side}`;
}

/**
 * One path segment, or empty — the server's own rule, spelled the same way.
 *
 * <p>Refused rather than rewritten: two different names sanitised into one would put two sides on
 * one database, which is the outcome the partition exists to prevent.</p>
 */
function pathSafeSide(value: string | undefined): string {
  const trimmed = (value ?? '').trim().toLowerCase();

  return trimmed.length === 0
    || trimmed === '.'
    || trimmed === '..'
    || /[/\\:*?"<>|]/.test(trimmed)
    ? ''
    : trimmed;
}
