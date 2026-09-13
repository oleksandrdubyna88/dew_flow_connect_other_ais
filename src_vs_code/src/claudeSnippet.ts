import { CALLER_RULE, DOCUMENT_RULE, GATE_RULE } from './generated/gateRule';
import { CONSULTANT_RULE } from './generated/consultantRule';

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
export const SNIPPET_BODY_SHA = '45dc60e8bbfd0d31';

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
  '.agents/rules/common/review-gate.md',
  '.agents/rules/common/coai-review-gate.md',
  '.claude/rules/common/review-gate.md',
  '.claude/rules/common/coai-review-gate.md',
  '.agents/conventions/common/coai-review-gate.md',
  '.claude/rules/shared/common/coai-review-gate.md',
];

/** The sentence a copy is recognised by, wherever it sits. */
export const SNIPPET_MARKER = 'Multi-model review gate (ConnectOtherAIs)';

/**
 * The locations that are a MOUNT of the shared rule rather than somebody's paste.
 *
 * <p>It mattered from the moment the snippet gained a second half. The gate text in a mount is
 * current by definition — the submodule pin says so — but it is now only PART of what the button
 * hands out, so a repository that mounts and never pasted is missing the consultant block. Telling
 * it to "replace the old block" would be wrong twice: there is no block to replace, and pasting the
 * whole snippet would duplicate a rule the mount already provides. (gemini, story 5's plan round.)</p>
 */
const MOUNTED_LOCATIONS: readonly string[] = [
  '.agents/conventions/common/coai-review-gate.md',
  '.claude/rules/shared/common/coai-review-gate.md',
];

/** What a workspace's pasted copy is, relative to what this build hands out. */
export type SnippetStatus =
  | { readonly kind: 'current'; readonly current: number }
  | { readonly kind: 'older'; readonly found: number; readonly current: number }
  | { readonly kind: 'ahead'; readonly found: number; readonly current: number }
  | { readonly kind: 'unversioned'; readonly current: number }
  | { readonly kind: 'absent'; readonly current: number }
  /**
   * The shared rule is MOUNTED here and nothing was pasted.
   *
   * <p>Its own answer rather than `older`, because the advice is different: the gate half is current
   * by the submodule pin, and what is missing is the consultant half — which lives in this product,
   * not in the shared store. Pasting the whole snippet over a mount would duplicate the gate rule.</p>
   */
  | { readonly kind: 'mounted'; readonly found: number; readonly current: number };

const MARKER = /<!-- coai-snippet v(\d+) -->/;

/**
 * The DOCUMENT half's own marker.
 *
 * <p><b>A second version rather than a bump of the first, and not by choice.</b>
 * `SNIPPET_VERSION` is the number written inside `coai-review-gate.md`, and that file is one of
 * the 24 rule bodies the conventions repository hashes against its migration baseline — editing
 * it turns that suite red, which is why the document flow is a second rule file at all. So the
 * gate half is still v5 and always will be until the inventory retires; what moves is this.</p>
 *
 * <p>It is not cosmetic. A copy pasted before the document gate existed carries
 * `coai-snippet v5` and no document marker, and the AI obeying it will never call
 * `review_document` — the same defect the first version marker was introduced for, one rule
 * file over.</p>
 */
export const DOCUMENT_VERSION = 1;

const DOCUMENT_MARKER = /<!-- coai-document v(\d+) -->/;

/**
 * The CALLER half's version — say which model you are when you open the gate.
 *
 * <p><b>A third number rather than a bump of either other one, and for the same reason there is a
 * second.</b> The sentence asking an AI to declare its own model belongs in step 1 of
 * `coai-review-gate.md`, and that file is frozen against the conventions migration baseline. So it
 * is a third rule file with a marker of its own, and a marker of its own is what lets a paste made
 * before it be recognised — the AI obeying such a paste never sends `callerModel`, and every round
 * it drives is recorded as stating no model while the gate is perfectly capable of recording one.
 * That is the same defect the first version marker was introduced for, two rule files over.</p>
 */
export const CALLER_VERSION = 1;

const CALLER_MARKER = /<!-- coai-caller v(\d+) -->/;

/**
 * The CONSULTANT half's version — when to ask another vendor, and what to do with the answer.
 *
 * <p><b>A fourth number, and the first one whose rule file is not in the conventions repository.</b>
 * The other three halves are shared: every repository in this family is reviewed by the gate, asked
 * to declare its model, and may have a document reviewed. This one says when to call ONE tool of ONE
 * server, which is this product's own material — asked where it should live, the operator answered
 * that conventions holds only shared rules and specific material belongs to the project that owns
 * it. So the source is <c>src_vs_code/src/consultantRule.md</c> here, emitted beside the generated
 * gate rules, and everything else about it follows the pattern the two halves above established.</p>
 *
 * <p>It is not cosmetic, for the same reason theirs are not: a copy pasted before the consultant
 * existed carries no consultant marker, and the AI obeying it never calls `consult` — it goes on
 * trying the same fix a third time, which is the whole thing this feature exists to interrupt.</p>
 */
export const CONSULTANT_VERSION = 1;

const CONSULTANT_MARKER = /<!-- coai-consultant v(\d+) -->/;

/** The first applicable paste wins, using the same reader for the panel and copy command. */
export async function readSnippetStatus(read: (name: string) => Promise<string>): Promise<SnippetStatus> {
  const texts = await Promise.all(SNIPPET_LOCATIONS.map(read));
  const at = texts.findIndex(text => text.includes(SNIPPET_MARKER));
  if (at < 0) {
    return snippetStatus(undefined);
  }

  // WHERE it was found decides what to say about it: the instruction files come first precisely so
  // a stale paste outranks a current mount, and a mount that answers means nothing was pasted.
  return snippetStatus(texts[at], MOUNTED_LOCATIONS.includes(SNIPPET_LOCATIONS[at]!));
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
export function snippetStatus(pasted: string | undefined, fromMount = false): SnippetStatus {
  const current = SNIPPET_VERSION;
  if (pasted === undefined || !pasted.includes(SNIPPET_MARKER)) {
    return { kind: 'absent', current };
  }
  const found = snippetVersionIn(pasted);
  if (found === undefined) {
    return { kind: 'unversioned', current };
  }
  if (found === current) {
    // The gate half is current and one of the OTHER halves may not be there at all — which is what
    // every copy pasted before plan 4 looks like, and every copy pasted before #174. Reported as
    // OLDER, because that is the sentence that gets it replaced — and `found` stays the number
    // actually in the file, so nobody is told they have a version that was never handed out.
    return documentVersionIn(pasted) === DOCUMENT_VERSION
           && callerVersionIn(pasted) === CALLER_VERSION
           && consultantVersionIn(pasted) === CONSULTANT_VERSION
      ? { kind: 'current', current }
      : { kind: 'older', found, current };
  }
  // A MOUNT that is behind is not a stale paste: the gate half is current by its pin, and what it
  // lacks is the half this product owns. Ahead is still ahead — a newer mount than this build knows
  // about is a machine to update, whoever put the text there.
  if (fromMount && found < current) {
    return { kind: 'mounted', found, current };
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
    case 'mounted':
      return `This workspace MOUNTS the shared gate rule (v${status.found}); the snippet this build hands `
        + `out is v${status.current} and carries a second half — when to consult another vendor — which `
        + 'lives in ConnectOtherAIs rather than in the shared rules. Copy it from the ⋯ menu and paste '
        + 'the consultant block; the gate half is already here and should not be duplicated.';
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
    case 'mounted':
      return `${took}. This repository MOUNTS the gate rule (v${status.found}), so paste the consultant `
        + 'block only — the gate half is already here through the submodule.';
    default: {
      const unhandled: never = status;

      return unhandled;
    }
  }
}

/** The document half's version out of a pasted file, or nothing when it has no such half. */
export function documentVersionIn(text: string): number | undefined {
  const found = DOCUMENT_MARKER.exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/** The caller half's version out of a pasted file, or nothing when it has no such half. */
export function callerVersionIn(text: string): number | undefined {
  const found = CALLER_MARKER.exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/** The consultant half's version out of a pasted file, or nothing when it has no such half. */
export function consultantVersionIn(text: string): number | undefined {
  const found = CONSULTANT_MARKER.exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/**
 * All four rules, in the order an AI should read them: the gate, the document gate, the caller, the
 * consultant.
 *
 * <p>Four files because the first one is frozen — see `DOCUMENT_VERSION` and `CALLER_VERSION`. A
 * person pasting this gets one block either way, and the AI reading it gets every rule, which is
 * the only thing that matters about the arrangement.</p>
 *
 * <p>The consultant is last on purpose: the three before it are about work being REVIEWED, and this
 * one is about the assistant asking for help mid-task. It is also the only one whose source is this
 * repository rather than the shared conventions — see `CONSULTANT_VERSION`.</p>
 */
export function claudeSnippet(): string {
  return `${GATE_RULE}\n${DOCUMENT_RULE}\n${CALLER_RULE}\n${CONSULTANT_RULE}`;
}
