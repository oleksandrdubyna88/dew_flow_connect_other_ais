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
 * The revision of the ARTEFACT — the composed text that actually goes on the clipboard.
 *
 * <p><b>A different thing from `SNIPPET_VERSION`, which is why the two differ by one and must not be
 * “tidied” into agreement.</b> That one is the marker inside `coai-review-gate.md` and is frozen at 5
 * by the conventions migration baseline; this one numbers the paste as a whole — gate, document,
 * caller and consultant together — and is free to move when any of them does.</p>
 *
 * <p><b>Why it exists.</b> The ⋯ menu said `(v5)` from the day the label was introduced, through three
 * changes to what the clipboard carries: the document half, the caller half, and the consultant half.
 * The label was pinned to the one number in this file that can never move, so it said the same thing
 * before and after — which is precisely the defect the label was added to fix, reported by the
 * operator twice, the second time as “if the snippet changed, the version should have gone up”.</p>
 *
 * <p><b>Why it is declared rather than derived.</b> The first attempt summed the four half markers, so
 * that no one would have to remember it. All three reviewers refused it: duplicate blocks in one
 * CLAUDE.md sum together (a person pasting without deleting the old copy would be told their v12 is
 * behind our v8), the sum collides for a paste that is newer in one half and missing another, and the
 * guard proposed for it compared the constant with the expression it was assigned from. So the number
 * is an ordinal, and what keeps it honest is a test rather than arithmetic: the SHA guard in
 * `snippetVersion.test.ts` goes red on any change to the artefact text — a new half included — naming
 * this constant, and `snippetVersionIsVisible.test.ts` goes red when the manifest title disagrees with
 * it. A command title in `package.json` is static JSON that the editor reads before any of our code
 * runs, so it cannot interpolate this; it is typed by hand and pinned by that test.</p>
 */
export const ARTEFACT_VERSION = 6;

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
 * What a workspace's pasted copy is, relative to what this build hands out.
 *
 * <p><b>`current` is the ARTEFACT's version in every state, and a diverged paste is described by
 * its HALVES rather than by a number of its own.</b> A pasted copy carries one marker per half and
 * nothing that numbers the whole, so any single number attributed to it is invented. The panel used
 * to print the gate half's frozen 5 as though it were the paste's version, which is how a
 * repository that mounts the gate rule came to be told "is v5; v5 is current".</p>
 */
export type SnippetStatus =
  | { readonly kind: 'current'; readonly current: number }
  | { readonly kind: 'older'; readonly behind: readonly string[]; readonly current: number }
  | { readonly kind: 'ahead'; readonly newer: readonly string[]; readonly current: number }
  | { readonly kind: 'unversioned'; readonly current: number }
  | { readonly kind: 'absent'; readonly current: number };

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

/**
 * The halves the artefact is made of, and the one place that knows there are four.
 *
 * <p>There used to be four marker regexes and four near-identical readers, which is four places to
 * edit when a fifth half arrives and four chances to edit three of them. The marker is built from
 * the id, so a row is all a half needs — and the reader now HAS a list of halves, which is what
 * makes `halvesIn` able to notice a half nobody taught it about.</p>
 *
 * <p>`name` is what a person reads in the panel when their copy is missing that half. It is prose,
 * not an identifier: the sentence it lands in is "missing or behind on: the document gate, the
 * consultant".</p>
 */
const HALVES = [
  { id: 'coai-snippet', name: 'the review gate', version: SNIPPET_VERSION },
  { id: 'coai-document', name: 'the document gate', version: DOCUMENT_VERSION },
  { id: 'coai-caller', name: 'the caller declaration', version: CALLER_VERSION },
  { id: 'coai-consultant', name: 'the consultant', version: CONSULTANT_VERSION },
] as const;

/** The ids this build reads, so a test can compare them with what the artefact actually carries. */
export const HALF_IDS: readonly string[] = HALVES.map((half) => half.id);

/** Any half's marker, from its id — never a second literal to keep in step. */
function markerOf(id: string): RegExp {
  return new RegExp(`<!-- ${id} v(\\d+) -->`);
}

/**
 * One half's version out of a text, or nothing when that half is not in it.
 *
 * <p>The regex is deliberately NOT global and the FIRST match wins. A person who pastes the new
 * block without deleting the old one has two of every marker in one file, and a reader that
 * accumulated them would answer a number belonging to neither copy.</p>
 */
function versionOf(text: string, id: string): number | undefined {
  const found = markerOf(id).exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/**
 * Every `coai-…` half id present in a text, in the order they appear.
 *
 * <p>Exported for one guard: the ids in what `claudeSnippet()` hands out must be exactly
 * {@link HALF_IDS}. A fifth rule file added to the paste without a row in {@link HALVES} is then a
 * red test rather than a half nothing reads — which is how the consultant half came to raise no
 * version anywhere for a day.</p>
 */
export function halvesIn(text: string): readonly string[] {
  return [...text.matchAll(/<!-- (coai-[a-z-]+) v\d+ -->/g)].map((match) => match[1]);
}

/** The first applicable paste wins, using the same reader for the panel and copy command. */
export async function readSnippetStatus(read: (name: string) => Promise<string>): Promise<SnippetStatus> {
  const texts = await Promise.all(SNIPPET_LOCATIONS.map(read));
  const at = texts.findIndex(text => text.includes(SNIPPET_MARKER));
  if (at < 0) {
    return snippetStatus(undefined);
  }

  return snippetStatus(texts[at]);
}

/** The GATE half's version out of a file the snippet was pasted into, or nothing when it has none. */
export function snippetVersionIn(text: string): number | undefined {
  return versionOf(text, 'coai-snippet');
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
  const current = ARTEFACT_VERSION;
  if (pasted === undefined || !pasted.includes(SNIPPET_MARKER)) {
    return { kind: 'absent', current };
  }
  if (snippetVersionIn(pasted) === undefined) {
    return { kind: 'unversioned', current };
  }

  // Per HALF, because that is the only comparison a pasted copy supports: it carries one marker for
  // each half it was given and nothing that numbers the paste as a whole. `newer` first — a copy
  // from a later build is told to keep what it has even when it is also missing something, because
  // pasting over it would be a downgrade nobody asked for.
  const newer = HALVES.filter((half) => (versionOf(pasted, half.id) ?? 0) > half.version);
  if (newer.length > 0) {
    return { kind: 'ahead', newer: newer.map((half) => half.name), current };
  }

  // Everything else that is not exactly this build's set of halves is OLDER: a copy pasted before a
  // half existed carries no marker for it, and the AI obeying such a copy never uses what that half
  // describes — it is the same defect for every one of them, which is why absent and behind are one
  // answer here and are named separately in the sentence.
  const behind = HALVES.filter((half) => (versionOf(pasted, half.id) ?? 0) < half.version);

  return behind.length === 0
    ? { kind: 'current', current }
    : { kind: 'older', behind: behind.map((half) => half.name), current };
}

/** A readable list — "the document gate, the caller declaration and the consultant". */
function names(halves: readonly string[]): string {
  return halves.length < 2
    ? halves.join('')
    : `${halves.slice(0, -1).join(', ')} and ${halves[halves.length - 1]}`;
}

/** One line for the panel, saying what to do about it — or nothing when there is nothing to say. */
export function snippetNote(status: SnippetStatus): string {
  switch (status.kind) {
    case 'older':
      return `The CLAUDE.md snippet in this workspace is missing or behind on: ${names(status.behind)}. `
        + `The ⋯ menu hands out v${status.current} — copy it again and replace the old block; what `
        + 'changed is what the AI reading it is told to do.';
    case 'unversioned':
      return 'The CLAUDE.md snippet in this workspace predates versioning, so it is at least one '
        + 'revision behind. Copy it again from the ⋯ menu and replace the old block.';
    case 'ahead':
      return `This workspace's snippet is newer than this build on: ${names(status.newer)}. `
        + `This extension hands out v${status.current} — somebody updated the repository from a newer `
        + 'build, so update this one rather than pasting over it.';
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
      return `${took}. This repository's copy is missing or behind on ${names(status.behind)} — `
        + 'replace the block between the markers.';
    case 'unversioned':
      return `${took}. This repository has a copy from before the version marker existed — replace it.`;
    case 'ahead':
      return `${took}, and this repository's copy is NEWER than this build on ${names(status.newer)}. `
        + 'Keep what you have.';
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

/** The document half's version out of a pasted file, or nothing when it has no such half. */
export function documentVersionIn(text: string): number | undefined {
  return versionOf(text, 'coai-document');
}

/** The caller half's version out of a pasted file, or nothing when it has no such half. */
export function callerVersionIn(text: string): number | undefined {
  return versionOf(text, 'coai-caller');
}

/** The consultant half's version out of a pasted file, or nothing when it has no such half. */
export function consultantVersionIn(text: string): number | undefined {
  return versionOf(text, 'coai-consultant');
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
