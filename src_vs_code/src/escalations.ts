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

export interface Escalation {
  readonly id: string;
  readonly sessionId: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly question: string;
  readonly openFindings: readonly EscalationFinding[];
  readonly askedUtc: string;
}

/** Parse one question file; anything that is not a question is skipped, never guessed at. */
export function parseEscalation(text: string): Escalation | undefined {
  try {
    const parsed = JSON.parse(text) as Partial<Escalation>;
    return typeof parsed.id === 'string' && typeof parsed.question === 'string' && parsed.question.length > 0
      ? ({ openFindings: [], ...parsed } as Escalation)
      : undefined;
  } catch {
    return undefined;
  }
}

/** The answer file's content, exactly as the server parses it. */
export function answerJson(id: string, answer: string, nowUtc: string): string {
  return JSON.stringify({ id, answer, answeredUtc: nowUtc }, null, 2);
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

/** One markdown section per open question, for the rounds view. */
export function renderEscalations(open: readonly Escalation[]): string {
  if (open.length === 0) {
    return '';
  }
  const sections = open
    .map((e) => {
      const findings = e.openFindings
        .map((f) => `  - \`${f.severity}\` ${f.category} ${f.file ? `\`${f.file}:${f.line ?? ''}\`` : ''} — ${f.title}`)
        .join('\n');
      return (
        `### ${e.branch} · asked ${e.askedUtc}\n\n${e.question}\n\n` +
        (findings.length > 0 ? `Still gating:\n${findings}\n` : '_No findings attached._\n')
      );
    })
    .join('\n');
  return `## Open questions — a review is waiting on you\n\n${sections}\n`;
}
