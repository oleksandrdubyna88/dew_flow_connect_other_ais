import { mayCarryAKey } from './bugsAdminApi';
import { type BugCorpus, type SendRun, sending } from './roundsDb';

/**
 * What a send is, before anything is spawned and after it comes back.
 *
 * <p>The decisions live here rather than in the panel for the standing reason: `panelProvider.ts`
 * imports `vscode` and no test in this repository can load it. What is left there is the wiring.</p>
 */

/** The exit codes `coai-mcp` answers, named where they are read. */
export const EXITS = {
  /** Everything the run offered was answered for. */
  fine: 0,
  /** A mode this binary has never heard of — an installed server older than the send path. */
  tooOld: 64,
  /** Bad arguments: no `--server`, no key, or an address that must not carry one. */
  refusedTheRequest: 65,
  /** The transport failed. Nothing is wrong with the pairs; try again. */
  couldNotReach: 69,
  /** The local database could not be opened. */
  noDatabase: 74,
} as const;

/**
 * Why a send cannot even be attempted.
 *
 * <p>Checked BEFORE the child is built, which is the point: an address that must not carry a key
 * must not have one put into a process environment and then be refused by the CLI. Both halves
 * refusing is deliberate — one of them is the one the person sees — but only this one runs before
 * the credential moves. (Plan round, codex.)</p>
 */
export type Refusal =
  | { readonly kind: 'no-server'; readonly why: string }
  | { readonly kind: 'unsafe-server'; readonly why: string }
  | { readonly kind: 'no-key'; readonly why: string }
  | { readonly kind: 'nothing-to-send'; readonly why: string }
  | { readonly kind: 'already-sending'; readonly why: string };

/** What the panel knows before it decides whether to send. */
export interface BeforeSending {
  readonly server: string;
  readonly key: string;
  readonly corpus: BugCorpus;
}

/**
 * Whether this send may start, and why not when it may not.
 *
 * <p>Ordered so the answer is the most actionable one: an address nobody set is a different sentence
 * from an address that cannot carry a credential, and both are different from a key nobody has
 * entered. A single "bad arguments" would send a person to read the CLI's source.</p>
 */
export function mayStart(before: BeforeSending): Refusal | undefined {
  return firstOf([
    () => address(before.server),
    () => credential(before.key),
    () => queue(before.corpus),
  ]);
}

/** The first refusal there is, or none. */
function firstOf(checks: readonly (() => Refusal | undefined)[]): Refusal | undefined {
  for (const check of checks) {
    const refusal = check();
    if (refusal !== undefined) {
      return refusal;
    }
  }

  return undefined;
}

/** An address that is set, and that a credential may cross. */
function address(server: string): Refusal | undefined {
  if (server.trim().length === 0) {
    return {
      kind: 'no-server',
      why: 'No ingest server is set. Set one first: nothing can be sent to nowhere.',
    };
  }

  const unsafe = mayCarryAKey(server.trim());

  return unsafe.length > 0 ? { kind: 'unsafe-server', why: unsafe } : undefined;
}

/** A key, which is the one thing this machine cannot derive. */
function credential(key: string): Refusal | undefined {
  return key.trim().length > 0
    ? undefined
    : {
      kind: 'no-key',
      why: 'No contributor key is set. It is kept in the editor’s secret storage on this '
        + 'machine only — never in settings, which sync.',
    };
}

/** Something to send, and nothing already sending it. */
function queue(corpus: BugCorpus): Refusal | undefined {
  if (sending(corpus.lastSend)) {
    return {
      kind: 'already-sending',
      why: 'A send is already running. It survives a reload, so this is the same one — '
        + 'waiting for it is right, and starting another would offer the same pairs twice.',
    };
  }

  return waiting(corpus) > 0
    ? undefined
    : {
      kind: 'nothing-to-send',
      why: 'Nothing is waiting to be sent. Collect finds material; Review is where a pair is kept.',
    };
}

/**
 * How many pairs are waiting to go.
 *
 * <p>`collected` counts every pair this machine has ever kept, and the funnel has no column for
 * "kept but unsent" — so this is the honest upper bound rather than a precise queue, and the send
 * itself reports what it actually offered. A number that claims more precision than the data has is
 * the kind of number nobody checks.</p>
 */
export function waiting(corpus: BugCorpus): number {
  return corpus.funnel.collected;
}

/** What a finished send came to, in the words the section shows. */
export type Outcome =
  | { readonly kind: 'sent'; readonly said: string }
  | { readonly kind: 'partly'; readonly said: string }
  | { readonly kind: 'trouble'; readonly said: string }
  | { readonly kind: 'too-old'; readonly said: string }
  | { readonly kind: 'refused'; readonly said: string }
  | { readonly kind: 'no-database'; readonly said: string }
  | { readonly kind: 'unreadable'; readonly said: string };

/** What the CLI prints on stdout when a run finishes. */
export interface Summary {
  readonly offered: number;
  readonly accepted: number;
  readonly duplicate: number;
  readonly refused: number;
  readonly trouble: string;
}

/**
 * The summary, READ rather than cast.
 *
 * <p>The same discipline story 3 arrived at for the admin API: a field renamed on the server leaves
 * both suites green and the panel showing zeroes. Anything that is not a number is a zero here, and
 * a body that is not an object at all is no summary — which the caller renders as "it finished and
 * would not say what it did", never as "nothing was sent".</p>
 */
export function readSummary(stdout: string): Summary | undefined {
  const json = objectIn(stdout);
  if (json.length === 0) {
    return undefined;
  }

  try {
    const raw: unknown = JSON.parse(json);

    // NOT an array, which `typeof` calls an object too: a run that printed a list would otherwise
    // read as a summary of all zeroes, and the panel would report "0 sent" for a send that worked.
    return typeof raw === 'object' && raw !== null && !Array.isArray(raw)
      ? summaryOf(raw as Record<string, unknown>)
      : undefined;
  } catch {
    return undefined;
  }
}

function summaryOf(raw: Record<string, unknown>): Summary {
  return {
    offered: whole(raw.offered),
    accepted: whole(raw.accepted),
    duplicate: whole(raw.duplicate),
    refused: whole(raw.refused),
    trouble: typeof raw.trouble === 'string' ? raw.trouble : '',
  };
}

function whole(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

/**
 * The JSON OBJECT in what the run printed.
 *
 * <p><b>It read the last LINE, and the live contract test caught that on its first run.</b> The
 * server writes its summary with `JsonSerializer` and indentation on, so the real output is six
 * lines and the last one is `}` — unreadable. Every unit test passed, because every fixture here was
 * written on one line: both sides agreed perfectly with each other and disagreed with the binary,
 * which is the exact failure that test exists for.</p>
 *
 * <p>So: the whole of stdout when it IS the object, and otherwise the span from the first brace to
 * the last. The run writes its progress to stderr, but a `capture` that merged the two streams must
 * not make the summary unreadable either.</p>
 */
function objectIn(stdout: string): string {
  const trimmed = stdout.trim();
  if (trimmed.startsWith('{')) {
    return trimmed;
  }

  const open = trimmed.indexOf('{');
  const close = trimmed.lastIndexOf('}');

  return open >= 0 && close > open ? trimmed.slice(open, close + 1) : '';
}

/**
 * What to say about a run that has ended.
 *
 * <p>Every exit the CLI can answer has a sentence, including the two that are about THIS EXTENSION
 * rather than about the pairs: 64 is an installed server older than the send path, and an
 * unreadable summary is a run whose result nobody can claim. Neither may be reported as "your pairs
 * were refused". (Plan round, codex and gemini.)</p>
 */
export function outcomeOf(code: number, stdout: string): Outcome {
  const summary = readSummary(stdout);

  return byCode(code, summary) ?? finished(summary);
}

/** The exits that mean the run never got as far as an answer. */
function byCode(code: number, summary: Summary | undefined): Outcome | undefined {
  const known: Readonly<Record<number, Outcome>> = {
    [EXITS.tooOld]: {
      kind: 'too-old',
      said: 'This machine’s coai-mcp is older than sending. Update it and try again — '
        + 'nothing was sent, and nothing was lost.',
    },
    [EXITS.refusedTheRequest]: {
      kind: 'refused',
      said: 'The send was refused before it left: the address or the key was not usable.',
    },
    [EXITS.noDatabase]: {
      kind: 'no-database',
      said: 'The local database could not be opened, so there was nothing to send from.',
    },
    [EXITS.couldNotReach]: {
      kind: 'trouble',
      said: troubleSaid(summary),
    },
  };

  return known[code];
}

/** A transport failure says TRY AGAIN, because nothing is wrong with the pairs. */
function troubleSaid(summary: Summary | undefined): string {
  const why = summary?.trouble ?? '';
  const because = why.length > 0 ? ` (${why})` : '';

  return `The server could not be reached${because}. Nothing was marked as sent, so pressing Send `
    + 'again offers the same pairs — and the server ignores one it already holds.';
}

/** And an exit of zero is judged by what the summary says, not by the zero. */
function finished(summary: Summary | undefined): Outcome {
  if (summary === undefined) {
    return {
      kind: 'unreadable',
      said: 'The send finished and would not say what it did. Refresh to see what is still waiting.',
    };
  }

  const went = `${summary.accepted} sent, ${summary.duplicate} already held, ${summary.refused} refused`;

  return summary.refused > 0
    ? { kind: 'partly', said: `${went}. A refusal names a defect in our own normaliser, not in the pair.` }
    : { kind: 'sent', said: summary.offered === 0 ? 'Nothing was waiting to be sent.' : `${went}.` };
}

/** How the section labels the button, given what the database says is happening. */
export function sendLabel(send: SendRun, corpus: BugCorpus): string {
  if (sending(send)) {
    return send.offered > 0 ? `Sending… ${send.sent} of ${send.offered}` : 'Sending…';
  }

  const ready = waiting(corpus);

  return ready > 0 ? `Send ${ready} pair(s)` : 'Send';
}
