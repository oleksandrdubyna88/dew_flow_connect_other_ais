// lib/vibe-manifest.mjs — the per-platform half of scripts/vibe-home-manifest.mjs (DL3 of todo/PLAN_kimi_and_vibe_on_linux.md):
// which entry a write replaces, where a not-yet-enforced entry is kept, and an inventory of a home that never reads through a link.
// The rule lists (VARYING, VARYING_FOLDERS) stay in the script itself: VibeHomeTests parses them there.
import { createHash } from 'node:crypto';
import { lstatSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { files } from './vibe.mjs';

/** An entry's platform as node spells it; an entry written before the field existed is the Windows one (DL3, the plan gate F6). */
export const platformOf = (entry) => entry.platform ?? 'win32';

const sameSlot = (a, b) => a.version === b.version && platformOf(a) === platformOf(b);
const byVersion = (a, b) => a.version.localeCompare(b.version, 'en', { numeric: true });
const replaced = (list, entry) => [...(list ?? []).filter((other) => !sameSlot(other, entry)), entry].sort(byVersion);

/**
 * `prior` with `entry` written into its slot — one per Vibe version PER PLATFORM, so a `linux` entry never replaces the `win32`
 * one of the same version. `stage` writes it into `staged` instead: the shipped coai-mcp (0.57.x) reads `versions` by version
 * alone, and a second 2.26.1 there would make every Windows 2.26.1 launch "unmeasured" (VibeLaunch.Verdict matches exactly one
 * entry) — so an entry for a platform the product does not read yet waits in `staged` until the C# reads `platform` (E2).
 * Returns a new manifest; `prior` is not touched.
 */
export function withEntry(prior, entry, { stage = false } = {}) {
  return stage
    ? { ...prior, versions: [...(prior.versions ?? [])], staged: replaced(prior.staged, entry) }
    : { ...prior, versions: replaced(prior.versions, entry), ...(prior.staged ? { staged: [...prior.staged] } : {}) };
}

/** `prior` with every staged entry of `platform` moved into `versions` (its slot replaced) — the step E2 takes once the C# reads the field. */
export function unstaged(prior, platform) {
  const moving = (prior.staged ?? []).filter((entry) => platformOf(entry) === platform);
  const versions = moving.reduce((list, entry) => replaced(list, entry), [...(prior.versions ?? [])]);
  return { ...prior, versions, staged: (prior.staged ?? []).filter((entry) => platformOf(entry) !== platform) };
}

const sha256 = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');

/**
 * Every file and folder of a home, by `lstat`: a folder as `a/b/` with no hash, a LINK named and flagged and never read through
 * (on Linux the home holds the `.env` sign-in link to the person's key file, DL2 — hashing it would read the key), a file with
 * its SHA-256 and size.
 */
export function inventoryOf(home) {
  return files(home, { folders: true }).map((path) => {
    if (path.endsWith('/')) return { path, sha256: '', bytes: 0 };
    const full = join(home, path);
    if (lstatSync(full).isSymbolicLink()) return { path, link: true, sha256: '', bytes: 0 };
    return { path, sha256: sha256(full), bytes: readFileSync(full).length };
  });
}
