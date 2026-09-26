import type { ControlKind } from './settingRoute';

/**
 * Whether a control whose write VS Code refused is repainted to what is stored — a checkbox or a dropdown,
 * never a text field.
 *
 * <p>2026-09-25: the extension updated in place, the window had not caught up, the tick on *Stop the local
 * reviewer…* was refused — and the box stayed ticked, so "saved" was on screen with nothing saved, and the
 * next morning's window read it as off. A box has nothing typed to lose, so it snaps back; nor has a
 * dropdown, which the page names as `control: 'select'` because its value is a string like any other. A
 * text field is NOT repainted: that would wipe what the person was writing, which `settledWrites` exists to
 * prevent.</p>
 */
export function snapsBackWhenRefused(value: unknown, control?: ControlKind): boolean {
  return typeof value === 'boolean' || control === 'select';
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
  steps: PlainWriteSteps,
  control?: ControlKind): Promise<void> {
  for (const stale of cleared) {
    await steps.save(stale, '');
  }
  if (await saveOrSnapBack(() => steps.save(key, value), () => steps.repaint(), value, control)) {
    return;
  }
  await steps.follow();
}

/**
 * One save, and the snap-back when it was refused — the ONE place every kind of write decides it, so no kind
 * keeps the gap the plain one had. True when it snapped back, which is where a caller stops.
 */
export async function saveOrSnapBack(
  save: () => Promise<boolean>,
  repaint: () => Promise<void>,
  value: unknown,
  control?: ControlKind): Promise<boolean> {
  if (await save() || !snapsBackWhenRefused(value, control)) {
    return false;
  }
  await repaint();

  return true;
}

/**
 * A repaint STARTED, never awaited, by the write that asks for it — it runs once the queue has settled.
 *
 * <p>`render()` waits for the write queue before it reads anything, and a write asking for the repaint is
 * still IN that queue: awaiting it there is a wait on itself. From PR #561 until this was found on review, a
 * refused box froze the panel that way until the window was reloaded, and every later write queued behind
 * it. Started instead, the repaint waits for this write and the ones after it, then paints what is stored.</p>
 */
export function afterTheWrite(repaint: () => Promise<void>): () => Promise<void> {
  return () => {
    // Said, because nothing upstream can catch it: the write that started it has already returned.
    repaint().catch((error: unknown) => {
      console.error('ConnectOtherAIs: the panel could not be repainted after a refused write', error);
    });

    return Promise.resolve();
  };
}
