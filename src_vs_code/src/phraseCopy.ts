import { textCopier, type CopyPorts, type CopyReport, type Said } from './copyText';
import { phraseById, type Phrase } from './phrases';

export type { CopyPorts, CopyReport, Said };

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

/**
 * A copier for phrases.
 *
 * <p>What is a PHRASE's is here: which row a button named, and the sentence said about it. Everything
 * else — one line on the status bar, one write at a time in the order the buttons were pressed, and
 * what to say when the clipboard refuses — belongs to [copyText.ts](copyText.ts) and is shared with
 * the chat's block controls. It was extracted there when the second caller arrived rather than
 * copied, because both of those behaviours exist for reasons a second implementation would not have
 * known to reproduce.</p>
 */
export function phraseCopier(ports: CopyPorts): { copy(phrases: readonly Phrase[], id: string | undefined): Promise<CopyReport> } {
  const copier = textCopier(ports);

  return {
    copy(phrases, id): Promise<CopyReport> {
      return copier.copy(() => {
        const decision = phraseToCopy(phrases, id);

        return decision.kind === 'refused'
          ? decision
          : {
            kind: 'copy',
            text: decision.phrase.text,
            done: `Copied “${decision.phrase.name}” — paste it with Ctrl+V.`,
            // Its OWN sentence, unchanged by the extraction: a phrase that could not be copied still
            // says it was a phrase. (codex, the code round.)
            failed: 'The phrase could not be copied — the clipboard is held by another program.',
          };
      });
    },
  };
}
