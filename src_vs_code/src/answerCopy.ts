import { answerBlocks, signatureOf } from './renderAnswer';
import type { CopyDecision, CopyReport } from './copyText';

/**
 * What a copy control on an answer takes, and what is said about it.
 *
 * <p>Pure of `vscode`, for the reason [phraseCopy.ts](phraseCopy.ts) gives and this file needed
 * first: the whole of the interesting behaviour here is REFUSAL, and the host's hooks live inside a
 * closure over `vscode` that no test in this repository imports. A rule written there is a rule
 * nothing can reach — which is how the plan for this feature came to specify an end-to-end test that
 * could not have been run.</p>
 *
 * <p><b>What is copied is the HOST's own stored markdown</b>, never anything the page sent. The page
 * names a position and echoes a signature; both are claims, and neither becomes text. The worst a
 * lie can do here is refuse a copy.</p>
 */

/** Said for both ways an ordinal can fail, because from the person's side they are one fact. */
const GONE = 'That block is no longer part of this answer.';

/**
 * Which block a control named, or the sentence to show instead.
 *
 * <p>Two refusals, one sentence. The ordinal can be past the end — an answer the store has since
 * truncated — and the SIGNATURE can disagree, which is the case a range check cannot see: a message
 * rewritten in place to a different text with the same number of blocks. Messages are only appended
 * to today, so the second cannot happen; it is guarded because a position outlives the text under it
 * and two reviewers said, correctly, that a caveat in a plan is not a guard.</p>
 *
 * <p><b>What the signature covers, exactly: the WHOLE stored markdown of that message</b> — every
 * byte of `messages[index].text` as it stands, not the selected block and not the block count.
 * `signatureOf` is given the same string `renderAnswer` was given when it drew the control, and both
 * sides call that one function. Signing the block alone would accept a control after a paragraph
 * somewhere else in the answer changed, which is the promise this exists to keep rather than an edge
 * of it. (codex, the plan round, asked for this to be stated rather than left to the implementation.)</p>
 *
 * <p>It is not a secret and not a defence against a forged message — a forged one can only cause a
 * refusal — it is a check that the control was drawn for the text now being read.</p>
 */
export function blockToCopy(markdown: string, block: number, signature: string): CopyDecision {
  if (signatureOf(markdown) !== signature) {
    return { kind: 'refused', said: GONE };
  }
  const blocks = answerBlocks(markdown);
  const one = blocks[block];
  if (one === undefined) {
    return { kind: 'refused', said: GONE };
  }

  return {
    kind: 'copy',
    text: one.text,
    done: one.reply
      ? 'Copied the reply prompt — paste it with Ctrl+V.'
      : 'Copied the block — paste it with Ctrl+V.',
    failed: one.reply
      ? 'The reply prompt could not be copied — the clipboard is held by another program.'
      : 'The block could not be copied — the clipboard is held by another program.',
  };
}

/**
 * The whole answer, for the control that has always copied it.
 *
 * <p>Here so that path gets the same failure handling as the new one. It wrote with a bare `void` and
 * no catch, so a refused clipboard was silent — and a person told nothing pastes whatever was there
 * before. One site fixed and the other left is the defect this family keeps writing, and there would
 * have been exactly two of them the moment the block control landed. (codex, the plan round.)</p>
 */
export function answerToCopy(markdown: string): CopyDecision {
  if (markdown.length === 0) {
    return { kind: 'refused', said: 'There is nothing in this answer to copy.' };
  }

  return {
    kind: 'copy',
    text: markdown,
    done: 'Copied the answer — paste it with Ctrl+V.',
    failed: 'The answer could not be copied — the clipboard is held by another program.',
  };
}

/** Where a copy control sits: which message, which block of it, and what it was drawn against. */
export interface CopiedControl {
  readonly index: number;
  /** Absent for the control that copies the whole answer. */
  readonly block?: number;
  readonly sig: string;
}

/**
 * Whether to tell the page a copy landed — and it is a function so that a test can ask.
 *
 * <p>The rule is one line and the reason it is not written inline is the one this file already
 * exists for: the hooks live inside a closure over `vscode` in `chatCommand.ts`, which no test in
 * this repository can import. A rule written there is a rule nothing can reach, and the plan for the
 * block-copy control learned that by specifying an end-to-end test that could not have been run.</p>
 *
 * <p><b>A copy that did not land is acknowledged with nothing.</b> `copied` is true only when the
 * clipboard write RESOLVED; a refusal, and a clipboard held by another program, both come back
 * false. The sentence `copyText.ts` has already put in the status bar is then the only thing the
 * person sees, which is right — a tick beside the control would be a claim about where their next
 * paste is coming from, and it would be wrong.</p>
 */
export function acknowledgement(report: CopyReport, where: CopiedControl): CopiedControl | undefined {
  return report.copied ? where : undefined;
}
