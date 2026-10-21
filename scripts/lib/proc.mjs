// lib/proc.mjs — the process halves of the Kimi probes (scripts/probe-kimi-tree-hold.mjs, scripts/probe-kimi-confinement.mjs; plan §5 of
// research/PLAN_kimi_and_vibe_on_linux.md and DN5 of todo/PLAN_kimi_and_vibe_on_native_linux.md): who is running, read from /proc (or from
// Windows PowerShell), whether one of them is a Kimi, and whether a lock file is held, asked of flock(1).
// All of it is a READ-ONLY question: nothing here ends a process or keeps a lock.
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, readlinkSync } from 'node:fs';
import { join, posix } from 'node:path';
import { SYSTEM32 } from './vibe.mjs';

/** util-linux's flock(1) by its absolute path, never looked up through PATH (S4036); '' where it is not installed. */
export const FLOCK = ['/usr/bin/flock', '/bin/flock'].find((p) => existsSync(p)) ?? '';

/**
 * One `/proc/<pid>/stat` line as `{ pid, ppid, comm, state }`, or null. The name (`comm`, at most 15 characters) is in parentheses and
 * may itself hold `)` or spaces, so it runs to the LAST `)`; the state and the parent pid follow it.
 */
function parseStat(line) {
  const open = line.indexOf('(');
  const close = line.lastIndexOf(')');
  if (open <= 0 || close <= open) return null;
  const pid = Number.parseInt(line.slice(0, open).trim(), 10);
  const rest = line.slice(close + 1).trim().split(/\s+/u);
  const ppid = Number.parseInt(rest[1] ?? '', 10);
  return Number.isInteger(pid) && Number.isInteger(ppid) ? { pid, ppid, comm: line.slice(open + 1, close), state: rest[0] ?? '' } : null;
}

/** One `/proc/<pid>/stat` line as `{ pid, ppid, comm }`, or null. */
export function parseProcStat(line) {
  const stat = parseStat(line);
  return stat ? { pid: stat.pid, ppid: stat.ppid, comm: stat.comm } : null;
}

/** The reads a listing is made of, over a real `/proc` folder — the seam a test replaces with an in-memory /proc (the product's FakeProcReads). */
export const procReads = (proc) => {
  const at = (pid, leaf) => join(proc, String(pid), leaf);
  return {
    list: () => readdirSync(proc).filter((name) => /^\d+$/u.test(name)),
    stat: (pid) => readFileSync(at(pid, 'stat'), 'utf8'),
    status: (pid) => readFileSync(at(pid, 'status'), 'utf8'),
    exe: (pid) => readlinkSync(at(pid, 'exe')),
    cmdline: (pid) => readFileSync(at(pid, 'cmdline'), 'utf8'),
  };
};

/** The kernel appends this to an `exe` link whose binary was replaced or removed (LinuxProcessListing.DeletedSuffix). */
const DELETED = ' (deleted)';
/** A read that failed because the process left between the listing and the read: never evidence of anything. */
const gone = (e) => e?.code === 'ENOENT' || e?.code === 'ESRCH';

/** Whether `/proc/<pid>/status` says the process runs as `uid` (ProcStatus.RunsAs): the real uid on the `Uid:` line. No uid asked for = every account. */
function runsAs(reads, pid, uid) {
  if (uid === undefined) return true;
  const match = /^Uid:\s*(\d+)/mu.exec(reads.status(pid));
  if (!match) throw new Error(`/proc/${pid}/status: no Uid line, so whose process it is cannot be told`);
  return Number(match[1]) === uid;
}

/** The file name of an `exe` target, after ONE trailing " (deleted)" is taken off (LinuxProcessListing.BaseName). */
const exeName = (target) => posix.basename(target.endsWith(DELETED) ? target.slice(0, -DELETED.length) : target);

/**
 * The `exe` is hidden: the process's own claim, `argv[0]` (the basename; the " (deleted)" suffix is the kernel's and is never on it). Gone is
 * null; only a `cmdline` that is hidden too makes the process unidentifiable, which the listing refuses to read as "not a Kimi".
 */
function argvZeroName(pid, reads, exeReason) {
  try {
    return posix.basename(reads.cmdline(pid).split('\0', 1)[0]);
  } catch (e) {
    if (gone(e)) return null;
    throw new Error(`pid ${pid} can be identified neither by /proc/${pid}/exe (${exeReason}) nor by /proc/${pid}/cmdline (${e.message})`);
  }
}

/** What the product's listing calls the process's name: the `exe` link's basename, else `argv[0]`'s; null when it left meanwhile. */
function nameOf(pid, reads) {
  try {
    return exeName(reads.exe(pid));
  } catch (e) {
    return argvZeroName(pid, reads, e.message);
  }
}

/** One process as `{ pid, ppid, comm, name }`, or [] for one that left, is a zombie, or belongs to another account (ProcessTree.ExitedEntry, ProcStatus.RunsAs). */
function describe(pid, reads, uid) {
  try {
    const stat = parseStat(reads.stat(pid));
    if (!stat || stat.state === 'Z' || !runsAs(reads, pid, uid)) return [];
    const name = nameOf(pid, reads);
    return name === null ? [] : [{ pid: stat.pid, ppid: stat.ppid, comm: stat.comm, name }];
  } catch (e) {
    if (gone(e)) return [];
    throw e;
  }
}

/**
 * Every process of this account `/proc` lists, as `{ pid, ppid, comm, name }` — `name` read as `LinuxProcessListing` reads it
 * (src_mcp/runners/Kimi/LinuxProcessListing.cs:100-134), so `isKimi` answers as the product does. A process that ended between the listing
 * and its read is skipped. A same-account process that can be identified by neither `exe` nor `cmdline` THROWS, naming it: the product makes
 * the whole listing unreadable there, and a guard must not read "cannot tell" as "no kimi".
 */
export function linuxProcesses(proc = '/proc', reads = procReads(proc), { uid = process.getuid?.() } = {}) {
  return reads.list().flatMap((pid) => describe(Number(pid), reads, uid));
}

/** The names a Kimi runs under (ForeignKimiWatch.KimiNames) and the second witness, a TUI's `comm` (ForeignKimiWatch.KimiComm). */
const KIMI_NAMES = ['kimi', 'kimi.exe'];
const KIMI_COMM = 'kimi';

/**
 * Whether a listed process is a Kimi: its name (the executable, or `argv[0]` when the executable is hidden) is `kimi` / `kimi.exe`, in any case,
 * OR its `comm` is `kimi`. A PORT of `ForeignKimiWatch.IsKimi` (src_mcp/runners/Kimi/ForeignKimi.cs:51-54, :115) — the ONLY Kimi test the probes
 * have. A `comm` of `MainThread` alone is NOT Kimi (a running `kimi acp` has it): the executable is the witness, exactly as in the product.
 */
export const isKimi = (entry) => KIMI_NAMES.includes(String(entry?.name ?? '').toLowerCase()) || entry?.comm === KIMI_COMM;

/**
 * Every live process as `{ pid, ppid, comm, name }` — a LISTING; nothing here ends a process. Linux: `/proc`. Windows: PowerShell by its FIXED path
 * under System32 and never a bare name, so no folder of the PATH (which a person's own tools can write to) decides what runs (S4036); the cmdlet and
 * the string are the same in 5.1 as in pwsh 7. There `name` is the image name and `comm` is empty. A listing that did not COMPLETE (no PowerShell, a
 * timeout, a non-zero exit) throws, as the Linux one does for a process it cannot identify: "cannot tell" must never read as "no kimi runs".
 * `spawn` is injected so a test can fail it.
 */
export function listProcesses(platform = process.platform, spawn = spawnSync) {
  if (platform !== 'win32') return linuxProcesses();
  const r = spawn(SYSTEM32(String.raw`WindowsPowerShell\v1.0\powershell.exe`), ['-NoProfile', '-NonInteractive', '-Command',
    'Get-CimInstance Win32_Process | ForEach-Object { "$($_.ProcessId),$($_.ParentProcessId),$($_.Name)" }'], { encoding: 'utf8', timeout: 30_000 });
  if (r.error || r.signal || r.status !== 0) {
    throw new Error(`the Windows process listing failed (${r.error?.code ?? r.signal ?? `exit ${r.status}`}): whether another kimi runs cannot be told`);
  }
  return String(r.stdout ?? '').split(/\r?\n/u).filter(Boolean).map((line) => {
    const [pid, ppid, ...name] = line.split(',');
    return { pid: Number(pid), ppid: Number(ppid), comm: '', name: name.join(',') };
  });
}

const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * The "no other kimi" guard (DN5 items 1 and 2): before a launch that could refresh the person's sign-in, waits (default 10 minutes) for any other
 * Kimi to end — two refreshers can race the rotating refresh token and sign the person out. True = none runs; false = one still did when the wait
 * ran out, and the launch must be SKIPPED. The listing, the clock and the sleep are injected, so the rule is tested without a real process.
 */
export async function waitForNoOtherKimi(label, { list = listProcesses, sleep = pause, now = () => performance.now(), log = console.error, waitMs = 600_000, pollMs = 10_000, tag = 'probe', except = [], startTimeOf = startTimeOfPid } = {}) {
  const until = now() + waitMs;
  // `except`: processes the caller owns and may leave running (N-K2's live variant lets the killed arm's OWN orphan stand), each `{ pid, startTime }`.
  // A process is excepted only when its pid AND its start time still match what was recorded — a reused pid is a stranger — and a start time that
  // could not be read excepts nothing. Never by name: every other kimi blocks.
  const excepted = (p) => except.some((e) => e.pid === p.pid && e.startTime !== '' && startTimeOf(p.pid) === e.startTime);
  while (true) {
    const others = list().filter(isKimi).filter((p) => !excepted(p));
    if (others.length === 0) return true;
    const pids = others.map((p) => p.pid).join(', ');
    if (now() > until) {
      log(`[${tag}] ${label}: SKIPPED — another kimi still runs after ${Math.round(waitMs / 6000) / 10} minutes (pids ${pids}); two refreshers could sign the person out`);
      return false;
    }
    log(`[${tag}] ${label}: waiting — another kimi runs (pids ${pids})`);
    await sleep(pollMs);
  }
}

/**
 * Held / Free / Absent for a lock file on Linux. .NET turns `FileShare.None` into an advisory `flock` there, so a plain open
 * succeeds whatever is held (the Windows probe's open-and-see answers Free every time); the question is asked of flock(1)
 * instead: `flock -n -E 75 <file> true` tries an EXCLUSIVE lock without waiting — 75 means another holder would keep a second
 * taker out. The try itself holds the lock for the instant `true` runs, the same window the Windows open had.
 */
export function lockState(path, { flock = FLOCK, spawn = spawnSync } = {}) {
  if (!existsSync(path)) return 'Absent';
  const r = spawn(flock, ['-n', '-E', '75', path, 'true'], { encoding: 'utf8', timeout: 10_000 });
  if (r.status === 0) return 'Free';
  return r.status === 75 ? 'Held' : `Unknown:${r.status ?? r.error?.code ?? r.signal ?? 'no-exit'}`;
}

/**
 * A process's start time — field 22 of its `/proc/<pid>/stat` (clock ticks since boot), the other half of an identity (a pid alone can be reused) —
 * or '' when the line has no such field.
 */
export function parseStartTime(line) {
  const close = line.lastIndexOf(')');
  const field = close < 0 ? '' : (line.slice(close + 1).trim().split(/\s+/u)[19] ?? '');
  return /^\d+$/u.test(field) ? field : '';
}

/** The start time of `pid` now, or '' when it is gone or unreadable (and always '' off Linux). */
export function startTimeOfPid(pid, reads = procReads('/proc')) {
  try {
    return parseStartTime(reads.stat(pid));
  } catch {
    return '';
  }
}
