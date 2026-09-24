import { withLocalPaths, type Imported } from './configTransfer';

/**
 * Applying an imported config ALL OR NOTHING — issue #467, the plan round (local, codex, gemini).
 *
 * <p>Everything the import will touch is read BEFORE the first write: the base value of every setting (or
 * that it had none) and the text of every prompt (or that there was no file). A write that fails puts all
 * of it back, so a half-applied config — some roles' texts from the file, the rest from before — is never
 * what a person is left with. A restore that fails too is named, never swallowed.</p>
 *
 * <p>No `vscode` here: the host hands the reads and writes in, which is what lets the test fail a write
 * on purpose.</p>
 */

export interface ApplyIo {
  /** The setting's BASE value, or undefined when it was never set. */
  readonly baseValue: (key: string) => unknown;
  /** Writes the base value; undefined removes it (back to the default). */
  readonly writeSetting: (key: string, value: unknown) => Promise<void>;
  /** The prompt's text, or undefined when there is no file. */
  readonly readPrompt: (id: string) => Promise<string | undefined>;
  readonly writePrompt: (id: string, text: string) => Promise<void>;
  readonly removePrompt: (id: string) => Promise<void>;
}

export type Applied =
  | { readonly ok: true; readonly settings: number; readonly prompts: number }
  | { readonly ok: false; readonly failed: string; readonly why: string; readonly notRestored: readonly string[] };

interface Step {
  readonly name: string;
  readonly apply: () => Promise<void>;
  readonly restore: () => Promise<void>;
}

export async function applyImport(imported: Extract<Imported, { ok: true }>, io: ApplyIo): Promise<Applied> {
  const steps = [...settingSteps(imported.settings, io), ...await promptSteps(imported.prompts, io)];
  const done: Step[] = [];
  for (const step of steps) {
    try {
      await step.apply();
      done.push(step);
    } catch (error) {
      // The failed step may have half-written; restoring it too is harmless and is the safe side.
      return { ok: false, failed: step.name, why: messageOf(error), notRestored: await restored([...done, step]) };
    }
  }

  return { ok: true, settings: Object.keys(imported.settings).length, prompts: Object.keys(imported.prompts).length };
}

function settingSteps(settings: Readonly<Record<string, unknown>>, io: ApplyIo): Step[] {
  return Object.entries(settings).map(([key, value]) => {
    const before = io.baseValue(key);

    return {
      name: `setting ${key}`,
      apply: () => io.writeSetting(key, withLocalPaths(value, before)),
      restore: () => io.writeSetting(key, before),
    };
  });
}

async function promptSteps(prompts: Readonly<Record<string, string>>, io: ApplyIo): Promise<Step[]> {
  const steps: Step[] = [];
  for (const [id, text] of Object.entries(prompts)) {
    const before = await io.readPrompt(id);
    steps.push({
      name: `prompt ${id}`,
      apply: () => io.writePrompt(id, text),
      restore: () => (before === undefined ? io.removePrompt(id) : io.writePrompt(id, before)),
    });
  }

  return steps;
}

/** Puts every step back, newest first; answers the names whose restore failed as well. */
async function restored(steps: readonly Step[]): Promise<string[]> {
  const failed: string[] = [];
  for (const step of [...steps].reverse()) {
    try {
      await step.restore();
    } catch {
      failed.push(step.name);
    }
  }

  return failed;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** The sentence a failed import ends with. */
export function failureSentence(applied: Extract<Applied, { ok: false }>): string {
  const back = applied.notRestored.length === 0
    ? 'Everything it had changed was put back.'
    : `These could not be put back and need a look: ${applied.notRestored.join(', ')}.`;

  return `The import stopped at ${applied.failed} (${applied.why}). ${back}`;
}
