/**
 * What was started, so a crash cannot leave it running.
 *
 * <p>A chat is a vendor CLI signed in as the person, and it lives as long as its tab. Closing the
 * tab kills it; disposing the extension kills them all. Neither runs when VS Code is FORCE-killed —
 * the Task Manager, a battery that went, an installer that restarts the machine — and what is left
 * behind is an authenticated process nobody can see and nobody will stop.</p>
 *
 * <p>So every child is written down as it starts and struck out as it ends, and the next activation
 * reads what is left. In the ordinary case there is nothing to read.</p>
 *
 * <p><b>One ledger per extension host, named after it.</b> `globalStorageUri` is SHARED by every VS
 * Code window, so a single file would mean one window's activation reading another window's live
 * children — and, since those children really are this extension's, verifying them as its own and
 * killing them. A person with two windows would have watched a working conversation die when they
 * opened the second. Raised as Blocking by the gate, and it is why the owner's pid is in the file
 * NAME: a window only ever reads files whose owner is gone.</p>
 *
 * <p><b>Killing by pid is the dangerous part, and this module exists to make it safe.</b> The
 * launcher's own comment says it: Windows reuses pids, so a recorded number can belong to somebody
 * else's process by the time anybody reads it — killing it would be worse than the orphan. A record
 * is therefore three facts, and all three must still hold: the pid, the image it was, and WHEN it
 * started. The check and the kill happen inside ONE command, so almost nothing can happen between
 * them; a separate query and kill leaves a window in which the pid can be handed on.</p>
 *
 * <p>Pure, so those rules are tests rather than claims — the files, the query and the kill are
 * somebody else's job.</p>
 */

/** One child, as the ledger remembers it. */
export interface ChildRecord {
  readonly pid: number;
  /** The executable's file name, lower-cased — what the operating system will call it back. */
  readonly image: string;
  /** Our own clock when it was started. Compared against what the OS says, not trusted alone. */
  readonly startedMs: number;
}

/**
 * How far apart the two clocks may be and still mean the same start.
 *
 * <p>Ours is taken as `spawn` returns and the OS's is the process's creation time, so the honest
 * difference is milliseconds. Both are absolute instants recorded once, so a clock the machine
 * changes afterwards moves neither. Ten seconds is slack for a machine under load; it is nowhere
 * near long enough for a pid to be recycled onto a program of the same name.</p>
 */
export const NEAR_ENOUGH_MS = 10_000;

/**
 * How long an entry nobody could resolve is carried forward.
 *
 * <p>An activation that cannot ask the operating system anything — no PowerShell, a refusal, a
 * machine that is not Windows — must not DELETE the record, or the orphan it could not verify
 * becomes one nobody will ever find. So it is kept and retried. A week is the bound on that: past
 * it, the pid means nothing on any machine that has rebooted, and the file would otherwise grow for
 * ever. (codex, the plan round.)</p>
 */
export const FORGET_AFTER_MS = 7 * 24 * 60 * 60 * 1000;

/** The image name a path means, for comparing with what the OS reports. */
export function imageOf(executable: string): string {
  const tail = executable.split(/[\\/]/).pop() ?? '';

  return tail.toLowerCase();
}

/**
 * An image name safe to put in a command.
 *
 * <p>It comes from a reviewer's `executablePath`, which is a person's own setting and therefore
 * their own text. The verify-and-kill command carries it, so a name with a quote in it would be a
 * quote in a PowerShell script. A file name has no business containing one; anything that does is
 * not compared, and its record is left alone.</p>
 */
export function safeImage(image: string): boolean {
  return /^[A-Za-z0-9](?:[A-Za-z0-9._+-]{0,120})$/.test(image);
}

/** The ledger with this child in it, and no duplicate pid — a reused number replaces the old row. */
export function recorded(entries: readonly ChildRecord[], child: ChildRecord): readonly ChildRecord[] {
  return [...entries.filter((known) => known.pid !== child.pid), child];
}

/** The ledger without this child. Called when it ends, however it ends. */
export function forgotten(entries: readonly ChildRecord[], pid: number): readonly ChildRecord[] {
  return entries.filter((known) => known.pid !== pid);
}

/** Old enough that its pid means nothing any more, whatever the operating system says. */
export function tooOld(record: ChildRecord, now: number): boolean {
  return now - record.startedMs > FORGET_AFTER_MS;
}

/**
 * Which records are worth asking the operating system about.
 *
 * <p>Not the ones too old to mean anything, and not the ones whose image could not appear in a
 * command. Everything else is a candidate — the answer decides, not this.</p>
 */
export function worthAsking(entries: readonly ChildRecord[], now: number): readonly ChildRecord[] {
  return entries.filter((record) => !tooOld(record, now) && safeImage(record.image));
}

/**
 * The ledger as it survived on disk.
 *
 * <p>Junk-safe by design: this file is read at activation, and a half-written line — the shape a
 * force-kill leaves — must not stop an extension from starting. Anything unreadable is no ledger,
 * which loses at most the chance to tidy up one crash. A truncated row cannot half-match either:
 * every one of the three facts is required, so half a record is no record.</p>
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

/** What a ledger file is called. The owner's pid is IN the name — see the note at the top. */
export function ledgerName(ownerPid: number): string {
  return `chat-children-${ownerPid}.json`;
}

/** The owner a ledger file belongs to, or 0 when the name is not one of ours. */
export function ownerOf(fileName: string): number {
  const found = /^chat-children-([0-9]{1,10})\.json$/.exec(fileName);

  return found === null ? 0 : Number(found[1]);
}

/**
 * The one command that checks and kills, because two commands leave a gap.
 *
 * <p>A query followed by a kill is a window in which the verified process can exit and its number be
 * handed to somebody else — small, and the whole reason this module exists is that small windows
 * around killing are not acceptable. So the three facts are compared and the tree is ended inside a
 * single invocation, and the script says which it did.</p>
 *
 * <p><b>`taskkill` with the tree flags, not `Terminate`</b>: a Windows shim is a tree — `codex` is
 * `codex.cmd` running `cmd.exe` running node — and ending only the root leaves the CLI that was
 * actually working. Its EXIT CODE is checked: a `taskkill` that was refused used to be reported as
 * a kill, which struck out the record and left the process running. (codex, the code round.)</p>
 *
 * <p>An image this side would not put in a command yields NO command — empty, which the caller reads
 * as "nothing could be asked". `worthAsking` says the same thing earlier; a guarantee a reader has to
 * reconstruct from another function is a guarantee waiting to be broken.</p>
 *
 * <p><b>`Get-CimInstance`, not `Get-Process`</b>: the latter reports a name with no extension and can
 * throw on a process it may not inspect, and the extension is half of what identifies an image. The
 * creation time is converted to epoch milliseconds HERE rather than parsed on the other side, so no
 * date format, locale or time zone crosses the boundary. Measured on this machine.</p>
 */
export function verifyAndKill(record: ChildRecord): string {
  if (!safeImage(record.image)) {
    return '';
  }

  return [
    `$p = Get-CimInstance Win32_Process -Filter "ProcessId=${record.pid}"`,
    '$started = if ($p) { [math]::Round(($p.CreationDate.ToUniversalTime() '
    + "- [datetime]'1970-01-01').TotalMilliseconds) } else { 0 }",
    `if ($p -and $p.Name -eq '${record.image}' `
    + `-and [math]::Abs($started - ${record.startedMs}) -le ${NEAR_ENOUGH_MS}) {`,
    `  taskkill /pid ${record.pid} /t /f | Out-Null`,
    '  if ($LASTEXITCODE -eq 0) { "killed" } else { "refused" }',
    '} elseif ($p) { "not ours" } else { "gone" }',
  ].join('\n');
}

/** What one verify-and-kill came to. Anything unreadable is `unknown` — and `unknown` keeps the row. */
export type KillOutcome = 'killed' | 'not ours' | 'gone' | 'unknown';

/**
 * What the command said.
 *
 * <p>`unknown` is every unhappy shape — a refusal, an error on stderr, a PowerShell that would not
 * start, a machine that is not Windows. It kills nothing and, unlike the other three, it does NOT
 * settle the record: an entry nobody could ask about is retried at the next activation rather than
 * quietly dropped, because dropping it is how an orphan becomes permanent.</p>
 */
export function killOutcome(exitCode: number, output: string): KillOutcome {
  if (exitCode !== 0) {
    return 'unknown';
  }
  const said = output.toLowerCase();
  if (said.includes('refused')) {
    // The tree kill itself failed — access denied, a process protected from us. Unknown, so the
    // record is KEPT and tried again rather than struck out over a process that is still running.
    return 'unknown';
  }
  if (said.includes('killed')) {
    return 'killed';
  }
  if (said.includes('not ours')) {
    return 'not ours';
  }

  return said.includes('gone') ? 'gone' : 'unknown';
}

/** A record is settled — struck out — unless nobody could tell what it was. */
export function settled(outcome: KillOutcome): boolean {
  return outcome !== 'unknown';
}
