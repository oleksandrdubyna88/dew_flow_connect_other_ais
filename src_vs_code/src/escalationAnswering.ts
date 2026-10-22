import { answerJson } from './escalationAnswer';
import { answerPaths } from './escalationDirs';
import { type Escalation, needsADecision } from './escalations';

/**
 * Answering one card, as a unit a test can drive: who is asked, what is written, and where — without a window.
 *
 * <p>This was the body of `EscalationWatcher.answerCommand`, where the only way to see it work was to click. The watcher now builds the three
 * things that need the editor or the disk ({@link AnswerFiles}, the dialog, the clock) and hands them here, so a scenario such as "an
 * informational card is dismissed" is a test that fails when the dialog is shown or the file is not beside the card.</p>
 */

/** What a person chose, as the answer file records it. */
export interface Picked {
  readonly decision: string;
  readonly text: string;
}

/**
 * An informational card's dismissal answer: decision `""` and the text `"dismissed"`.
 *
 * <p>The convention: a card of kind `info` is told, not asked — the round that wrote it has returned and no hold is bound to it — so its one
 * button, Dismiss, writes this answer WITHOUT asking anything. The file exists only to take the card off the screen (an answered card is not
 * open); <b>the server never reads it</b>. Any other reader may rely on `decision: ""` + `text: "dismissed"` meaning "dismissed", and on nothing else.</p>
 */
export const DISMISSED: Picked = { decision: '', text: 'dismissed' };

/** The disk, as answering needs it. Node's calls, never VS Code's: those make missing parent folders, and an answer must not (E5.7, D3 of paths per side). */
export interface AnswerFiles {
  /** Whether an answer is already sitting at the path. A read that throws is "no answer", not a crash. */
  exists(path: string): Promise<boolean>;
  write(path: string, bytes: Uint8Array): Promise<void>;
  rename(from: string, to: string): Promise<void>;
}

/** Everything answering needs from outside. */
export interface AnswerDeps {
  /** This window's own data directory (a native filesystem path); a card read elsewhere is answered beside itself. */
  readonly dataDir: string;
  readonly files: AnswerFiles;
  /** The dialog for a card that asks for a decision: the person's pick, or `undefined` when they closed it. Never called for an informational card. */
  readonly ask: (escalation: Escalation) => Promise<Picked | undefined>;
  readonly now: () => string;
}

/** What happened, for the caller to say: the watcher turns each into the notification it deserves. */
export type AnswerOutcome =
  | { readonly kind: 'closed' }
  | { readonly kind: 'refused-id' }
  | { readonly kind: 'already-answered' }
  | { readonly kind: 'written'; readonly target: string }
  | { readonly kind: 'failed'; readonly dir: string; readonly reason: unknown };

/** The pick for a card: asked of the person for a card that asks for a decision, the fixed dismissal for one that does not. */
export async function chooseAnswer(escalation: Escalation, ask: AnswerDeps['ask']): Promise<Picked | undefined> {
  return needsADecision(escalation) ? ask(escalation) : DISMISSED;
}

/**
 * Puts the decision in front of the person and writes what they chose, BESIDE the question.
 *
 * <p>Where those paths are is {@link answerPaths}: the directory comes from the question, and the temp is beside the target because a rename
 * across filesystems throws EXDEV. FIRST WRITER WINS — checked immediately before the write, since the gap between discovery and the write is
 * exactly where another window answers. A failure to write is returned, never swallowed: the question stays open.</p>
 */
export async function answerEscalation(escalation: Escalation, deps: AnswerDeps): Promise<AnswerOutcome> {
  const chosen = await chooseAnswer(escalation, deps.ask);
  if (chosen === undefined) {
    return { kind: 'closed' }; // dismissing the dialog is not answering; the question stays open
  }

  const paths = answerPaths(escalation.id, deps.dataDir, escalation.from);
  if (paths === undefined) {
    return { kind: 'refused-id' };
  }

  return writeBeside(paths, escalation.id, chosen, deps);
}

async function writeBeside(
  paths: NonNullable<ReturnType<typeof answerPaths>>,
  id: string,
  chosen: Picked,
  deps: AnswerDeps,
): Promise<AnswerOutcome> {
  const bytes = new TextEncoder().encode(answerJson(id, chosen.text.trim(), deps.now(), chosen.decision));
  try {
    if (await deps.files.exists(paths.target)) {
      return { kind: 'already-answered' };
    }
    await deps.files.write(paths.temp, bytes);
    await deps.files.rename(paths.temp, paths.target);

    return { kind: 'written', target: paths.target };
  } catch (reason) {
    return { kind: 'failed', dir: paths.dir, reason };
  }
}
