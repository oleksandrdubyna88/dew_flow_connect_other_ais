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
 * `bad-name` (S2-A Opus 1: a name is ONE path segment, or no path is ever built from it), `not-listed`, `protected`, `missing`,
 * `not-a-directory`, `foreign-uid` (S2-A Opus 4: on POSIX the caller owns what it removes, rule §3), `changed`, `owner-<state>` (a legacy run
 * folder's claim; for a quarantine of this script, the state of the process whose pid its name carries — S2-A Opus 3: alive or unaskable keeps it),
 * `quarantine-taken`, `moved-but-changed`; any other error from the file system refuses with `error-<code>`; an ENOENT from the rename is `missing`
 * (S2-A gate 4). After the move a legacy run folder's claim is read again before anything is removed (S2-A Opus 5), and the uid again (S2-A code2 1). The root is the
 * listing's: `bad-listing` / `wrong-root` refuse every pick before any of these (S2-A code2 7). A pick that is not listed refuses ALL the picks before anything is
 * removed (`not-listed` for it, `not-attempted` for the rest — S2-A code3 3). A refusal after the move says where the folder is now (S2-A code3 0). Nothing throws out of `removePicked`.
 *
 * THE RESIDUAL WINDOW (S2-A gate finding 4). The `changed` check and the rename are two calls: an object swapped in between is moved aside under
 * the quarantine name instead of the one the person picked. The check after the move (device, inode AND last-write time against the listing) sees
 * it and KEEPS it — nothing is removed — but the swapped-in object sits under a dot name until the person reads the next `list`, which shows it as
 * an interrupted clean-up. Between that check and the removal only the same user can swap the dot-named quarantine, which is the threat this
 * tool does not guard against. An object that comes back with the same inode AND the same last-write time to the nanosecond is not told apart.
 *
 * The exclusive move, the placeholder checks and the quarantine draw are the conventions' own (`.agents/conventions/tools/lib/leftovers-sweep.mjs`, exported since
 * release 5863c60), imported — not copied (E5 S2-A code5 2/8/9; the copy of the first release is gone). The one thing kept here is a test seam: `hooks.drawName` names the
 * quarantine, which the reference's `moveToQuarantine` cannot; without that hook the reference's own `moveToQuarantine` runs.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { moveExclusively, moveToQuarantine as moveToQuarantineOf } from '../../.agents/conventions/tools/lib/leftovers-sweep.mjs';
import { hidepidActive, isAskablePid, legacyOwnerState, parseLegacyOwner, processProbe, readClaimText } from '../../.agents/conventions/tools/lib/owned.mjs';

/** The quarantine names the `hooks.drawName` seam draws before it gives up (the reference's own bound is its `QUARANTINE_DRAWS`, also 5). */
export const MOVE_DRAWS = 5;
const OWNER_SUFFIX = '.owner';
const LEGACY_RUN = /^testsToDelete-coai-run-[0-9a-f]{6}$/;
/** A quarantine of THIS script: the picked name, `.teststodelete-quarantine-`, a pid, eight hex. */
const QUARANTINE = /^\.(coai-[^/\\]+|testsToDelete-coai-run-[0-9a-f]{6})\.teststodelete-quarantine-([0-9]+)-([0-9a-f]{8})$/i;

/** Is `name` ONE path segment: not empty, not `.` or `..`, no separator of either kind, no `:` (`C:foo` is drive-relative on Windows; S2-A code5 3) and no NUL, and its own basename? A name that is not is never joined to a path. */
export function isOneSegment(name) {
  return typeof name === 'string' && name !== '' && name !== '.' && name !== '..' && !/[/\\:\0]/.test(name) && name === path.basename(name);
}

/** `'coai'`, `'legacy-run'` or `null` (protected, a cache, or no candidate at all) — judged by name, NOW, from the product's list. */
export function candidateKind(name, neverSwept) {
  if (LEGACY_RUN.test(name)) return 'legacy-run';
  const product = neverSwept.some((owned) => name.startsWith(owned));
  return name.startsWith('coai-') && !/^coai-[0-9]/.test(name) && !product ? 'coai' : null;
}

/** What a name is for this tool: its kind, and for a quarantine the original name it holds. `null` when it is not ours to touch. */
export function judge(name, neverSwept) {
  const quarantine = QUARANTINE.exec(name);
  if (quarantine === null) {
    const kind = candidateKind(name, neverSwept);
    return kind === null ? null : { kind, original: name };
  }
  // [S2-A code2 0] defence in depth: the pattern already excludes separators, and the original must be ONE segment before any claim is read or unlinked.
  if (!isOneSegment(quarantine[1]) || candidateKind(quarantine[1], neverSwept) === null) return null;
  return { kind: 'quarantine', original: quarantine[1], pid: quarantine[2] };
}

// --- what is at a name ---------------------------------------------------------------------------------------------

/** `{ dev, ino, mtimeNs, uid, directory }` of an entry itself (a link is not followed), or `undefined` when it is not there. */
function statOf(target, io) {
  try {
    const stat = io.lstatSync(target, { bigint: true });
    return { dev: stat.dev, ino: stat.ino, mtimeNs: stat.mtimeNs, uid: Number(stat.uid), directory: stat.isDirectory() };
  } catch {
    return undefined;
  }
}

/**
 * Move `dir` aside under a quarantine name that was free. The conventions' `moveToQuarantine` draws the name and throws when every draw was taken (`undefined` here); with
 * the `hooks.drawName` test seam the draws are ours, `MOVE_DRAWS` at most. The quarantine's path, or `undefined` when all were taken.
 */
function moveToQuarantine(dir, hooks, options) {
  if (hooks.drawName === undefined) {
    try {
      return moveToQuarantineOf(dir, hooks, options);
    } catch (error) {
      if (String(error?.message).startsWith('cannot move ')) return undefined;
      throw error;
    }
  }
  for (let draw = 0; draw < MOVE_DRAWS; draw += 1) {
    const to = hooks.drawName(dir);
    if (moveExclusively(dir, to, hooks, options)) return to;
  }
  return undefined;
}

// --- the listing --------------------------------------------------------------------------------------------------

/**
 * The state of the claim file beside `original`: `none`, `unreadable`, or the legacy grammar's `gone` / `live` / `unknown` (a Linux reader never says `gone`).
 * [S2-A code2 2] A read that fails in ANY way — a directory or a FIFO in the claim's place, EACCES, a throw — is `unreadable`, which refuses a pick and never aborts a listing.
 */
function claimState(tmp, original, { probe, claimPlatform, readClaim = readClaimText }) {
  try {
    const read = readClaim(path.join(tmp, `${original}${OWNER_SUFFIX}`));
    if (read === null) return 'none';
    if (typeof read !== 'string') return 'unreadable';
    return legacyOwnerState(parseLegacyOwner(read), probe, { platform: claimPlatform });
  } catch {
    return 'unreadable';
  }
}

const identityFields = (stat) => ({ dev: String(stat.dev), ino: String(stat.ino), mtimeNs: String(stat.mtimeNs) });

/** The entry of one directory: kind, age, identity, and what the kind adds (`of` and `empty` for a quarantine, `claim` for a legacy run folder). */
function entryOf(tmp, name, judged, { now, io, probe, claimPlatform, readClaim }) {
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
    ...(isRunFolder ? { claim: claimState(tmp, judged.original, { probe, claimPlatform, readClaim }) } : {}),
  };
}

/** Is this the shape of a name this tool would have offered, were it one segment? A folder of that shape that is not one on this system is REPORTED, not offered. */
function looksLikeOurs(name, neverSwept) {
  return candidateKind(name, neverSwept) !== null || /^\.(coai-|testsToDelete-coai-run-)/i.test(name);
}

/**
 * What `tmp` holds that this tool may offer: `{ tmp, entries, refused }`. `tmp` is the root's absolute path, recorded so `remove` reads the same root (S2-A gate finding 2).
 * Product prefixes, the version cache, files, links and every other name are left out — they were never candidates. A quarantine of this script is
 * listed as an interrupted clean-up (S2-A gate finding 3). [S2-A code2 6] A folder of our shape whose name is not ONE path segment on THIS system (a POSIX name holding a
 * backslash) is reported in `refused` as `bad-name` — seen, never offered — so nothing sits behind a removal that always refuses it.
 */
export function listOldLeftovers({ tmp: given, neverSwept, now = Date.now(), io = fs, probe = processProbe, claimPlatform = process.platform, readClaim = readClaimText }) {
  const tmp = path.resolve(given); // [S2-A code4 8] resolved ONCE, here: the scan and the recorded root are the same absolute folder whatever the working directory does next
  const directories = io.readdirSync(tmp, { withFileTypes: true }).filter((dirent) => dirent.isDirectory());
  const entries = directories
    .filter((dirent) => isOneSegment(dirent.name))
    .flatMap((dirent) => {
      const judged = judge(dirent.name, neverSwept);
      return judged === null ? [] : [entryOf(tmp, dirent.name, judged, { now, io, probe, claimPlatform, readClaim })];
    })
    .sort((a, b) => (a.name < b.name ? -1 : 1));
  const refusedNames = directories.filter((dirent) => !isOneSegment(dirent.name) && looksLikeOurs(dirent.name, neverSwept))
    .map((dirent) => ({ name: dirent.name, check: 'bad-name' })).sort((a, b) => (a.name < b.name ? -1 : 1));
  return { tmp, entries, refused: refusedNames };
}

/** Are two temp roots the same folder? By realpath (a link to the root is the root); a root that cannot be resolved is compared as an absolute path. */
export function sameRoot(a, b) {
  const real = (root) => {
    try {
      return fs.realpathSync.native(root);
    } catch {
      return path.resolve(root);
    }
  };
  return real(a) === real(b);
}

// --- the removal --------------------------------------------------------------------------------------------------

const refused = (name, check) => ({ name, outcome: 'refused', check });
const sameAsListed = (now, entry) => String(now.dev) === entry.dev && String(now.ino) === entry.ino && String(now.mtimeNs) === entry.mtimeNs;

/** Is the process whose pid a quarantine's name carries gone? `gone`, `live` or `unknown` (a pid nobody may ask about, a refused query, a list that may hide processes). */
function renamerState(pidText, { probe }) {
  if (!isAskablePid(pidText)) return 'unknown';
  let seen;
  try {
    seen = probe(Number(pidText));
  } catch {
    return 'unknown';
  }
  if (seen?.state === 'live') return 'live';
  return seen?.state === 'dead' && !hidepidActive() ? 'gone' : 'unknown';
}

/** The state that keeps a pick: a quarantine's renamer still alive or unaskable, or a legacy run folder's claim that is not `gone`. `undefined` when nothing keeps it. */
function ownerRefusal(tmp, judged, ctx) {
  const renamer = judged.kind === 'quarantine' ? renamerState(judged.pid, ctx) : 'gone';
  if (renamer !== 'gone') return `owner-${renamer}`;
  const state = LEGACY_RUN.test(judged.original) ? claimState(tmp, judged.original, ctx) : 'none';
  return state === 'gone' || state === 'none' ? undefined : `owner-${state}`;
}

/** On POSIX: is the object owned by somebody else than the caller? (No uid on Windows.) */
const isForeign = (identity) => typeof process.getuid === 'function' && identity.uid !== process.getuid();

/** The checks that need only a look at the name now: the first that fails. */
function refusalBefore(tmp, entry, judged, ctx) {
  const now = statOf(path.join(tmp, entry.name), ctx.io);
  if (now === undefined) return 'missing';
  if (!now.directory) return 'not-a-directory'; // lstat: a link or a file at the name
  if (isForeign(now)) return 'foreign-uid'; // the kernel owner is the caller's, or it is not ours to remove
  if (!sameAsListed(now, entry)) return 'changed';
  return ownerRefusal(tmp, judged, ctx);
}

/** A legacy run folder's claim file, last: a claim that is not there is nothing to remove. */
function removeClaim(file, io) {
  try {
    io.unlinkSync(file);
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
  }
}

/** A refusal AFTER the move: the object is aside, so the result says where (S2-A code3 0) and a later `list` / `remove` can finish it. */
const keptAside = (name, check, quarantined) => ({ ...refused(name, check), quarantine: path.basename(quarantined) });

/** The last step: the quarantined object (checked again where it is) removed by ITS path, then a legacy run folder's claim file. */
function removeQuarantined(tmp, entry, judged, quarantined, ctx) {
  const now = statOf(quarantined, ctx.io);
  if (now === undefined || !sameAsListed(now, entry)) return keptAside(entry.name, 'moved-but-changed', quarantined); // left in place, never removed; the next listing shows it
  if (isForeign(now)) return keptAside(entry.name, 'foreign-uid', quarantined); // [S2-A code2 1] the kernel owner is checked again where the object is now
  const claim = ownerRefusal(tmp, judged, ctx); // the claim is read AGAIN now that the folder is aside: a run that took it in between keeps the folder
  if (claim !== undefined) return keptAside(entry.name, claim, quarantined);
  try {
    ctx.io.rmSync(quarantined, { recursive: true, force: true });
  } catch (error) {
    return keptAside(entry.name, `error-${error?.code ?? 'unknown'}`, quarantined);
  }
  if (!LEGACY_RUN.test(judged.original)) return { name: entry.name, outcome: 'removed' };
  const claimFile = path.join(tmp, `${judged.original}${OWNER_SUFFIX}`);
  try {
    removeClaim(claimFile, ctx.io);
  } catch (error) {
    return { name: entry.name, outcome: 'partial', claimLeft: claimFile, cause: error?.code ?? 'unknown' }; // [S2-A code4 9] the folder is gone; the claim file is named, not hidden in an error
  }
  return { name: entry.name, outcome: 'removed' };
}

/** The move, with an ENOENT from the rename read as what it says: the pick is gone (S2-A gate 4). `undefined` when every drawn name was taken. */
function moveAside(target, ctx) {
  try {
    return moveToQuarantine(target, ctx.hooks, { io: ctx.io, platform: ctx.platform });
  } catch (error) {
    if (error?.code === 'ENOENT') return 'missing';
    throw error;
  }
}

function removeOne(tmp, entry, ctx) {
  const judged = judge(entry.name, ctx.neverSwept);
  if (judged === null || judged.kind !== entry.kind) return refused(entry.name, 'protected'); // re-judged now, not trusted from the listing
  const check = refusalBefore(tmp, entry, judged, ctx);
  if (check !== undefined) return refused(entry.name, check);
  const target = path.join(tmp, entry.name);
  if (judged.kind === 'quarantine') return removeQuarantined(tmp, entry, judged, target, ctx);
  const quarantined = moveAside(target, ctx);
  if (quarantined === undefined) return refused(entry.name, 'quarantine-taken');
  if (quarantined === 'missing') return refused(entry.name, 'missing');
  ctx.hooks.afterRename?.(target, quarantined);
  return removeQuarantined(tmp, entry, judged, quarantined, ctx);
}

/** The result of one pick: the name must be one path segment before anything else is looked at, and nothing throws. */
function decide(tmp, name, entries, ctx) {
  if (!isOneSegment(name)) return refused(String(name), 'bad-name'); // S2-A Opus 1: no path is built from a name that could leave the root
  const entry = entries.get(name);
  if (entry === undefined) return refused(name, 'not-listed');
  try {
    return removeOne(tmp, entry, ctx);
  } catch (error) {
    return refused(name, `error-${error?.code ?? 'unknown'}`);
  }
}

/**
 * Removes the `picks` named in `listing`, one result per pick: `{ name, outcome: 'removed' | 'partial' | 'refused', check }` (`partial`: the folder is gone and its claim file is left, named in `claimLeft`), each handed to `onResult` as soon as it is
 * decided (S2-A gate 6/9) and all returned; a result of a refusal after the move carries `quarantine` (the name it was moved to). The listing's entries are indexed by name once (S2-A gate 7). [S2-A code2 7] The root is the LISTING's: `listing.tmp` must be
 * an absolute path (else every pick is refused `bad-listing`), and a `tmp` argument that is another folder refuses every pick `wrong-root` — the deletion API itself holds the
 * confinement, not only the command that calls it. `hooks` are the seams the tests use: `drawName(dir)` (the full quarantine path), `afterReserve`, `beforeRename`,
 * `afterRename(from, quarantined)` and `platform` (the move's, default `process.platform`); `readClaim` reads a claim file.
 */
export function removePicked({ tmp, listing, picks, neverSwept, io = fs, hooks = {}, probe = processProbe, claimPlatform = process.platform, readClaim = readClaimText, onResult = () => {} }) {
  const root = listing?.tmp;
  const ctx = { io, hooks, neverSwept, probe, claimPlatform, readClaim, platform: hooks.platform ?? process.platform };
  const entries = new Map((listing?.entries ?? []).map((entry) => [entry.name, entry]));
  const rootCheck = typeof root !== 'string' || !path.isAbsolute(root) ? 'bad-listing' : tmp !== undefined && !sameRoot(tmp, root) ? 'wrong-root' : undefined;
  // [S2-A code3 3] Every pick is checked against the listing BEFORE any removal: one pick that is not listed (a typo) removes nothing, and every such name is reported.
  const problem = (name) => (!isOneSegment(name) ? 'bad-name' : entries.has(name) ? undefined : 'not-listed');
  const aborted = rootCheck === undefined && picks.some((name) => problem(name) !== undefined);
  return picks.map((name) => {
    const result = rootCheck !== undefined ? refused(String(name), rootCheck)
      : aborted ? refused(String(name), problem(name) ?? 'not-attempted')
        : decide(root, name, entries, ctx);
    onResult(result);
    return result;
  });
}

/** The line a person reads for one result: `removed <name>`, or `refused <name>: <check>` — and where the folder is now when it was moved aside first (S2-A code3 0 and 3). */
export function describeResult(result) {
  if (result.outcome === 'removed') return `removed ${result.name}`;
  if (result.outcome === 'partial') return `partial ${result.name}: the folder is removed, but its claim file is left at ${result.claimLeft} (${result.cause}); remove that file by hand`;
  const why = result.check === 'not-attempted' ? ' (another pick is not in the listing, so nothing was removed — fix the names and run it again)' : '';
  const aside = result.quarantine === undefined ? '' : ` — moved aside as ${result.quarantine}; run \`list\` again and \`remove\` that name to finish it`;
  return `refused ${result.name}: ${result.check}${why}${aside}`;
}
