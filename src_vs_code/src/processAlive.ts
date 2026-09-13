/**
 * Is a process still running?
 *
 * <p>Signal 0 asks without sending anything, and the ERROR is the answer: `ESRCH` means no such
 * process, and anything else — `EPERM` most of all — means a process that exists and will not be
 * inspected by us. **Only `ESRCH` is death, and everything else is life**, because both callers pay
 * asymmetrically for the mistake: the orphan ledger would read another window's ledger and kill the
 * children it is using right now, and the conversation picker would reopen a conversation another
 * window has open and give one record two writers.</p>
 *
 * <p>It lived privately inside `chatOrphans.ts` until the picker needed the same question of a
 * heartbeat's writer. It is here rather than copied, because the rule that only `ESRCH` counts is
 * the whole of it and two copies of that rule are two places for one of them to start treating
 * `EPERM` as death. (The conventions' reuse rule: extract the shared half.)</p>
 *
 * <p><b>What it cannot tell you</b> is whether the process it found is the SAME process that wrote
 * the pid down — an operating system reuses a pid eventually. Both callers treat that residual the
 * conservative way: an unrelated live process wearing an old pid means a ledger left alone and a
 * conversation this window declines to reopen, never a child killed or a record forked.</p>
 */
export function processAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);

    return true;
  } catch (reason) {
    return (reason as NodeJS.ErrnoException).code !== 'ESRCH';
  }
}
