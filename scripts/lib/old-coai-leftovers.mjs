/**
 * The one-off clean-up of old `coai-*` test leftovers — the logic (E5 S2-A, D4; `todo/PLAN_test_leftovers_E5_S2_spec.md` decision 5).
 *
 * A person's tool, run with no coai session and no VS Code window open on the machine; never a sweep, never the janitor's, never wired into
 * a runner. `listOldLeftovers` says what is there, with age and identity; the person picks names; `removePicked` takes ONLY those, and only
 * while each is still the object that was listed. Nothing here removes by a name read earlier: the pick is MOVED aside under a quarantine name
 * first (atomic, same parent, so the name is free and a later listing finds the quarantine if this run is killed), the moved object is checked
 * again, and only then removed, by the quarantine's path.
 *
 * The checks of one pick, in this order — the first that fails refuses the pick under its name and the run goes on to the next:
 * `not-listed`, `protected`, `missing`, `not-a-directory`, `changed`, `owner-<state>`, `quarantine-taken`, `moved-but-changed`; any other
 * error from the file system refuses with `error-<code>`. Nothing throws out of `removePicked`.
 *
 * THE RESIDUAL WINDOW (S2-A gate finding 4). The `changed` check and the rename are two calls: an object swapped in between is moved aside under
 * the quarantine name instead of the one the person picked. The check after the move (device, inode AND last-write time against the listing) sees
 * it and KEEPS it — nothing is removed — but the swapped-in object sits under a dot name until the person reads the next `list`, which shows it as
 * an interrupted clean-up. Between that check and the removal only the same user can swap the dot-named quarantine, which is the threat this
 * tool does not guard against. An object that comes back with the same inode AND the same last-write time to the nanosecond is not told apart.
 *
 * The identity helpers (`identityOf`, the exclusive move) are COPIES of the reference's, not imports: the conventions do not export them, and this
 * story merges before any new conventions release. Source: `.agents/conventions/tools/lib/leftovers-sweep.mjs` (`identityOf`, `moveExclusively`,
 * `isOwnEmptyPlaceholder`, `removeOwnEmptyPlaceholder`, `moveToQuarantine`) at pin 105d53c. FOLLOW-UP: when the conventions export them, switch to
 * the exported functions and delete the copies.
 */
import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { isTakenRename } from '../../.agents/conventions/tools/lib/leftovers-sweep.mjs';
import { legacyOwnerState, parseLegacyOwner, processProbe, readClaimText } from '../../.agents/conventions/tools/lib/owned.mjs';

/** The quarantine names a move draws before it gives up (the reference's `QUARANTINE_DRAWS`). */
export const MOVE_DRAWS = 5;
const OWNER_SUFFIX = '.owner';
const LEGACY_RUN = /^testsToDelete-coai-run-[0-9a-f]{6}$/;
/** A quarantine of THIS script: the picked name, `.teststodelete-quarantine-`, a pid, eight hex. */
const QUARANTINE = /^\.(coai-.+|testsToDelete-coai-run-[0-9a-f]{6})\.teststodelete-quarantine-([0-9]+)-([0-9a-f]{8})$/i;
/** What rename(2) answers when the name is occupied: a directory with content, or a file where our directory was. */
const TAKEN_CODES = new Set(['ENOTEMPTY', 'EEXIST', 'ENOTDIR']);

/** `'coai'`, `'legacy-run'` or `null` (protected, a cache, or no candidate at all) — judged by name, NOW, from the product's list. */
export function candidateKind(name, neverSwept) {
  if (LEGACY_RUN.test(name)) return 'legacy-run';
  const product = neverSwept.some((owned) => name.startsWith(owned));
  return name.startsWith('coai-') && !/^coai-[0-9]/.test(name) && !product ? 'coai' : null;
}

/** What a name is for this tool: its kind, and for a quarantine the original name it holds. `null` when it is not ours to touch. */
function judge(name, neverSwept) {
  const quarantine = QUARANTINE.exec(name);
  if (quarantine === null) {
    const kind = candidateKind(name, neverSwept);
    return kind === null ? null : { kind, original: name };
  }
  return candidateKind(quarantine[1], neverSwept) === null ? null : { kind: 'quarantine', original: quarantine[1] };
}

// --- identity: copies of the reference's (see the header) ---------------------------------------------------------

/** `{ dev, ino, mtimeNs, directory }` of an entry itself (a link is not followed), or `undefined` when it is not there. */
function identityOf(target, io) {
  try {
    const stat = io.lstatSync(target, { bigint: true });
    return { dev: stat.dev, ino: stat.ino, mtimeNs: stat.mtimeNs, directory: stat.isDirectory() };
  } catch {
    return undefined;
  }
}

const sameObject = (a, b) => a !== undefined && b !== undefined && a.dev === b.dev && a.ino === b.ino && a.directory === b.directory;

/** Is `placeholder` still the directory this move reserved: a directory, not a link, the reserved `(dev, ino)`, and empty? */
function isOwnEmptyPlaceholder(placeholder, reserved, io) {
  const now = identityOf(placeholder, io);
  if (!sameObject(now, reserved) || !now.directory) return false;
  try {
    return io.readdirSync(placeholder).length === 0;
  } catch {
    return false;
  }
}

/** Remove the reserved placeholder only while it is still ours and empty (`rmdir`, never recursive): a name taken over in between is never touched. */
function removeOwnEmptyPlaceholder(placeholder, reserved, io) {
  try {
    if (isOwnEmptyPlaceholder(placeholder, reserved, io)) io.rmdirSync(placeholder);
  } catch { /* gone, or not empty: leave it */ }
}

/**
 * Rename `from` to `to` ONLY if nothing is at `to`: `true` when it moved, `false` when the name is taken, any other failure throws. POSIX: Node has no
 * `renameat2(RENAME_NOREPLACE)` and `rename(2)` replaces an EMPTY directory, so the name is reserved with an exclusive `mkdir` and the rename lands on our
 * own empty placeholder, checked again immediately before the rename. S2-A gate finding 0: whenever the rename did not complete, the placeholder is removed in
 * a `finally` — only while it is still our empty reservation.
 */
function moveExclusively(from, to, io, hooks, platform) {
  if (platform === 'win32') {
    try {
      io.renameSync(from, to); // onto an existing directory this fails by itself
    } catch (error) {
      if (isTakenRename(error, identityOf(to, io) !== undefined, 'win32')) return false;
      throw error;
    }
    return true;
  }
  try {
    io.mkdirSync(to, { mode: 0o700 }); // exclusive: an existing folder, file or link under the name refuses
  } catch (error) {
    if (error?.code === 'EEXIST') return false;
    throw error;
  }
  const reserved = identityOf(to, io);
  let moved = false;
  try {
    hooks.afterReserve?.(to, from);
    if (!isOwnEmptyPlaceholder(to, reserved, io)) return false; // not ours any more: never renamed onto, never removed
    hooks.beforeRename?.(to, from);
    io.renameSync(from, to);
    moved = true;
    return true;
  } catch (error) {
    if (TAKEN_CODES.has(error?.code)) return false;
    throw error;
  } finally {
    if (!moved) removeOwnEmptyPlaceholder(to, reserved, io);
  }
}

function quarantineName(dir) {
  return path.join(path.dirname(dir), `.${path.basename(dir)}.teststodelete-quarantine-${process.pid}-${crypto.randomBytes(4).toString('hex')}`);
}

/** Move `dir` to a quarantine name that was free: a taken draw draws a new name, `MOVE_DRAWS` times at most. The quarantine's path, or `undefined` when all were taken. */
function moveToQuarantine(dir, io, hooks, platform) {
  for (let draw = 0; draw < MOVE_DRAWS; draw += 1) {
    const to = (hooks.drawName ?? quarantineName)(dir);
    if (moveExclusively(dir, to, io, hooks, platform)) return to;
  }
  return undefined;
}

// --- the listing --------------------------------------------------------------------------------------------------

/** The state of the claim file beside `original`: `none`, `unreadable`, or the legacy grammar's `gone` / `live` / `unknown` (a Linux reader never says `gone`). */
function claimState(tmp, original, { probe, claimPlatform }) {
  const read = readClaimText(path.join(tmp, `${original}${OWNER_SUFFIX}`));
  if (read === null) return 'none';
  if (typeof read !== 'string') return 'unreadable';
  return legacyOwnerState(parseLegacyOwner(read), probe, { platform: claimPlatform });
}

const identityFields = (stat) => ({ dev: String(stat.dev), ino: String(stat.ino), mtimeNs: String(stat.mtimeNs) });

/** The entry of one directory: kind, age, identity, and what the kind adds (`of` and `empty` for a quarantine, `claim` for a legacy run folder). */
function entryOf(tmp, name, judged, { now, io, probe, claimPlatform }) {
  const stat = io.lstatSync(path.join(tmp, name), { bigint: true });
  const lastWriteMs = Number(stat.mtimeNs / 1_000_000n);
  const isRunFolder = LEGACY_RUN.test(judged.original);
  return {
    name,
    kind: judged.kind,
    lastWriteMs,
    ageMinutes: Math.floor((now - lastWriteMs) / 60_000),
    ...identityFields(stat),
    ...(judged.kind === 'quarantine' ? { of: judged.original, empty: io.readdirSync(path.join(tmp, name)).length === 0 } : {}),
    ...(isRunFolder ? { claim: claimState(tmp, judged.original, { probe, claimPlatform }) } : {}),
  };
}

/**
 * What `tmp` holds that this tool may offer: `{ tmp, entries }`. `tmp` is the root's absolute path, recorded so `remove` reads the same root (S2-A gate finding 2).
 * Product prefixes, the version cache, files, links and every other name are left out — they were never candidates. A quarantine of this script is
 * listed as an interrupted clean-up (S2-A gate finding 3).
 */
export function listOldLeftovers({ tmp, neverSwept, now = Date.now(), io = fs, probe = processProbe, claimPlatform = process.platform }) {
  const entries = io.readdirSync(tmp, { withFileTypes: true })
    .filter((dirent) => dirent.isDirectory())
    .flatMap((dirent) => {
      const judged = judge(dirent.name, neverSwept);
      return judged === null ? [] : [entryOf(tmp, dirent.name, judged, { now, io, probe, claimPlatform })];
    })
    .sort((a, b) => (a.name < b.name ? -1 : 1));
  return { tmp: path.resolve(tmp), entries };
}

// --- the removal --------------------------------------------------------------------------------------------------

const refused = (name, check) => ({ name, outcome: 'refused', check });
const sameAsListed = (now, entry) => String(now.dev) === entry.dev && String(now.ino) === entry.ino && String(now.mtimeNs) === entry.mtimeNs;

/** The checks that need only a look at the name now: the first that fails. */
function refusalBefore(tmp, entry, judged, ctx) {
  const target = path.join(tmp, entry.name);
  const now = identityOf(target, ctx.io);
  if (now === undefined) return 'missing';
  if (!now.directory) return 'not-a-directory'; // lstat: a link or a file at the name
  if (!sameAsListed(now, entry)) return 'changed';
  const state = LEGACY_RUN.test(judged.original) ? claimState(tmp, judged.original, ctx) : 'none';
  return state === 'gone' || state === 'none' ? undefined : `owner-${state}`;
}

/** A legacy run folder's claim file, last: a claim that is not there is nothing to remove. */
function removeClaim(file, io) {
  try {
    io.unlinkSync(file);
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
  }
}

/** The last step: the quarantined object (checked again where it is) removed by ITS path, then a legacy run folder's claim file. */
function removeQuarantined(tmp, entry, judged, quarantined, ctx) {
  const now = identityOf(quarantined, ctx.io);
  if (now === undefined || !sameAsListed(now, entry)) return refused(entry.name, 'moved-but-changed'); // left in place, never removed; the next listing shows it
  ctx.io.rmSync(quarantined, { recursive: true, force: true });
  if (LEGACY_RUN.test(judged.original)) removeClaim(path.join(tmp, `${judged.original}${OWNER_SUFFIX}`), ctx.io);
  return { name: entry.name, outcome: 'removed' };
}

function removeOne(tmp, entry, ctx) {
  const judged = judge(entry.name, ctx.neverSwept);
  if (judged === null || judged.kind !== entry.kind) return refused(entry.name, 'protected'); // re-judged now, not trusted from the listing
  const check = refusalBefore(tmp, entry, judged, ctx);
  if (check !== undefined) return refused(entry.name, check);
  const target = path.join(tmp, entry.name);
  if (judged.kind === 'quarantine') return removeQuarantined(tmp, entry, judged, target, ctx);
  const quarantined = moveToQuarantine(target, ctx.io, ctx.hooks, ctx.platform);
  if (quarantined === undefined) return refused(entry.name, 'quarantine-taken');
  ctx.hooks.afterRename?.(target, quarantined);
  return removeQuarantined(tmp, entry, judged, quarantined, ctx);
}

/**
 * Removes the `picks` named in `listing`, one result per pick: `{ name, outcome: 'removed' | 'refused', check }`. `hooks` are the seams the tests use:
 * `drawName(dir)` (the full quarantine path), `afterReserve`, `beforeRename`, `afterRename(from, quarantined)` and `platform` (the move's, default `process.platform`).
 */
export function removePicked({ tmp, listing, picks, neverSwept, io = fs, hooks = {}, probe = processProbe, claimPlatform = process.platform }) {
  const ctx = { io, hooks, neverSwept, probe, claimPlatform, platform: hooks.platform ?? process.platform };
  return picks.map((name) => {
    const entry = listing.entries.find((e) => e.name === name);
    if (entry === undefined) return refused(name, 'not-listed');
    try {
      return removeOne(tmp, entry, ctx);
    } catch (error) {
      return refused(name, `error-${error?.code ?? 'unknown'}`);
    }
  });
}
