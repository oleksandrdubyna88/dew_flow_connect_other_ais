import { csvOf, ExportRound } from './roundsCsv';

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
}

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
export async function inBatches<T, R>(
  items: readonly T[],
  each: (item: T) => Promise<R>,
  atOnce = 4,
): Promise<R[]> {
  const done: R[] = [];
  for (let at = 0; at < items.length; at += atOnce) {
    // Sequential BETWEEN batches on purpose: this awaits inside a loop, which is usually a smell and
    // is the point here.
    // eslint-disable-next-line no-await-in-loop
    done.push(...await Promise.all(items.slice(at, at + atOnce).map(each)));
  }

  return done;
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
