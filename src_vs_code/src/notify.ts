import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { Notice, answered, buttonsOf, gapSentence, noticeRecord } from './notice';
import { NotificationRecord } from './notifications';
import { recordNotification } from './notificationsFile';
import { Suppressor, suppressor } from './suppression';

/**
 * The one door every message this extension shows a person goes through.
 *
 * <p>It RECORDS, then it shows. Until this existed there were 109 `window.show*Message` call sites
 * and no durable record of any of them, and on 2026-09-16 that cost ninety minutes: a
 * settings-mirror stand-down warned once, nobody saw it, and eleven code rounds ran against a role
 * deleted thirty-eight minutes earlier. Plan:
 * `todo/PLAN_every_message_is_written_down.md`; the pure half is `notice.ts`.</p>
 *
 * <h2>Two doors, because awaiting a toast inside a lock is a deadlock</h2>
 *
 * <p><b>`notify` never waits for the person.</b> It awaits the APPEND — microseconds — fires the
 * toast and returns. <b>`notifyAndAsk` does wait</b>, and is called only from outside a critical
 * section.</p>
 *
 * <p>That division is not tidiness. `show*Message` with a button does not resolve until somebody
 * clicks it, and the plan's second draft had every caller write `const chosen = await notify(…)`.
 * A stand-down raised inside `serverSettingsSync`'s critical section would then have held that
 * section for as long as the toast sat on screen; every later `sync()` would answer `'busy'`; and
 * the one dropped retry would fire. The mechanism built against the incident would have reproduced
 * the incident. (The plan round, round 2, as a blocking finding.)</p>
 *
 * <h2>The append is awaited for every class, not for three</h2>
 *
 * <p>An earlier draft awaited it only for `failure`, `stand-down` and `storm`. That makes "records,
 * then shows" false for the rest: an information toast shown while its append is still queued, on a
 * host killed a moment later, is a message the person saw and the ledger never had — this feature's
 * own definition of the defect. An append costs microseconds; there was nothing to buy.</p>
 */

/**
 * Which run this is.
 *
 * <p>Minted once per host start, and NOT the pid: pids are reused, so a pid cannot identify a
 * process lifetime and two unrelated incidents months apart would group into one row. It lives in
 * memory, so a restart is a new run — which is the honest reading, a new host being a new
 * observer.</p>
 */
const RUN = randomBytes(6).toString('hex');

/**
 * What bounds this run's share of the ledger.
 *
 * <p>One per host, minted beside the run id because that is exactly its lifetime: every bound it
 * enforces is per run, and a restart is a new observer with a fresh count. It bounds what is
 * WRITTEN and never what is shown — see `suppression.ts`.</p>
 */
const BOUNDS: Suppressor = suppressor(RUN, process.pid);

/** How many records this run could not write, and when that started. Never a notification. */
let lost = 0;
let lostSinceIso = '';

/** What the panel section and the page say about the ledger's own gap. Empty when there is none. */
export function theGap(): string {
  return gapSentence(lost, lostSinceIso === '' ? '' : new Date(lostSinceIso).toLocaleTimeString());
}

/** For a test, and for the panel after a data directory move. */
export function forgetTheGap(): void {
  lost = 0;
  lostSinceIso = '';
}

function noteLoss(at: Date): void {
  if (lost === 0) {
    lostSinceIso = at.toISOString();
  }
  lost += 1;
}

/**
 * Put one record down. Never throws, and tells the gap counter when the disk refuses.
 *
 * <p>`appendLine` catches internally and would hand back a resolved promise, so without the
 * callback the counter above would read zero straight through a disk failure — a log that lies by
 * omission about its own omissions.</p>
 */
async function write(record: NotificationRecord): Promise<void> {
  await recordNotification(coaiDataDir(), record, () => noteLoss(new Date()));
}

/** The toast itself, exactly as the call site would have shown it. */
function show(notice: Notice): Thenable<string | undefined> {
  const options: vscode.MessageOptions = {
    ...(notice.modal === true ? { modal: true } : {}),
    ...(notice.detail === undefined ? {} : { detail: notice.detail }),
  };
  const actions = buttonsOf(notice);

  if (notice.as === 'error') {
    return vscode.window.showErrorMessage(notice.title, options, ...actions);
  }
  if (notice.as === 'warning') {
    return vscode.window.showWarningMessage(notice.title, options, ...actions);
  }

  return vscode.window.showInformationMessage(notice.title, options, ...actions);
}

/**
 * Put one occurrence down, unless this run's bounds have already had enough of it.
 *
 * <p>Returns what was written, or nothing when the bounds refused it — a caller that has an answer
 * to record later needs the asking it belongs to, and a question whose asking was never written must
 * not leave an answer behind with nothing to answer.</p>
 *
 * <p>The occurrence goes down before its meta-alert, so the file reads in the order things happened:
 * the thousandth repeat, then the row saying the thousandth was the last one kept.</p>
 */
async function record(notice: Notice, at: Date): Promise<{
  readonly seq: number;
  readonly kept: NotificationRecord | undefined;
}> {
  const verdict = BOUNDS.admit(notice, at);
  const kept = verdict.write
    ? { ...noticeRecord(notice, RUN, process.pid, at), seq: verdict.seq }
    : undefined;
  if (kept !== undefined) {
    await write(kept);
  }
  if (verdict.storm !== undefined) {
    await write(verdict.storm);
  }

  return { seq: verdict.seq, kept };
}

/**
 * This condition has ended, so its next occurrence is news rather than a repeat.
 *
 * <p>The counterpart of `serverSettingsSync.ts:171`, which clears its own once-per-version guard on
 * a successful write because *"the situation is over; a stand-down after this is news, not a
 * repeat."* Without it a fault that comes back after being fixed is a silent increment on a counter
 * nobody is reading, which is the shape of the incident this whole ledger exists for.</p>
 */
export function notifyResolved(code: string, subject?: string): void {
  BOUNDS.resolved(code, subject);
}

/**
 * Record it, then show it — and do not wait for the person.
 *
 * <p>Returns once the record is on disk. The toast outlives the call, which is what every fire-and
 * forget call site did before the funnel and what keeps a caller holding a lock safe.</p>
 */
export async function notify(notice: Notice): Promise<void> {
  await record(notice, new Date());
  void show(notice);
}

/**
 * Record every occurrence; show only the first of each `(code, subject)` in this run.
 *
 * <p>For a call site whose condition is polled rather than raised — the panel asks the server what
 * it could not understand on every probe, and the same complaint comes back every few seconds. Those
 * sites already suppressed themselves, each with its own `Set` or flag; this is that guard, written
 * once, and it is strictly more honest than what it replaces: `panelProvider`'s `notesSaid` showed
 * the sentence once and recorded NOTHING, so a complaint arriving four thousand times looked exactly
 * like one arriving twice.</p>
 *
 * <p>It is not a fourth policy. The funnel never suppresses a toast on its own initiative — this
 * door exists because a caller asked for it, which is the only sanctioned way a message stops being
 * shown.</p>
 */
export async function notifyOnce(notice: Notice): Promise<void> {
  const { seq } = await record(notice, new Date());
  if (seq === 1) {
    void show(notice);
  }
}

/**
 * Record it, show it, and react to the button — without making the caller wait for the person.
 *
 * <p>The shape `reportStandDown` has: it offers **Reload Window** and reloads if pressed, and it is
 * raised from inside `serverSettingsSync`'s critical section. `notifyAndAsk` there would hold the
 * lock until somebody clicked; plain `notify` would drop the button on the floor. So the answer is
 * awaited on its own, off the caller's path, and recorded when it arrives.</p>
 */
export async function notifyThen(
  notice: Notice,
  chosen: (answer: string | undefined) => void,
): Promise<void> {
  const { kept } = await record(notice, new Date());
  void show(notice).then(async (answer) => {
    if (kept !== undefined) {
      await write(answered(kept, answer));
    }
    chosen(answer);
  });
}

/**
 * Record it, show it, wait for the answer, and record THAT.
 *
 * <p>Two records rather than one amended: nothing in this design is ever updated in place, which is
 * what keeps the measured append guarantee applicable. The asking is written before anything can
 * refuse — `chatDoorsFile` records a door the same way and for the same reason, *"because those are
 * invocations too"*, and that was the blocking finding on its own plan round. A question the person
 * walks away from is a fact worth having.</p>
 *
 * <p>With no `action` it is still the right door for a caller that must not carry on until the
 * message has been seen — several do, and the wait is the behaviour, not an accident. In that case
 * no second record is written: "dismissed" for a toast with no button on it is a fact about VS
 * Code's timer rather than about the person.</p>
 *
 * <p><b>Never call this while holding a lock.</b> It waits for a human.</p>
 */
export async function notifyAndAsk(notice: Notice): Promise<string | undefined> {
  const { kept } = await record(notice, new Date());
  const chosen = await show(notice);
  if (kept !== undefined && (buttonsOf(notice).length > 0 || notice.modal === true)) {
    await write(answered(kept, chosen));
  }

  return chosen;
}
