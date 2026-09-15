/**
 * WHICH directories are watched for questions, and what is wrong with the ones that are not.
 *
 * <p>Pure, and separate from `escalationWatcher.ts` for the reason `chatMessages.ts` is separate
 * from `chatPanel.ts`: the watcher is `vscode` wiring that no unit test can reach, and every decision
 * worth getting right — what a path means, which two spellings are one place, what to say about a
 * path that cannot work — would otherwise live where nothing can check it.</p>
 *
 * <p><b>Why there is more than one directory at all.</b> A `coai-mcp` writes its questions into its
 * own data directory, and a Claude Code session inside WSL has a different one from the Windows
 * window watching for them — two defaults on two filesystems, confirmed live on 2026-09-15 as a
 * 25.8 MB and an 8.3 MB database written the same day. The question was always captured; nothing was
 * watching the store it landed in. The STORES stay separate — that ruling belongs to
 * `PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md` and is not this module's to revisit
 * — and only the question surface is shared: JSON files written once and read once, with no database
 * anywhere near the boundary.</p>
 */

/** One directory the watcher was asked to watch, and what became of the asking. */
export interface WatchedDir {
  /** Exactly what the person typed, so the panel can name the thing they need to correct. */
  readonly asked: string;
  /** The path to use, empty when `refusal` says why there is none. */
  readonly path: string;
  /** Empty when the directory is usable. A sentence otherwise. */
  readonly refusal: string;
}

/**
 * A path inside WSL, named from a Windows window, cannot be reached by writing it the way WSL does.
 *
 * <p>This is the FIRST thing the reporter of the bug would have typed: their store is
 * `/home/<user>/.local/share/coai-mcp`, and that is the string the panel shows them on the WSL side.
 * Resolved by a Windows extension host it becomes `C:\home\…`, which does not exist — and a
 * directory that does not exist contributes nothing and says nothing, which reproduces the exact
 * symptom this feature exists to end, with no feedback at all.</p>
 *
 * <p><b>No distribution is guessed.</b> Turning `/home/x` into `\\wsl.localhost\<distro>\home\x`
 * needs a distro name, and inventing one means watching a path nobody chose and reporting it as
 * healthy. The refusal names the shape that works and leaves the choice where it belongs.</p>
 */
export const POSIX_ON_WINDOWS =
  'this looks like a path inside WSL. From a Windows window name it as '
  + '\\\\wsl.localhost\\<distro>\\home\\<user>\\.local\\share\\coai-mcp';

/**
 * Two spellings of one directory, as one key.
 *
 * <p>`C:\Data`, `c:\data\` and `C:\Data\` are one place, and watching it three times would offer the
 * same question three times. Case is folded only where the platform folds it — a Linux window's
 * `/home/A` and `/home/a` are genuinely two directories, and treating them as one would silently
 * drop a watch somebody asked for.</p>
 */
export function dirKey(path: string, platform: NodeJS.Platform): string {
  const trimmed = path.trim().replace(/[\\/]+$/, '');
  const slashes = trimmed.replace(/\\/g, '/');

  return platform === 'win32' ? slashes.toLowerCase() : slashes;
}

/**
 * The directories to watch, in order, each with the reason it cannot be watched when it cannot.
 *
 * <p>The window's OWN directory is always first and is never refused: it is where this installation's
 * own questions land, and a setting that could silence it would be a setting that can break the
 * feature it extends. Everything after it is what somebody named.</p>
 *
 * <p>Refusals are KEPT rather than dropped, because the panel renders them. A named directory that
 * quietly vanishes from the list is the same silence this whole change is about.</p>
 */
export function watchedDirs(own: string, extras: readonly string[], platform: NodeJS.Platform): readonly WatchedDir[] {
  const out: WatchedDir[] = [{ asked: own, path: own, refusal: '' }];
  const seen = new Set<string>([dirKey(own, platform)]);

  for (const raw of extras) {
    const asked = raw.trim();
    if (asked.length === 0) {
      continue;
    }
    // REFUSED, not normalised away: a path that cannot work on this host is a thing to correct, and
    // the person only learns it from the panel if it survives to be rendered there.
    // `//server/share` is a UNC path written with forward slashes and is perfectly reachable from
    // Windows; only a SINGLE leading slash is the POSIX shape that resolves to C:\… here.
    if (platform === 'win32' && asked.startsWith('/') && !asked.startsWith('//')) {
      out.push({ asked, path: '', refusal: POSIX_ON_WINDOWS });
      continue;
    }
    const key = dirKey(asked, platform);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    out.push({ asked, path: asked, refusal: '' });
  }

  return out;
}

/** The directories that can actually be read — what the watcher subscribes to. */
export function usableDirs(dirs: readonly WatchedDir[]): readonly string[] {
  return dirs.filter((dir) => dir.refusal.length === 0).map((dir) => dir.path);
}

/** Where an answer is written, and the temporary file it is renamed from. */
export interface AnswerPaths {
  readonly dir: string;
  readonly target: string;
  readonly temp: string;
}

/**
 * Where the answer to one question goes — BESIDE the question, never in this window's own directory.
 *
 * <p><b>Two rules, both of which have a failure behind them.</b></p>
 *
 * <p>The directory is the one the question was READ from. A question can come from another
 * installation, and the server that asked polls the directory it wrote in and nowhere else — so an
 * answer written into the answering window's own store leaves that round blocked for ever, having
 * been answered. A question with no `from` is one of this window's own, which is every question there
 * was before the setting existed.</p>
 *
 * <p>The temporary file is in the SAME directory as the target, and that is not tidiness. The write
 * is atomic — temp, then rename — and a rename across two filesystems throws `EXDEV: cross-device
 * link not permitted`. A temp written into this window's directory and renamed into a WSL or a NAS
 * one fails every single time, and the answer never lands. Pure and tested because the alternative is
 * a comment, and a comment is what the plan round found this guarantee resting on. (gemini, the plan
 * round, Blocking.)</p>
 *
 * <p>Callers pass NATIVE filesystem paths — `Uri.fsPath` on both sides since the first code round —
 * and the parts are joined with a forward slash, which Windows accepts and POSIX requires. A trailing
 * separator of either kind is trimmed first, or a root written `C:\coai\` composes a mixed-separator
 * string.</p>
 */
export function answerPaths(id: string, own: string, from?: string): AnswerPaths | undefined {
  // THE ID IS NOT OURS. It is read out of a JSON file written by another process, and it is about to
  // become part of a path this extension writes to — so `../../.ssh/authorized_keys` would be a
  // question file asking to be answered somewhere else entirely. Ids this server mints are hex; the
  // grammar below is that, widened only as far as a name can safely go. Refused rather than
  // sanitised: a question whose id is not a name is not a question this window can answer, and
  // quietly rewriting somebody's id would answer a different question. (codex, the code round.)
  if (!/^[A-Za-z0-9._-]+$/.test(id) || id === '.' || id === '..') {
    return undefined;
  }
  const root = from === undefined || from.trim().length === 0 ? own : from;
  // BOTH separators, the way `dirKey` strips both. Callers pass native filesystem paths, so a root
  // written `C:\coai\` trimmed of forward slashes only would compose `C:\coai\/escalations` — the
  // same mixed-separator string the first code round removed elsewhere. A forward slash joins them
  // because Windows accepts one and every POSIX path uses it. (gemini, the second code round.)
  const dir = `${root.trim().replace(/[\\/]+$/, '')}/escalations`;

  return { dir, target: `${dir}/${id}.answer.json`, temp: `${dir}/${id}.answer.json.tmp` };
}
