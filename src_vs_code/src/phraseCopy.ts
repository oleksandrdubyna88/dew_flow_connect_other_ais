import { phraseById, type Phrase } from './phrases';

/**
 * Putting a phrase on the clipboard, and saying what happened.
 *
 * <p>Pure of `vscode`, and that is the point rather than a habit: the interesting paths here are the
 * ones that FAIL, and a rule living inside the panel host is a rule no test can reach. The clipboard
 * and the status bar arrive as two functions, so a test can make the write reject and watch what the
 * person is told. (Code round on the story's plan, codex: every listed test resolved phrase data, so
 * an implementation could report success before the write landed and pass all of them.)</p>
 *
 * <p><b>Nothing here claims success on a path that did not succeed.</b> Three outcomes and three
 * sentences: the phrase went to the clipboard, the id named no row, or the write was refused. The
 * button in the panel only says *Copied* for the first, because the host tells it so afterwards
 * rather than the page assuming it on the click.</p>
 */

export type CopyDecision =
  | { readonly kind: 'copy'; readonly phrase: Phrase }
  | { readonly kind: 'refused'; readonly said: string };

/** Which phrase a button named, or the sentence to show instead. */
export function phraseToCopy(phrases: readonly Phrase[], id: string | undefined): CopyDecision {
  const phrase = id === undefined ? undefined : phraseById(phrases, id);
  if (phrase !== undefined) {
    return { kind: 'copy', phrase };
  }

  return {
    kind: 'refused',
    said: phrases.length === 0
      ? 'There are no phrases yet — add one with Edit phrases.'
      : 'That phrase is not in the list any more. The panel will catch up in a moment.',
  };
}

/** What a message on the status bar must be able to do: go away. */
export interface Said {
  dispose(): void;
}

export interface CopyPorts {
  /** `vscode.env.clipboard.writeText`. It REJECTS — a clipboard held elsewhere, a session without one. */
  readonly writeText: (text: string) => Promise<void>;
  /** `vscode.window.setStatusBarMessage`, which answers with the handle that removes the line again. */
  readonly say: (message: string, forMs: number) => Said;
}

export interface CopyReport {
  /** True only when the write RESOLVED. The page flips a button to *Copied* on this and nothing else. */
  readonly copied: boolean;
  readonly said: string;
}

/** How long a confirmation stays on the status bar. Long enough to notice, short enough to ignore. */
const SAID_FOR_MS = 3000;

/**
 * A copier that keeps ONE line on the status bar.
 *
 * <p>`setStatusBarMessage` returns a disposable, and five phrases copied in five seconds would
 * otherwise leave five of them competing over the same strip. The previous one is disposed before
 * the next is set, so what is there is always the last thing that happened.</p>
 */
export function phraseCopier(ports: CopyPorts): { copy(phrases: readonly Phrase[], id: string | undefined): Promise<CopyReport> } {
  let live: Said | undefined;
  /**
   * One write at a time, in the order the buttons were pressed.
   *
   * <p>Two presses start two asynchronous writes, and whichever RESOLVES last is what the clipboard
   * ends up holding — so pressing A and then B could leave A on it, which is the one thing a copy
   * button must never do. Chaining them makes the last press the last write. (Code round, codex.)</p>
   */
  let working: Promise<unknown> = Promise.resolve();

  function tell(said: string): void {
    live?.dispose();
    live = ports.say(said, SAID_FOR_MS);
  }

  async function once(phrases: readonly Phrase[], id: string | undefined): Promise<CopyReport> {
    const decision = phraseToCopy(phrases, id);
    if (decision.kind === 'refused') {
      tell(decision.said);

      return { copied: false, said: decision.said };
    }
    try {
      await ports.writeText(decision.phrase.text);
    } catch {
      // The write is the only thing that can fail here, and a person who is told nothing pastes
      // whatever was on the clipboard before — which is the failure this sentence exists to stop.
      const refused = 'The phrase could not be copied — the clipboard is held by another program.';
      tell(refused);

      return { copied: false, said: refused };
    }
    const done = `Copied “${decision.phrase.name}” — paste it with Ctrl+V.`;
    tell(done);

    return { copied: true, said: done };
  }

  return {
    copy(phrases, id): Promise<CopyReport> {
      // Chained onto whatever is in flight, and onto its FAILURE too — a refused write must not
      // break the queue for the press after it.
      const next = working.then(() => once(phrases, id), () => once(phrases, id));
      working = next;

      return next;
    },
  };
}
