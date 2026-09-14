import { csvOf, ExportableRow, ExportRound } from './roundsCsv';

/**
 * Turning a selection of rounds into a file the person chose the place for.
 *
 * <p><b>No `vscode` import, on purpose.</b> The three endings below are the whole of what this
 * feature can do wrong, and a module that imports `vscode` cannot be constructed by any test in this
 * suite — which is the same reasoning `roundsLogMessages.ts` sets out for the message decoder. The
 * host supplies the dialog, the write and the two ways of speaking to a person; everything that
 * DECIDES anything is here.</p>
 */

/** What the host lends this. Each one is the thin half that genuinely needs VS Code. */
export interface ExportPorts {
  /** The save dialog. `undefined` is the person choosing not to. */
  readonly pickPath: (suggestedName: string) => Promise<string | undefined>;
  readonly write: (path: string, text: string) => Promise<void>;
  readonly report: (message: string) => void;
  readonly reportError: (message: string) => void;
  /**
   * Ask before starting an unusually large one. `false` is the person saying no.
   *
   * <p>Optional because a single-row export has nothing to ask about. When it is absent a big
   * selection simply proceeds, which is what the per-row button has always done.</p>
   */
  readonly confirmLarge?: (howMany: number) => Promise<boolean>;
  /** How far the reads have got. Called once per round READ, so a caller can count in rounds. */
  readonly progress?: (done: number, total: number) => void;
  /** Whether the person has cancelled. Checked between batches, so cancelling is prompt. */
  readonly cancelled?: () => boolean;
}

/**
 * Above this many rounds, ask first.
 *
 * <p>Not a limit — a person may genuinely want a year of rounds — but a selection this size is
 * minutes of child processes, and starting it because somebody ticked the header box without
 * meaning to is a worse outcome than one extra question.</p>
 */
export const ASK_ABOVE = 500;

/**
 * How many reads run at once.
 *
 * <p>Named rather than repeated: each read is a child process, and four is enough that their
 * start-up overlaps without an export contending with the editor the person is using.</p>
 */
export const READS_AT_ONCE = 4;

/** What happened, for the caller that has to clear an in-flight state either way. */
export type ExportOutcome = 'written' | 'cancelled' | 'failed';

/**
 * A name somebody can find again.
 *
 * <p>The day, and how many rounds — so a folder of these sorts by date and says at a glance which is
 * the big one. The date is the LOCAL day, because it is a file name a person reads.</p>
 */
export function suggestedName(howMany: number, today: Date = new Date()): string {
  const day = [
    today.getFullYear(),
    String(today.getMonth() + 1).padStart(2, '0'),
    String(today.getDate()).padStart(2, '0'),
  ].join('-');

  return howMany === 1 ? `coai-round-${day}.csv` : `coai-rounds-${day}-${howMany}.csv`;
}

/**
 * Write the rounds out, and say what happened.
 *
 * <p><b>Three endings, all of them defined</b> — the code round over the plan raised this three
 * times, from three vendors, because an undefined one is how a button ends up stuck on
 * <i>Exporting…</i> for ever:</p>
 * <ul>
 *   <li><b>cancelled</b> — the dialog answered nothing. A clean no-op: no error, and no success
 *       message for a file that was never asked for.</li>
 *   <li><b>failed</b> — the write threw. A read-only drive, a file another program holds, a path
 *       that stopped existing between the dialog and the write. The person is told what failed and
 *       is NOT told it worked.</li>
 *   <li><b>written</b> — how many rounds, and where.</li>
 * </ul>
 *
 * <p>The outcome is returned as well as reported, so the caller can clear its in-flight state on
 * every path without having to infer which one it took.</p>
 */
export async function exportRounds(
  rounds: readonly ExportRound[],
  ports: ExportPorts,
  today: Date = new Date(),
): Promise<ExportOutcome> {
  if (rounds.length === 0) {
    // Nothing selected is not a failure and not a file. It reaches here only if the page and the
    // host disagree about what is selected, and the honest answer is to do nothing quietly.
    return 'cancelled';
  }

  // THE FOURTH ENDING, and it comes FIRST: a round whose findings could not be read is refused
  // before the person is asked where to put a file. Writing it as a round with blank finding cells
  // would say it found nothing, which is the lie `readFindings`' three-state answer exists to
  // prevent — and asking for a path and then failing would waste the one decision they made.
  let built: ReturnType<typeof csvOf>;
  try {
    built = csvOf(rounds);
  } catch (reason: unknown) {
    // Still inside a failure path, per the plan round: a serialisation that throws on a value
    // nobody expected must not leave this promise unresolved.
    ports.reportError(`The rounds could not be prepared for export: ${messageOf(reason)}`);

    return 'failed';
  }
  // Nothing could be read at all, so there is nothing worth a save dialog.
  if ('refused' in built) {
    ports.reportError(
      `The findings of ${counted(built.refused.length)} could not be read, so nothing was written. `
      + `Open ${built.refused.length === 1 ? 'that round' : 'those rounds'} in the log and try again: `
      + built.refused.join(', '));

    return 'failed';
  }

  // EVERYTHING is inside the failure path, not only the write. The plan named three endings and a
  // reviewer was right that two more could escape between them: a dialog that rejects because its
  // window went away, and a serialisation that throws on a value nobody expected. A promise leaving
  // this function unresolved is a control left running and a person told nothing. (Plan round, codex.)
  let path: string | undefined;
  let text: string;
  try {
    path = await ports.pickPath(suggestedName(rounds.length, today));
    if (path === undefined) {
      return 'cancelled';
    }
    text = built.text;
  } catch (reason: unknown) {
    ports.reportError(`The rounds could not be prepared for export: ${messageOf(reason)}`);

    return 'failed';
  }

  try {
    await ports.write(path, text);
  } catch (reason: unknown) {
    ports.reportError(`The rounds could not be written to ${path}: ${messageOf(reason)}`);

    return 'failed';
  }

  // Said plainly rather than buried: some rounds are in the file with blank finding columns and
  // `findings_read = failed`, and a person who is not told that would read them as clean.
  ports.report(built.unread.length === 0
    ? `${counted(rounds.length)} written to ${path}.`
    : `${counted(rounds.length)} written to ${path} — but the findings of `
      + `${counted(built.unread.length)} could not be read, and those rows are marked `
      + `"failed" rather than empty: ${built.unread.join(', ')}.`);

  return 'written';
}

/**
 * How a round's findings are fetched. One call per round; the coordinator bounds the concurrency.
 *
 * <p>A PORT rather than a direct call, so the whole export — the reads included — is reachable from
 * a test without an editor, and so C1 and C2 can supply a different reader without the orchestration
 * moving. It answers a state and never throws: a rejection is a `failed` round, not an exception
 * that takes the export with it.</p>
 */
export type ReadFindings = (row: ExportableRow) => Promise<ExportRound['found']>;

/**
 * Read EVERY selected round in one go, when the server can.
 *
 * <p>Story C2 makes a bulk export one spawn instead of one per round, and a coordinator that can
 * only take a per-round reader would have to be rewritten to allow it. So the batch shape is the
 * seam now: supply `readAll` and the per-round loop is not used at all; supply neither and nothing
 * changes. It answers one `found` per row IN ORDER, and answering a different number of rows is a
 * failure of the reader rather than something this has to guess about. (Code round, gemini.)</p>
 */
export type ReadAllFindings = (rows: readonly ExportableRow[]) => Promise<readonly ExportRound['found'][]>;

/**
 * Read every round's findings, then write them.
 *
 * <p>The read phase lives HERE rather than at the wiring, and inside whatever queue the caller runs
 * this in. It used to sit in `extension.ts` before the queue, which meant a hundred quick clicks
 * started a hundred batches of four rather than one batch at a time — the cap was real and was
 * being stepped around. Three reviewers said so. (Code round.)</p>
 */
export async function readAndExport(
  rows: readonly ExportableRow[],
  read: ReadFindings,
  ports: ExportPorts,
  today: Date = new Date(),
  readAll?: ReadAllFindings,
): Promise<ExportOutcome> {
  if (rows.length > ASK_ABOVE && ports.confirmLarge !== undefined && !await ports.confirmLarge(rows.length)) {
    return 'cancelled';
  }

  if (readAll !== undefined) {
    // ONE call for the whole selection. Its failures are its own to report, and a reader that
    // answers a different number of rows than it was asked about is not answering about these
    // rounds — every one of them is failed rather than paired up by position and hoped for.
    let all: readonly ExportRound['found'][];
    try {
      all = await readAll(rows);
    } catch (reason: unknown) {
      console.error('ConnectOtherAIs: the batch read of findings failed', reason);
      all = [];
    }
    const paired = rows.map((row, at) => ({
      row,
      found: all.length === rows.length
        ? all[at]!
        : { state: 'failed' as const, findings: [] as ExportableRow[] },
    }));
    ports.progress?.(rows.length, rows.length);

    return ports.cancelled?.() === true ? 'cancelled' : exportRounds(paired, ports, today);
  }

  let done = 0;
  const rounds = await inBatches(
    rows,
    async (row) => {
      const found = await read(row);
      done += 1;
      ports.progress?.(done, rows.length);

      return { row, found };
    },
    // A read that threw is a FAILED round, never an absent one: we know nothing about it, and
    // `absent` is a claim that the database has no record.
    (row, reason) => {
      console.error('ConnectOtherAIs: findings could not be read for export', reason);

      done += 1;
      ports.progress?.(done, rows.length);

      return { row, found: { state: 'failed' as const, findings: [] as ExportableRow[] } };
    },
    READS_AT_ONCE,
    // Checked BETWEEN batches, so a cancel takes effect within one batch rather than after every
    // round has been read. The reads already in flight are allowed to finish; killing a child
    // process mid-read would leave the database connection to be cleaned up by the OS.
    ports.cancelled);

  if (!rounds.done) {
    // Nothing is written and nothing is reported: the person stopped it, and telling them they
    // stopped it is noise — the same answer a dismissed save dialog gets. The READS are discarded
    // with it; a file of the rounds that happened to finish would look like a log of the selection
    // and silently not be one.
    return 'cancelled';
  }

  return exportRounds(rounds.results, ports, today);
}

/**
 * One export at a time.
 *
 * <p>Two Export buttons clicked in quick succession open two dialogs, and a person who picks the
 * same destination in both gets two atomic writes racing for one path — the file ends up holding
 * whichever finished last while BOTH report success. Serialising them costs nothing here: an export
 * is a human-paced action, and the second simply waits for the first to finish. (Plan round,
 * codex.)</p>
 */
export function oneAtATime(): (run: () => Promise<ExportOutcome>) => Promise<ExportOutcome> {
  let queue: Promise<unknown> = Promise.resolve();

  return (run) => {
    // Every link swallows its own failure, because `exportRounds` already reports one and a
    // rejection here would break the chain for every later export rather than for this one.
    const mine = queue.then(run, run);
    queue = mine.catch(() => undefined);

    return mine;
  };
}

/**
 * Run a job per item, a few at a time.
 *
 * <p>The reads behind an export are one CHILD PROCESS each. `Promise.all` over a selection starts
 * every one of them at once, and a few hundred exhausts file handles or process slots — so rounds
 * that were perfectly readable time out, and the export then refuses or hangs on failures it caused
 * itself. Three reviewers raised it on the plan round, each from a different direction.</p>
 *
 * <p>Four at a time: enough that the reads overlap their process start-up, few enough that a big
 * selection cannot exhaust anything. Order is preserved, because a file whose lines shuffle between
 * exports is a file nobody can diff.</p>
 */
export interface Batched<R> {
  /**
   * Whether the work FINISHED, said as a value rather than left to be inferred.
   *
   * <p>The first shape returned the results read so far and left the caller to ask the cancellation
   * token again. Two reviewers called that Blocking and they were right: a caller that forgets the
   * second question writes a truncated file and reports success, which is the exact class of defect
   * this plan exists to remove. A cancelled run cannot be mistaken for a complete one now, because
   * the two are different values.</p>
   */
  readonly done: boolean;
  readonly results: readonly R[];
}

export async function inBatches<T, R>(
  items: readonly T[],
  each: (item: T) => Promise<R>,
  onFailure: (item: T, reason: unknown) => R,
  atOnce = READS_AT_ONCE,
  stop?: () => boolean,
): Promise<Batched<R>> {
  // A batch size that cannot advance the loop would hang for ever and look like a stuck export.
  // (Code round, codex.)
  const step = Number.isFinite(atOnce) && atOnce >= 1 ? Math.floor(atOnce) : 1;
  const done: R[] = [];
  for (let at = 0; at < items.length; at += step) {
    if (stop?.() === true) {
      return { done: false, results: done };
    }
    const batch = items.slice(at, at + step);
    // ONE job's rejection must not abort the batch and leave its siblings running with nobody
    // awaiting them. `Promise.all` does exactly that, and this is a generic helper whose next
    // caller will not know. Each job answers for itself and `onFailure` turns a rejection into a
    // result, so every item gets one. (Code round, gemini, twice.)
    // eslint-disable-next-line no-await-in-loop
    const settled = await Promise.all(batch.map(async (item) => {
      try {
        return await each(item);
      } catch (reason: unknown) {
        return onFailure(item, reason);
      }
    }));
    for (const one of settled) {
      done.push(one);
    }
  }

  // Checked AFTER the loop as well as before each batch. A cancel arriving while the LAST batch was
  // in flight let the loop end naturally and report done: true, and the export then opened the save
  // dialog and wrote the file — and a run of four or fewer rounds is one batch, so for those the
  // check never happened at all. Three reviewers found it. (Code round.)
  return { done: stop?.() !== true, results: done };
}

/** "1 round" / "41 rounds" — a count nobody has to read twice. */
function counted(howMany: number): string {
  return howMany === 1 ? '1 round' : `${howMany} rounds`;
}

/**
 * What went wrong, in words.
 *
 * <p>A rejection is not always an `Error`: a disposed host, a cancelled token and a plain string all
 * reach here. Whatever it is, the person gets something rather than `[object Object]`.</p>
 */
function messageOf(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}
