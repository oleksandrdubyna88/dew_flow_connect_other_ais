/**
 * What was started, so a crash cannot leave it running.
 *
 * <p>A chat is a vendor CLI signed in as the person, and it lives as long as its tab. Closing the
 * tab kills it; disposing the extension kills them all. Neither runs when VS Code is FORCE-killed —
 * the Task Manager, a battery that went, an installer that restarts the machine — and what is left
 * behind is an authenticated process nobody can see and nobody will stop.</p>
 *
 * <p>So every child is written down as it starts and struck out as it ends, and the next activation
 * reads what is left. In the ordinary case the file is empty and the whole mechanism costs nothing.</p>
 *
 * <p><b>Killing by pid is the dangerous part, and this module exists to make it safe.</b> The
 * launcher's own comment says it: Windows reuses pids, so a recorded number can belong to somebody
 * else's process by the time anybody reads it — killing it would be worse than the orphan. A record
 * is therefore three facts, and all three must still hold: the pid, the image it was, and WHEN it
 * started. A reused pid is a different program, or the same program started at a different time, and
 * either mismatch is enough to leave it alone.</p>
 *
 * <p>Pure, so those rules are tests rather than claims — the file, the process query and the kill
 * are somebody else's job.</p>
 */

/** One child, as the ledger remembers it. */
export interface ChildRecord {
  readonly pid: number;
  /** The executable's file name, lower-cased — what the operating system will call it back. */
  readonly image: string;
  /** Our own clock when it was started. Compared against what the OS says, not trusted alone. */
  readonly startedMs: number;
}

/** What the operating system says about a pid NOW, or nothing when no such process exists. */
export interface LivingProcess {
  readonly image: string;
  readonly createdMs: number;
}

/**
 * How far apart the two clocks may be and still mean the same start.
 *
 * <p>Ours is taken just before `spawn` returns and the OS's is taken when the process was created,
 * so the honest difference is milliseconds. Ten seconds is slack for a machine under load and a
 * clock that ticks between the two readings; it is nowhere near long enough for a pid to be recycled
 * onto a program of the same name, which is the only thing this tolerance risks.</p>
 */
export const NEAR_ENOUGH_MS = 10_000;

/** The image name a path means, for comparing with what the OS reports. */
export function imageOf(executable: string): string {
  const tail = executable.split(/[\\/]/).pop() ?? '';

  return tail.toLowerCase();
}

/** The ledger with this child in it, and no duplicate pid — a reused number replaces the old row. */
export function recorded(entries: readonly ChildRecord[], child: ChildRecord): readonly ChildRecord[] {
  return [...entries.filter((known) => known.pid !== child.pid), child];
}

/** The ledger without this child. Called when it ends, however it ends. */
export function forgotten(entries: readonly ChildRecord[], pid: number): readonly ChildRecord[] {
  return entries.filter((known) => known.pid !== pid);
}

/**
 * Is the process now holding this pid the one we started?
 *
 * <p>Three facts, all of them required. A pid that nothing holds is already gone. A pid held by
 * another program is a recycled number. A pid held by the same program but started at another time
 * is a different run of it — somebody's own `claude`, most likely, which is precisely the thing that
 * must not be killed.</p>
 */
export function isOurs(record: ChildRecord, alive: LivingProcess | undefined): boolean {
  if (alive === undefined) {
    return false;
  }

  return alive.image === record.image && Math.abs(alive.createdMs - record.startedMs) <= NEAR_ENOUGH_MS;
}

/**
 * The ledger as it survived on disk.
 *
 * <p>Junk-safe by design: this file is read at activation, and a half-written line — the shape a
 * force-kill leaves — must not stop an extension from starting. Anything unreadable is no ledger,
 * which loses at most the chance to tidy up one crash.</p>
 */
export function parseLedger(text: string): readonly ChildRecord[] {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return [];
  }
  if (!Array.isArray(value)) {
    return [];
  }

  return value.filter(isRecord).map((row) => ({
    pid: row.pid,
    image: row.image.toLowerCase(),
    startedMs: row.startedMs,
  }));
}

function isRecord(value: unknown): value is ChildRecord {
  const row = value as Partial<ChildRecord> | null;

  return typeof row === 'object'
    && row !== null
    && typeof row.pid === 'number'
    && Number.isInteger(row.pid)
    && row.pid > 0
    && typeof row.image === 'string'
    && row.image.length > 0
    && typeof row.startedMs === 'number'
    && Number.isFinite(row.startedMs);
}

/** The ledger as text. Its own function so both halves agree about the shape on disk. */
export function ledgerText(entries: readonly ChildRecord[]): string {
  return JSON.stringify(entries);
}

/**
 * What to ask Windows about a pid.
 *
 * <p>Two facts and nothing else, formatted by the shell so this side parses one line rather than a
 * JSON dialect that changes between PowerShell versions. `Get-CimInstance` rather than
 * `Get-Process`, because `Get-Process` reports a name with no extension and can throw on a process
 * it may not inspect — and the extension is half of what identifies the image.</p>
 *
 * <p>Measured on this machine: `pwsh.exe|1788944157495`.</p>
 */
export function livingQuery(pid: number): string {
  return `$p = Get-CimInstance Win32_Process -Filter "ProcessId=${pid}"; `
    + 'if ($p) { "$($p.Name)|$([math]::Round(($p.CreationDate.ToUniversalTime() '
    + "- [datetime]'1970-01-01').TotalMilliseconds))\" }";
}

/**
 * What the query answered, or nothing at all.
 *
 * <p>Nothing covers every unhappy shape on purpose — no such process, a refusal, an error on
 * stderr, a PowerShell that would not start. All of them mean the same thing here: this side cannot
 * prove the pid is ours, so it is left alone. A guard that fails OPEN would kill strangers.</p>
 */
export function parseLiving(text: string): LivingProcess | undefined {
  const line = text.split(/\r?\n/).map((one) => one.trim()).find((one) => one.includes('|'));
  const [image, created] = (line ?? '').split('|');
  const createdMs = Number(created);

  return image !== undefined && image.length > 0 && Number.isFinite(createdMs) && createdMs > 0
    ? { image: image.toLowerCase(), createdMs }
    : undefined;
}
