import { ReviewPair } from './bugzReviewPage';
import { identityOf, MarkReader, ProjectIdentity, UNKNOWN_PROJECT } from './projectIdentity';
import { openedFrom, Tab } from './tabStrip';

/**
 * Which pairs are on screen — by project, then by language.
 *
 * <p>Two hundred pairs from several products in one table is a list nobody reads to the end. These
 * are the two cuts the operator asked for, in the order they narrow: a project, and then a language
 * within it.</p>
 *
 * <p><b>The blank table cannot happen, and that is a property of the shape rather than a guard.</b>
 * Two plan reviewers reported the same defect independently — C# selected in project A, switch to
 * project B which is only TypeScript, and the filter matches nothing. Here the language tabs are
 * built from the pairs of the SELECTED project, so a language absent from it is not a tab at all,
 * and {@link openedFrom} drops a held choice that is not among the tabs. Every tab therefore has at
 * least one pair behind it by construction.</p>
 *
 * <p><b>Both filters open on everything.</b> The page shows every pair today, and an opening screen
 * that had already hidden rows would be a worse page than the one being improved.</p>
 *
 * <p><b>A strip is drawn only when it offers a choice.</b> With one project, `All projects · 9` next
 * to `dew_flow_connect_other_ais · 9` is two tabs doing one thing. The live corpus is one project
 * and two languages today (9 pairs, measured 2026-09-18), so the single-value case is the ordinary
 * one here, not an edge.</p>
 */

/**
 * The tab that filters nothing.
 *
 * <p>A sentinel rather than the empty string, because a pair's language CAN be empty — `pairOf`
 * fills an absent field with `''` when the server is older than story 2.1 — and an empty language
 * bucket sharing a key with "everything" would filter the table to nothing the moment somebody
 * pressed it.</p>
 */
export const ALL = '*all*';

/** What the person last chose, held by the panel across redraws. */
export interface HeldTabs {
  readonly project: string;
  readonly language: string;
}

/** The two strips, and the pairs they leave on screen. */
export interface ReviewTabs {
  /** Project tabs, biggest first, or none when there is only one project. */
  readonly projects: readonly Tab[];

  /** Language tabs for the chosen project, biggest first, or none when there is only one. */
  readonly languages: readonly Tab[];

  /** The pairs to draw, in the order the server sent them. */
  readonly shown: readonly ReviewPair[];

  /** The project actually open — the held one, or {@link ALL} when it no longer exists. */
  readonly project: string;

  /** The language actually open, on the same rule. */
  readonly language: string;
}

/** A bucket: the pairs that share one key, with what to call it. */
interface Bucket {
  readonly key: string;
  readonly label: string;
  readonly title: string;
  readonly pairs: readonly ReviewPair[];
}

/**
 * The pairs of each key, in the order they arrived.
 *
 * <p><b>Nothing the caller owns is mutated, which a code reviewer asked for and the repository's
 * own practice agrees with.</b> The map and every array in it are created and finished HERE and
 * handed back; the earlier shape took the accumulator as a parameter, which made a function whose
 * job was to change somebody else's collection. `remember` in `bugzReviewPanel` is the precedent:
 * it copies the set it was given and then fills the copy — what the rule forbids is mutating what
 * you were handed, not writing into what you just made. Rebuilding an immutable map per pair would
 * be quadratic on a corpus this is meant to scale to.</p>
 */
function groupedBy(pairs: readonly ReviewPair[], keyOf: (pair: ReviewPair) => string): ReadonlyMap<string, readonly ReviewPair[]> {
  const grouped = new Map<string, ReviewPair[]>();

  for (const pair of pairs) {
    const key = keyOf(pair);
    const held = grouped.get(key);
    grouped.set(key, held === undefined ? [pair] : [...held, pair]);
  }

  return grouped;
}

/**
 * Buckets in the order they will be read: most pairs first.
 *
 * <p>Ten projects is a strip a person scans left to right, so the one they are working in should be
 * where they look. Ties keep the order the server sent, which `Array.prototype.sort` guarantees.</p>
 */
function ordered(buckets: readonly Bucket[]): readonly Bucket[] {
  return [...buckets].sort((a, b) => b.pairs.length - a.pairs.length);
}

/** What a project tab's tooltip says — the path, or why there is not one to show. */
function whereItIs(key: string, full: string, reachable: boolean): string {
  if (key === UNKNOWN_PROJECT) return 'no path was recorded for these rounds';

  return reachable ? full : `${full} — not on disk any more`;
}

function projectBuckets(pairs: readonly ReviewPair[], read: MarkReader): readonly Bucket[] {
  const names = new Map<string, ProjectIdentity>();
  const grouped = groupedBy(pairs, (pair) => {
    const one = identityOf(pair.repoPath, read);
    names.set(one.key, one);

    return one.key;
  });

  return ordered([...grouped.entries()].map(([key, inIt]) => {
    const one = names.get(key) as ProjectIdentity;

    return { key, label: one.label, title: whereItIs(key, one.full, one.reachable), pairs: inIt };
  }));
}

function languageBuckets(pairs: readonly ReviewPair[]): readonly Bucket[] {
  const grouped = groupedBy(pairs, (pair) => pair.language);

  return ordered([...grouped.entries()].map(([key, inIt]) => ({
    key,
    label: key === '' ? 'Unknown language' : key,
    title: '',
    pairs: inIt,
  })));
}

/**
 * A strip, or nothing at all.
 *
 * <p>The `All` tab counts every pair the strip covers; each other tab counts its own. The count is
 * on the label because the first question about a strip of ten projects is which one the work is
 * in, and that is what the number answers.</p>
 */
function strip(buckets: readonly Bucket[], everything: string, total: number): readonly Tab[] {
  if (buckets.length < 2) return [];

  const each = buckets.map((bucket, at) => ({
    key: bucket.key,
    label: `${bucket.label} · ${bucket.pairs.length}`,
    slug: String(at),
    title: bucket.title,
  }));

  return [{ key: ALL, label: `${everything} · ${total}`, slug: 'all' }, ...each];
}

function inBucket(buckets: readonly Bucket[], key: string, fallback: readonly ReviewPair[]): readonly ReviewPair[] {
  return buckets.find((bucket) => bucket.key === key)?.pairs ?? fallback;
}

/**
 * The tab that is open, answering {@link ALL} when there is no strip.
 *
 * <p>{@link openedFrom} answers an empty string for no tabs, which is right for a strip and wrong
 * for a filter: the empty string is a real language key here. A strip that is not drawn filters
 * nothing.</p>
 */
function openedOr(tabs: readonly Tab[], held: string): string {
  return tabs.length === 0 ? ALL : openedFrom(tabs, held);
}

/**
 * The two strips and the pairs they leave, from the pairs and what was last chosen.
 *
 * <p>`read` is the filesystem, injected — a table in a test, and in the panel a reader wrapped in
 * `askedOnce` whose lifetime is the pairs themselves. **It is the CALLER's job to memoise it**, and
 * that is not an implementation detail: memoising in here made every filter press re-probe all 91
 * paths of the live corpus synchronously on the extension host, which two code reviewers caught.</p>
 */
export function reviewTabs(
  pairs: readonly ReviewPair[],
  held: HeldTabs,
  read: MarkReader,
): ReviewTabs {
  const byProject = projectBuckets(pairs, read);
  const projects = strip(byProject, 'All projects', pairs.length);
  const project = openedOr(projects, held.project);
  const inProject = project === ALL ? pairs : inBucket(byProject, project, pairs);

  const byLanguage = languageBuckets(inProject);
  const languages = strip(byLanguage, 'All languages', inProject.length);
  const language = openedOr(languages, held.language);
  const shown = language === ALL ? inProject : inBucket(byLanguage, language, inProject);

  return { projects, languages, shown, project, language };
}
