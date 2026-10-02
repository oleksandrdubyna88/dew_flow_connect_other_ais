/**
 * The escalation half of the extension: what the server has asked, and what has been answered.
 *
 * <p>Pure — the file watching and the dialogs live in `extension.ts`. Everything that decides
 * *whether* to raise a modal, *what* the status bar says, and *how* an answer is written is here,
 * so it is a test rather than something discovered when a round hangs.</p>
 */

export interface EscalationFinding {
  readonly severity: string;
  readonly category: string;
  readonly file: string | null;
  readonly line: number | null;
  readonly title: string;
}

/** One consultant row's answer as the server folds it under a card (S3 of the question consultant, D8). */
export interface EscalationAdvice {
  readonly rowId: string;
  readonly vendor: string;
  readonly model: string;
  readonly promptTitle: string;
  readonly capability: string;
  /** D13's caveat on the pair — `unconfined`, `default-deny` — or empty. Shown beside the advice. */
  readonly flag: string;
  readonly status: string;
  readonly reason: string;
  readonly advice: string;
}

/** The one status word a question file can carry besides none: the server marked it after its wait ran out (A10). */
export const EXPIRED = 'expired';

export interface Escalation {
  readonly id: string;
  readonly sessionId: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly question: string;
  readonly openFindings: readonly EscalationFinding[];
  readonly askedUtc: string;
  readonly questionOriginal?: string;
  readonly language?: string;
  readonly translationNote?: string;
  /**
   * Empty or absent while the question is open; `expired` once the server's wait ran out (S3 of the
   * question consultant, A10) — the AI was told to ask in the chat, so the card leaves the active set
   * and stays only for the log. Every file written before S3 has no status and is open.
   */
  readonly status?: string;
  readonly expiredUtc?: string;
  /** The `ask_consultants` reply this question followed, verified by the server. */
  readonly consultId?: string;
  /** D8: the person was asked at once and the consultants ran beside; their answers are under `consultantAnswers`. */
  readonly productionRisk?: boolean;
  readonly riskReason?: string;
  readonly consultantAnswers?: readonly EscalationAdvice[];
  /**
   * The directory this question was READ from, so its answer goes back beside it.
   *
   * <p>Not a field the server writes — the file's content does not carry it. It is what the reader
   * knows about where it found the file, which is why `parseEscalation` takes it rather than parsing
   * it.</p>
   *
   * <p>It exists because a window can watch more than one installation's questions: a Claude Code
   * session inside WSL writes into the WSL store, and the Windows window answering it must write
   * back into that same store, because the server that asked polls nowhere else. An answer written
   * into the answering window's own directory leaves the round blocked for ever, having been
   * answered.</p>
   */
  readonly from?: string;
}

/**
 * Parse one question file; anything that is not a question is skipped, never guessed at.
 *
 * @param from the directory it was read from, carried onto the question so its answer can go back
 *   beside it. Omitted by callers that read only this window's own directory.
 */
export function parseEscalation(text: string, from = ''): Escalation | undefined {
  try {
    const parsed = JSON.parse(text) as Partial<Escalation>;
    if (typeof parsed.id !== 'string' || typeof parsed.question !== 'string' || parsed.question.length === 0) {
      return undefined;
    }
    // `from` LAST, so a file that carries a `from` of its own cannot decide where its answer is
    // written. The reader knows where it read; the file does not get a say in it.
    return { openFindings: [], ...parsed, ...(from.length > 0 ? { from } : {}) } as Escalation;
  } catch {
    return undefined;
  }
}


/**
 * Whether a question is still OPEN — the one thing the watcher asks before it shows a card.
 *
 * <p>A file without a status, or with an empty one, is open: that is every file written before the
 * status existed. Only the server's own word closes it; an answer file beside the question is the
 * other closer, and the watcher reads that from the directory, not from here.</p>
 */
export function isOpenEscalation(escalation: Escalation): boolean {
  return (escalation.status ?? '') !== EXPIRED;
}

/**
 * What the status bar says. Empty means hide it: a status-bar item that says "0" is furniture.
 */
export function statusBarText(openCount: number): string {
  if (openCount === 0) {
    return '';
  }
  return openCount === 1
    ? '$(question) ConnectOtherAIs: 1 question'
    : `$(question) ConnectOtherAIs: ${openCount} questions`;
}

/**
 * Whether to raise a modal for this escalation now.
 *
 * <p>Once per id per window: a modal that reappears every poll cannot be dismissed, and a person
 * who deliberately closed it to look at the code should not be fighting it. The question stays in
 * the status bar and the list, which is what makes dismissing safe.</p>
 */
export function shouldPrompt(id: string, alreadyPrompted: ReadonlySet<string>, answered: boolean): boolean {
  return !answered && !alreadyPrompted.has(id);
}

/** The modal's body: the question, then what is still gating, then how to answer. */
export function modalText(escalation: Escalation): string {
  const where = `${escalation.branch} — ${escalation.repoPath}`;
  if (escalation.openFindings.length === 0) {
    return `${escalation.question}\n\n(${where})`;
  }
  const findings = escalation.openFindings
    .map((f) => `• ${f.severity}/${f.category} ${f.file ? `${f.file}:${f.line ?? ''} ` : ''}— ${f.title}`)
    .join('\n');
  return `${escalation.question}\n\nStill gating:\n${findings}\n\n(${where})`;
}
