import { GATE_RULE } from './generated/gateRule';

/**
 * The instruction text a person pastes into a target repository's CLAUDE.md, teaching that
 * repo's main AI when to call the `coai` tools.
 *
 * <p><b>It carries a version, and the panel reads it back out of whatever it was pasted into.</b>
 * The copy in one repository here was two revisions behind the button's text and predated the SCOPE
 * rule, so the AI obeying it would call `review_code` with a commit subject and meet a refusal
 * nothing in its instructions explained. Nobody was careless: that is what happens to text somebody
 * pastes. The source moves, the copy does not, and the copy is the one being obeyed.</p>
 *
 * <p><b>A number, not a hash — and both.</b> A hash cannot be forgotten but can only say
 * "different", while the useful sentence is "OLDER than the current one": a stale paste and a
 * locally edited one want opposite advice, and only an ordered number can tell them apart. So the
 * number is ordered and `snippetVersion.test.ts` pins it to the text's hash, which makes bumping it
 * unforgettable without making it meaningless.</p>
 *
 * <p>Offered as a paste, never written into someone's CLAUDE.md: adoption should be explicit and
 * reviewable, and that file is often hand-curated. Pure, so its claims are tests.</p>
 *
 * <p><b>It names no repository.</b> It used to interpolate the open workspace's folder name, which
 * was wrong twice over: the snippet is pasted into whichever repo you are adopting it for — not
 * necessarily the one that was open when you copied it — and the AI reading it is already working
 * in a checkout it can name for itself. "This checkout" is both shorter and always true.</p>
 */

/**
 * The revision of the pasted text. Raise it whenever the snippet changes — the hash guard in
 * `snippetVersion.test.ts` will not let you forget, and will tell you the new hash.
 *
 * <p>Numbering starts at 2, and that is not an off-by-one: v1 names the text that was pasted around
 * before this marker existed, which is a real generation sitting in real repositories. Copies of it
 * carry no marker and report `unversioned`; if anybody ever hand-marks one, v1 is the honest number
 * for it.</p>
 */
export const SNIPPET_VERSION = 5;

/** The snippet body's hash, so the version above cannot silently stop meaning anything. */
export const SNIPPET_BODY_SHA = '4e951347aa178972';

/**
 * Where a repository is allowed to keep the block, in the order a reader should believe them.
 *
 * <p><b>The instruction files come first, and that ordering is the whole point.</b> A block pasted
 * into `CLAUDE.md` is what the AI in that repository actually reads, so if it is three revisions
 * old that is the sentence the panel must say — reporting the mounted rule's version instead would
 * be a green light over the stale text still being obeyed.</p>
 *
 * <p>Root/project and local policy precede the shared rule, so an older applicable paste
 * cannot be hidden by a current mount. Neutral and legacy shared locations are supported.
 * Named paths keep repaint work bounded; copies elsewhere remain outside this lookup.</p>
 */
export const SNIPPET_LOCATIONS: readonly string[] = [
  'CLAUDE.md',
  'AGENTS.md',
  'GEMINI.md',
  '.github/copilot-instructions.md',
  '.agents/PROJECT.md',
  '.agents/rules/common/coai-review-gate.md',
  '.claude/rules/common/coai-review-gate.md',
  '.agents/conventions/common/coai-review-gate.md',
  '.claude/rules/shared/common/coai-review-gate.md',
];

/** The sentence a copy is recognised by, wherever it sits. */
export const SNIPPET_MARKER = 'Multi-model review gate (ConnectOtherAIs)';

/** What a workspace's pasted copy is, relative to what this build hands out. */
export type SnippetStatus =
  | { readonly kind: 'current'; readonly current: number }
  | { readonly kind: 'older'; readonly found: number; readonly current: number }
  | { readonly kind: 'ahead'; readonly found: number; readonly current: number }
  | { readonly kind: 'unversioned'; readonly current: number }
  | { readonly kind: 'absent'; readonly current: number };

const MARKER = /<!-- coai-snippet v(\d+) -->/;

/** The first applicable paste wins, using the same reader for the panel and copy command. */
export async function readSnippetStatus(read: (name: string) => Promise<string>): Promise<SnippetStatus> {
  for (const name of SNIPPET_LOCATIONS) {
    const text = await read(name);
    if (text.includes(SNIPPET_MARKER)) {
      return snippetStatus(text);
    }
  }

  return snippetStatus(undefined);
}

/** The version out of a file the snippet was pasted into, or nothing when it carries no marker. */
export function snippetVersionIn(text: string): number | undefined {
  const found = MARKER.exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/**
 * How a workspace's instruction files compare with the snippet this build would hand out.
 *
 * <p>Five answers rather than a boolean, because they want different sentences. `absent` is not a
 * problem to report — a repository that has deliberately not adopted the gate is entitled to — while
 * `unversioned` means "pasted before this existed", which is true of every copy made until today
 * and is not the same as version zero. `ahead` is a real case too: an extension older than the
 * repository, on a machine that has not updated.</p>
 */
export function snippetStatus(pasted: string | undefined): SnippetStatus {
  const current = SNIPPET_VERSION;
  if (pasted === undefined || !pasted.includes(SNIPPET_MARKER)) {
    return { kind: 'absent', current };
  }
  const found = snippetVersionIn(pasted);
  if (found === undefined) {
    return { kind: 'unversioned', current };
  }
  if (found === current) {
    return { kind: 'current', current };
  }

  return found < current ? { kind: 'older', found, current } : { kind: 'ahead', found, current };
}

/** One line for the panel, saying what to do about it — or nothing when there is nothing to say. */
export function snippetNote(status: SnippetStatus): string {
  switch (status.kind) {
    case 'older':
      return `The CLAUDE.md snippet in this workspace is v${status.found}; v${status.current} is current. `
        + 'Copy it again from the ⋯ menu and replace the old block — what changed is what the AI '
        + 'reading it is told to do.';
    case 'unversioned':
      return 'The CLAUDE.md snippet in this workspace predates versioning, so it is at least one '
        + 'revision behind. Copy it again from the ⋯ menu and replace the old block.';
    case 'ahead':
      return `This workspace has snippet v${status.found} and this extension hands out v${status.current}. `
        + 'Somebody updated the repository from a newer build — update this one rather than pasting over it.';
    case 'current':
      return '';
    case 'absent':
      return '';
    default: {
      const unhandled: never = status;

      return unhandled;
    }
  }
}

/**
 * What to say the moment somebody copies it: the version they took, and what this repository has.
 *
 * <p>The menu item names the version too, but a menu is read BEFORE the click and this is read
 * after it — and "v5" means nothing to a person whose repository is on v4 unless somebody says so.
 * Asked by the operator, about a version only the source code knew: "how does a person find out?"</p>
 */
export function copiedMessage(status: SnippetStatus): string {
  const took = `The CLAUDE.md snippet (v${status.current}) is on your clipboard`;
  switch (status.kind) {
    case 'older':
      return `${took}. This repository has v${status.found} — replace the block between the markers.`;
    case 'unversioned':
      return `${took}. This repository has a copy from before the version marker existed — replace it.`;
    case 'ahead':
      return `${took}, and this repository already has v${status.found}, which is NEWER than this build. Keep what you have.`;
    case 'current':
      return `${took}, and this repository is already on it — nothing to replace.`;
    case 'absent':
      return `${took} — paste it into the CLAUDE.md of the repository you want reviewed.`;
    default: {
      const unhandled: never = status;

      return unhandled;
    }
  }
}

export function claudeSnippet(): string {
  return GATE_RULE;
}
