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
