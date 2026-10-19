// lib/proc.mjs — the Linux halves of the Kimi tree-hold probe (scripts/probe-kimi-tree-hold.mjs, plan §5 of
// research/PLAN_kimi_and_vibe_on_linux.md): who is running, read from /proc, and whether a lock file is held, asked of flock(1).
// Both are READ-ONLY questions: nothing here ends a process or keeps a lock.
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/** util-linux's flock(1) by its absolute path, never looked up through PATH (S4036); '' where it is not installed. */
export const FLOCK = ['/usr/bin/flock', '/bin/flock'].find((p) => existsSync(p)) ?? '';

/**
 * One `/proc/<pid>/stat` line as `{ pid, ppid, name }`, or null. The name (`comm`, at most 15 characters) is in parentheses and
 * may itself hold `)` or spaces, so it runs to the LAST `)`; the state and the parent pid follow it.
 */
export function parseProcStat(line) {
  const open = line.indexOf('(');
  const close = line.lastIndexOf(')');
  if (open <= 0 || close <= open) return null;
  const pid = Number.parseInt(line.slice(0, open).trim(), 10);
  const rest = line.slice(close + 1).trim().split(/\s+/u);
  const ppid = Number.parseInt(rest[1] ?? '', 10);
  return Number.isInteger(pid) && Number.isInteger(ppid) ? { pid, ppid, name: line.slice(open + 1, close) } : null;
}

/** Every process `/proc` lists, as `{ pid, ppid, name }` — a process that ended between the listing and its read is skipped. */
export function linuxProcesses(proc = '/proc') {
  return readdirSync(proc).filter((name) => /^\d+$/u.test(name)).flatMap((name) => {
    try {
      const parsed = parseProcStat(readFileSync(join(proc, name, 'stat'), 'utf8'));
      return parsed ? [parsed] : [];
    } catch {
      return [];
    }
  });
}

/**
 * Held / Free / Absent for a lock file on Linux. .NET turns `FileShare.None` into an advisory `flock` there, so a plain open
 * succeeds whatever is held (the Windows probe's open-and-see answers Free every time); the question is asked of flock(1)
 * instead: `flock -n -E 75 <file> true` tries an EXCLUSIVE lock without waiting — 75 means another holder would keep a second
 * taker out. The try itself holds the lock for the instant `true` runs, the same window the Windows open had.
 */
export function lockState(path) {
  if (!existsSync(path)) return 'Absent';
  const r = spawnSync(FLOCK, ['-n', '-E', '75', path, 'true'], { encoding: 'utf8', timeout: 10_000 });
  if (r.status === 0) return 'Free';
  return r.status === 75 ? 'Held' : `Unknown:${r.status ?? r.error?.code}`;
}
