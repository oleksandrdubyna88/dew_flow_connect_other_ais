/**
 * Putting TEXT on the clipboard, and saying what happened.
 *
 * <p>Pure of `vscode`, and that is the point rather than a habit: the interesting paths here are the
 * ones that FAIL, and a rule living inside the panel host is a rule no test can reach. The clipboard
 * and the status bar arrive as two functions, so a test can make the write reject and watch what the
 * person is told.</p>
 *
 * <p><b>This is the half that `phraseCopy.ts` and the chat's block controls share.</b> It was
 * extracted rather than copied when the second caller arrived, because the two problems it solves are
 * not obvious and a second implementation would have been written without them — the ordering of two
 * presses, and the stacking of confirmations. Both are recorded on the functions below. Neither
 * caller may keep its own copy of this; what belongs to a caller is only which text to copy and what
 * to say about it.</p>
 */

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
  /** True only when the write RESOLVED. A page flips a control to *Copied* on this and nothing else. */
  readonly copied: boolean;
  readonly said: string;
}

/**
 * What a caller decided: copy this and say that, or refuse and say why.
 *
 * <p>A decision rather than a `string | undefined`, so that a refusal always arrives WITH its
 * sentence. The shape is what stops a caller returning nothing and leaving the person told nothing —
 * which, for a copy, means pasting whatever was on the clipboard before.</p>
 */
export type CopyDecision =
  | { readonly kind: 'copy'; readonly text: string; readonly done: string }
  | { readonly kind: 'refused'; readonly said: string };

/** How long a confirmation stays on the status bar. Long enough to notice, short enough to ignore. */
export const SAID_FOR_MS = 3000;

export interface TextCopier {
  copy(decide: () => CopyDecision): Promise<CopyReport>;
}

/**
 * A copier that keeps ONE line on the status bar and writes in the order it was asked.
 *
 * <p><b>One line.</b> `setStatusBarMessage` returns a disposable, and five copies in five seconds
 * would otherwise leave five of them competing over the same strip. The previous one is disposed
 * before the next is set, so what is there is always the last thing that happened.</p>
 *
 * <p><b>In order.</b> Two presses start two asynchronous writes, and whichever RESOLVES last is what
 * the clipboard ends up holding — so pressing A and then B could leave A on it, which is the one
 * thing a copy control must never do. Chaining them makes the last press the last write.</p>
 *
 * <p>The decision is taken as a FUNCTION, evaluated when its turn comes rather than when the press
 * arrived: what a caller resolves may depend on state a press ahead of it in the queue changed.</p>
 */
export function textCopier(ports: CopyPorts): TextCopier {
  let live: Said | undefined;
  let working: Promise<unknown> = Promise.resolve();

  function tell(said: string): void {
    live?.dispose();
    live = ports.say(said, SAID_FOR_MS);
  }

  async function once(decide: () => CopyDecision): Promise<CopyReport> {
    const decision = decide();
    if (decision.kind === 'refused') {
      tell(decision.said);

      return { copied: false, said: decision.said };
    }
    try {
      await ports.writeText(decision.text);
    } catch {
      // The write is the only thing that can fail here, and a person who is told nothing pastes
      // whatever was on the clipboard before — which is the failure this sentence exists to stop.
      const refused = 'It could not be copied — the clipboard is held by another program.';
      tell(refused);

      return { copied: false, said: refused };
    }
    tell(decision.done);

    return { copied: true, said: decision.done };
  }

  return {
    copy(decide): Promise<CopyReport> {
      // Chained onto whatever is in flight, and onto its FAILURE too — a refused write must not
      // break the queue for the press after it.
      const next = working.then(() => once(decide), () => once(decide));
      working = next;

      return next;
    },
  };
}
