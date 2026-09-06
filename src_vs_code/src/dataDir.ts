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

  return configured ?? `${localAppData}/coai-mcp`;
}
