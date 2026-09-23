/**
 * Removes what earlier test runs left in the machine's temp directory — BEFORE this run starts.
 *
 * <p>The operator's ruling of 2026-09-18: tests do not clean up after themselves and must, and not
 * after each test but before the run — everything older than ten minutes goes. A cleanup hung off
 * each test only runs when the test ends, and the runs that leave the most behind are exactly the
 * ones that never end: Ctrl-C, a killed runner, a host that crashed. One sweep at the start always
 * runs. Measured before it existed: 5 455 `coai-*` directories after one day, and a full extension
 * run adding about 3 300.</p>
 *
 * <p>The rule is `shared/temp-sweep.json`, which the server suite's own sweep reads too. The
 * decision is {@link toSweep}, pure, over names and times; {@link sweep} is the thin half that
 * touches a disk, and it never fails a run — a directory it cannot remove is counted and stepped
 * over, because a suite that refuses to start over housekeeping has made things worse.</p>
 */
import fs from 'node:fs';
import path from 'node:path';

/** The rule both runners share, read from the checkout's `shared/` folder. */
export function readRule(repoRoot) {
  return JSON.parse(fs.readFileSync(path.join(repoRoot, 'shared', 'temp-sweep.json'), 'utf8'));
}

/** A downloaded server, `coai-<version>`: a cache, never scratch. */
function versionCache(name, rule) {
  return /^\d/.test(name.slice(rule.prefix.length));
}

/** Whether one entry is a leftover this run may take. */
function leftover(entry, now, rule) {
  return entry.isDirectory
    && entry.name.startsWith(rule.prefix)
    && !versionCache(entry.name, rule)
    && !rule.neverSwept.some((owned) => entry.name.startsWith(owned))
    && now - entry.lastWriteMs > rule.keepMinutes * 60_000;
}

/**
 * The names to remove, out of `entries` (`{ name, isDirectory, lastWriteMs }`) at `now`.
 *
 * <p>The LAST WRITE, not the creation: Windows hands a recreated name its predecessor's creation
 * time, so a directory made this minute under a name used yesterday would read as a day old.</p>
 */
export function toSweep(entries, now, rule) {
  return entries.filter((entry) => leftover(entry, now, rule)).map((entry) => entry.name);
}

/** What `dir` holds, as {@link toSweep} reads it. An entry that vanished mid-listing is skipped. */
function entriesOf(dir, io) {
  return io.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    try {
      const stat = io.statSync(path.join(dir, entry.name));
      return [{ name: entry.name, isDirectory: entry.isDirectory(), lastWriteMs: stat.mtimeMs }];
    } catch {
      return [];
    }
  });
}

/** Removes one directory, answering whether it went. */
function removed(target, io) {
  try {
    io.rmSync(target, { recursive: true, force: true });
    return true;
  } catch {
    return false;
  }
}

/**
 * Sweeps `dir` at `now`, and says how it went: `{ removed, failed }`.
 *
 * `io` is `node:fs` unless a test hands it something that fails on purpose.
 */
export function sweep(dir, now, rule, io = fs) {
  const outcomes = toSweep(entriesOf(dir, io), now, rule).map((name) => removed(path.join(dir, name), io));

  return {
    removed: outcomes.filter(Boolean).length,
    failed: outcomes.filter((ok) => !ok).length,
  };
}
