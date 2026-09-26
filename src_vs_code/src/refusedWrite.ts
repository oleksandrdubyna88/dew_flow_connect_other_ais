/**
 * Whether a control whose write VS Code refused is repainted to what is stored — a checkbox, not a text field.
 *
 * <p>2026-09-25: the extension updated in place, the window had not caught up, the tick on *Stop the local
 * reviewer…* was refused — and the box stayed ticked, so "saved" was on screen with nothing saved, and the
 * next morning's window read it as off. A box has nothing typed to lose, so it snaps back. A text field or a
 * select is NOT repainted: that would wipe what the person was writing, which `settledWrites` exists to
 * prevent.</p>
 */
export function snapsBackWhenRefused(value: unknown): boolean {
  return typeof value === 'boolean';
}

/** What one plain setting write does, handed in so the ORDER can be run and observed without VS Code. */
export interface PlainWriteSteps {
  /** Saves one key; true once saved, false once VS Code refused it (the refusal already reported). */
  save(key: string, value: unknown): Promise<boolean>;
  /** Redraws the panel from what is stored. */
  repaint(): Promise<void>;
  /** What follows a write that stands — the per-side seeding and carrying. */
  follow(): Promise<void>;
}

/**
 * One plain setting write, in its order: what it invalidates is cleared first, then the key itself; a
 * refused box snaps back and STOPS — a per-side switch that was never saved must not seed or carry — and
 * anything else goes on to what follows a write.
 *
 * <p>Its own function so the order is RUN in a test: the host imports `vscode`, and a regex over its text
 * cannot see a missing `return` (the gate's code round, 2026-09-26).</p>
 */
export async function writePlain(
  key: string,
  value: unknown,
  cleared: readonly string[],
  steps: PlainWriteSteps): Promise<void> {
  for (const stale of cleared) {
    await steps.save(stale, '');
  }
  const saved = await steps.save(key, value);
  if (!saved && snapsBackWhenRefused(value)) {
    await steps.repaint();
    return;
  }
  await steps.follow();
}
