import * as path from 'node:path';

/**
 * Where a conversation's pictures live — one directory per conversation, and until 2026-09-17 that
 * was false for every conversation at once.
 *
 * <p><b>The defect.</b> The caller was `pictureDir(entry.id.toString())`. `ChatEntry.id` is typed
 * `object` — it is the identity a `WeakMap` is keyed on, deliberately opaque — so `toString()`
 * returns `"[object Object]"` for every conversation there has ever been. Sanitised, that is
 * `objectObject`: <b>one directory, shared by all of them</b>. The file inside is named from the
 * image type and the turn number, so two conversations attaching a picture on the same turn wrote
 * to the same path and one silently replaced the other's.</p>
 *
 * <p>SonarCloud reported it as <i>"stringifies as `[object Object]` if anything ever puts it in a
 * template"</i>, and the plan recorded it as a latent risk — <i>"it does not today; the defect is
 * that nothing stops it"</i>. It did today. Reading the type of `ChatEntry.id` is what turned a
 * cosmetic finding into a real one.</p>
 *
 * <p><b>So the parameter is the conversation, not a string.</b> A `string` id invites exactly the
 * call that caused this; asking for the thing that HAS an identity means the wrong value does not
 * compile. `saveId` is the conversation's own uuid — the name its record is written under — so the
 * directory and the record agree about which conversation they belong to.</p>
 *
 * <p><b>A note the old header got wrong, kept here corrected.</b> It said one directory per
 * conversation "is what makes forgetting them possible: the tab closing removes it whole, the way
 * `chatOrphans.ts` ends the processes". Nothing removes it. Measured 2026-09-17: this is persistent
 * data under the extension's own folder, and no sweep anywhere reaches that tree. The retention is
 * open work in `todo/PLAN_the_tail_of_the_command_split.md`; what is true today is that a directory
 * per conversation at least makes such a sweep POSSIBLE, which one shared directory did not.</p>
 */
export function pictureDir(root: string, conversation: { readonly saveId: string }): string {
  // Sanitised because it becomes a path segment, and refused rather than guessed when nothing
  // survives: a conversation whose id sanitises to nothing would otherwise land every window's
  // pictures in the same place again, which is the defect this function exists to have stopped.
  const named = conversation.saveId.replace(/[^\w-]/gu, '');
  if (named.length === 0) {
    throw new Error('a conversation with no usable id cannot be given a picture directory');
  }

  return path.join(root, 'pictures', named);
}
