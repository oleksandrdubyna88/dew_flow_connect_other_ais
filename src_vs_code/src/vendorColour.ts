/**
 * One colour per vendor, the same colour everywhere that vendor is named.
 *
 * <p>In `codex/PlanCritique — running` the one word that says WHO was the same grey as the rest of
 * the row. A colour makes a list of nine reviewers scannable — you find your vendor by colour before
 * you have read anything.</p>
 *
 * <p><b>Derived from the name, never assigned in arrival order.</b> A palette handed out as rows
 * appear would give `codex` one colour in the rounds list and another in the spending chart, and a
 * different one again after a restart — which is worse than no colour, because it teaches a mapping
 * that then lies. A hash of the name is stable across every view, every window and every release.</p>
 *
 * <p>The palette is the editor's own chart colours, so the result is legible in a light theme as
 * well as a dark one. Nothing here is a hex value: a hard-coded colour is a colour that will one day
 * be invisible on somebody's background.</p>
 */
const PALETTE: readonly string[] = [
  'var(--vscode-charts-blue)',
  'var(--vscode-charts-green)',
  'var(--vscode-charts-orange)',
  'var(--vscode-charts-purple)',
  'var(--vscode-charts-red)',
  'var(--vscode-charts-yellow)',
];

/**
 * The vendors this product ships with, pinned.
 *
 * <p>Not decoration: a plain hash put `codex`, `gemini` and `local` into TWO colours out of six —
 * the three vendors the whole feature exists for, indistinguishable. Caught by its own test on the
 * first run. Anchoring them is the difference between a palette that works for the case everybody
 * sees and one that works on average.</p>
 */
const ANCHORED: Readonly<Record<string, string>> = {
  codex: 'var(--vscode-charts-blue)',
  gemini: 'var(--vscode-charts-green)',
  local: 'var(--vscode-charts-orange)',
};

/** The CSS colour for a vendor's name. Case and surrounding space do not make a second vendor. */
export function vendorColour(vendor: string): string {
  const name = vendor.trim().toLowerCase();
  if (name.length === 0) {
    return 'var(--vscode-foreground)';
  }
  if (ANCHORED[name] !== undefined) {
    return ANCHORED[name]!;
  }

  // FNV-1a, 32-bit, unsigned throughout: `>>> 0` after the multiply keeps it out of the sign bit,
  // where a negative modulus would index off the front of the palette.
  let hash = 0x811c9dc5;
  for (let at = 0; at < name.length; at += 1) {
    hash = (hash ^ name.charCodeAt(at)) >>> 0;
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }

  return PALETTE[hash % PALETTE.length] ?? 'var(--vscode-foreground)';
}

/** Every colour this can return — what a test asserts against, rather than a repeated literal. */
export const VENDOR_PALETTE = PALETTE;
