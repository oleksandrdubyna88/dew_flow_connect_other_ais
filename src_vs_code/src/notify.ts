import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { Notice, answered, gapSentence, noticeRecord } from './notice';
import { NotificationRecord } from './notifications';
import { recordNotification } from './notificationsFile';

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
  const actions = notice.action === undefined ? [] : [notice.action];

  if (notice.as === 'error') {
    return vscode.window.showErrorMessage(notice.title, options, ...actions);
  }
  if (notice.as === 'warning') {
    return vscode.window.showWarningMessage(notice.title, options, ...actions);
  }

  return vscode.window.showInformationMessage(notice.title, options, ...actions);
}

/**
 * Record it, then show it — and do not wait for the person.
 *
 * <p>Returns once the record is on disk. The toast outlives the call, which is what every fire-and
 * forget call site did before the funnel and what keeps a caller holding a lock safe.</p>
 */
export async function notify(notice: Notice): Promise<void> {
  await write(noticeRecord(notice, RUN, process.pid, new Date()));
  void show(notice);
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
  const asked = noticeRecord(notice, RUN, process.pid, new Date());
  await write(asked);
  void show(notice).then(async (answer) => {
    await write(answered(asked, answer));
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
 * <p><b>Never call this while holding a lock.</b> It waits for a human.</p>
 */
export async function notifyAndAsk(notice: Notice): Promise<string | undefined> {
  const asked = noticeRecord(notice, RUN, process.pid, new Date());
  await write(asked);
  const chosen = await show(notice);
  await write(answered(asked, chosen));

  return chosen;
}
