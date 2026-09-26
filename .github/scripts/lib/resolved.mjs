// lib/resolved.mjs — where a program actually is, for every script in `.github/scripts/`.
//
// Its own module since 2026-09-26: it lived inside `branch-protection.mjs`, and once the release
// scripts (`release-anchors.mjs`, `docs-only-title.mjs`) needed it too, importing a PATH resolver from
// a branch-protection script coupled two things that have nothing to do with each other. Nothing about
// it changed in the move; `branch-protection.mjs --selftest` still holds its edge cases.

import { accessSync, constants, existsSync, statSync } from 'node:fs';
import path from 'node:path';

/**
 * Where a program actually is, resolved once, rather than a bare name handed to the spawner.
 *
 * <p>Spawning `gh` by name delegates the choice of what runs to whatever `PATH` happens to say —
 * SonarCloud calls it out (S4036) and it is right that the decision should be visible. Resolving it
 * here does not make PATH trustworthy; what it buys is that the lookup is one explicit step with an
 * error somebody can act on, instead of an opaque spawn failure at the moment the tool was supposed
 * to answer a question.</p>
 *
 * <p>Done in JavaScript rather than by shelling out to `which`, because `which` would be the same
 * problem one level down.</p>
 */
export function resolved(name) {
  // ONLY what `execFileSync` can start directly, which is narrower than PATHEXT. Node refuses
  // `.cmd` and `.bat` without `shell: true` — the 2024 argument-injection fix — and `.ps1`/`.vbs`
  // are not executables at all. The first version walked PATHEXT and would have returned such a
  // file happily, SHADOWING a working `gh.exe` further along PATH and failing at the spawn with a
  // message about neither. Refusing here says the true thing. (CodeRabbit, creds_for_devs #116.)
  const suffixes = process.platform === 'win32' ? ['.exe', '.com'] : [''];

  // `filter(Boolean)` DROPS EMPTY ENTRIES, and on POSIX an empty entry — `PATH=:/usr/bin`, a
  // trailing colon, `::` — means the CURRENT DIRECTORY. That is a deliberate divergence from
  // `execvp`: the current directory when this tool runs is a REPOSITORY CHECKOUT, and it spawns `gh`
  // holding a token that can rewrite branch protection, so the legacy that is deprecated everywhere
  // else is not worth honouring here either. (CodeRabbit, creds_for_devs #117 — correct about POSIX.)
  //
  // WHAT THIS DOES NOT DO, measured rather than assumed, because the first version of this comment
  // claimed more than the code delivers: an EXPLICIT `.` on PATH still resolves out of the working
  // directory, because `path.resolve('.')` is the working directory and `.` is a legal relative
  // entry like any other. So this refuses the IMPLICIT form — a typo, a trailing colon, a PATH
  // inherited from something that built it by concatenation — and not cwd lookup in general. Both
  // halves are pinned by cases below, so the boundary is stated rather than implied.
  for (const entry of (process.env.PATH ?? '').split(path.delimiter).filter(Boolean)) {
    // A RELATIVE PATH entry is legal and common enough (`tools`, `.`), and joining onto it yields a
    // relative answer — which the caller then spawns relative to ITS working directory rather than
    // the one PATH meant. It also made the selftest's `isAbsolute` assertion fail in an environment
    // that was perfectly correct.
    const dir = path.resolve(entry);

    for (const suffix of suffixes) {
      const candidate = path.join(dir, name + suffix);
      if (!existsSync(candidate) || !statSync(candidate).isFile()) {
        continue;
      }
      // On POSIX a readable file without the execute bit is not a program, and returning it only
      // moves the failure to the spawn. Windows has no equivalent bit; the extension list above is
      // what stands in for it there.
      if (process.platform !== 'win32') {
        try {
          accessSync(candidate, constants.X_OK);
        } catch {
          continue;
        }
      }
      return candidate;
    }
  }
  throw new Error(`${name} is not on PATH, so nothing that needs it can run`);
}
