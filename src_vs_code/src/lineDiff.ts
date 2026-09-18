/**
 * What differs between a pair's two skeletons, line by line — the way git shows it.
 *
 * <p>Pure, and free of `node:` and `vscode`, because the review page calls it.</p>
 *
 * <h3>The measurement that decided the shape of this</h3>
 *
 * <p><b>Anonymisation invents differences the original code never had.</b> The normaliser numbers
 * placeholders in order of declaration (`Placeholders.cs`: `$"{kind}_{next}"`, and the kinds are
 * exactly `var`, `method`, `type`), so a fix that adds ONE line near the top of a method shifts the
 * numbering of everything below it. On a representative pair:</p>
 *
 * <pre>
 *   lines the fix actually added            1
 *   a plain line diff marks changed         7
 *   a diff over masked names marks changed  1
 * </pre>
 *
 * <p>Seven of seven lines, for one added statement. On the page where somebody decides what leaves
 * their machine, that is the worst kind of noise: it hides the real change inside a wall of
 * artefacts. So the COMPARISON runs over masked text and the DISPLAY keeps the real text.</p>
 *
 * <p><b>The honest cost, which is not hidden.</b> A line whose only difference is a placeholder
 * index is reported as unchanged — and it is impossible in principle to tell that apart from a line
 * where the author genuinely switched to a different variable, because the skeleton no longer
 * carries the information that would distinguish them. The mask chooses the lesser error and this
 * paragraph is the place that says so out loud. (Measured on a constructed pair: no real corpus was
 * present on the machine, and `research/module_tests.md` records that.)</p>
 */

/** What happened to one line. `changed` is a removal and an addition the diff paired up. */
export type LineMark = 'same' | 'added' | 'removed' | 'changed';

/** A mark for every line of each side, in the order the lines appear. */
export interface PairDiff {
  readonly before: readonly LineMark[];
  readonly after: readonly LineMark[];
}

/**
 * The placeholder kinds the normaliser emits — and the ONLY words this masks.
 *
 * <p>The first version matched any `([A-Za-z]+)_\d+`, reasoning that a fourth kind would then keep
 * working. Two reviewers on two providers refused it, and they were right: the corpus keeps string
 * literals and comments verbatim, so a real `utf8_2`, `base64_128` or `token_1` would be masked
 * too — and a line where one of those genuinely changed would be reported as UNCHANGED. That is the
 * opposite of what this module is for, and it fails silently.</p>
 *
 * <p>So the list is narrow, and `lineDiff.test.ts` reads `Placeholders.cs` in `src_mcp` and fails if
 * the normaliser ever returns a kind that is not here. A fourth kind then arrives as a red test
 * naming it, rather than as a wall of false differences nobody can explain.</p>
 */
export const PLACEHOLDER_KINDS = ['var', 'method', 'type'] as const;

const INDEXED = new RegExp(`\\b(${PLACEHOLDER_KINDS.join('|')})_\\d+\\b`, 'gu');

/** A line with its placeholder indices removed, so renumbering is not read as change. */
const masked = (line: string): string => line.replace(INDEXED, '$1_#');

/**
 * How much comparing two skeletons may cost before it is not worth doing properly.
 *
 * <p>The table is `before.length × after.length` cells and `row()` builds one per pair — including
 * for collapsed rows — so a pathological skeleton would freeze the page before anybody expanded
 * anything. Four reviewers asked for a ceiling and none of them named a number, so: 250,000 cells
 * is a 500-line method against a 500-line method, already far outside what the collector extracts,
 * which is ONE member. Past it the two sides are compared as wholes, which is honest and cheap and
 * visibly different from silence.</p>
 */
const MOST_CELLS = 250_000;

/**
 * The longest common subsequence of two line arrays, as the indices that survive on each side.
 *
 * <p>The classic table. It is quadratic, which is fine at the size of one method — the skeletons
 * this compares are a few dozen lines — and would not be for whole files.</p>
 */
function common(before: readonly string[], after: readonly string[]): boolean[][] {
  const longest = Array.from(
    { length: before.length + 1 },
    () => new Array<number>(after.length + 1).fill(0),
  );
  for (let left = before.length - 1; left >= 0; left -= 1) {
    for (let right = after.length - 1; right >= 0; right -= 1) {
      longest[left]![right] = before[left] === after[right]
        ? longest[left + 1]![right + 1]! + 1
        : Math.max(longest[left + 1]![right]!, longest[left]![right + 1]!);
    }
  }

  return walk(before, after, longest);
}

/** Walking the table: which lines of each side are part of the common subsequence. */
function walk(
  before: readonly string[], after: readonly string[], longest: number[][],
): boolean[][] {
  const keptBefore = new Array<boolean>(before.length).fill(false);
  const keptAfter = new Array<boolean>(after.length).fill(false);
  let left = 0;
  let right = 0;
  while (left < before.length && right < after.length) {
    if (before[left] === after[right]) {
      keptBefore[left] = true;
      keptAfter[right] = true;
      left += 1;
      right += 1;
    } else if (longest[left + 1]![right]! >= longest[left]![right + 1]!) {
      left += 1;
    } else {
      right += 1;
    }
  }

  return [keptBefore, keptAfter];
}

/**
 * Pair up a run of removals with the run of additions beside it, and call those `changed`.
 *
 * <p>Git's own line diff has only `+` and `-`; a person reading two panes side by side reads a
 * removal opposite an addition as one line REWRITTEN, and saying so is what makes the third colour
 * worth having. Only the overlap is paired: three lines gone and one arrived is one change and two
 * removals, never three.</p>
 */
function pairUp(marks: LineMark[], from: number, to: number, howMany: number): void {
  for (let at = from; at < from + howMany && at < to; at += 1) {
    marks[at] = 'changed';
  }
}

/**
 * Every line of both skeletons, marked.
 *
 * <p>An identical pair is marked `same` throughout, which is what makes "nothing differs" visible as
 * an absence of colour rather than as a page that looks broken.</p>
 */
export function pairDiff(before: string, after: string): PairDiff {
  const leftLines = before.split('\n');
  const rightLines = after.split('\n');
  if (leftLines.length * rightLines.length > MOST_CELLS) {
    return tooBig(leftLines, rightLines);
  }
  const [keptBefore, keptAfter] = common(leftLines.map(masked), rightLines.map(masked));

  const left: LineMark[] = keptBefore!.map((kept) => (kept ? 'same' : 'removed'));
  const right: LineMark[] = keptAfter!.map((kept) => (kept ? 'same' : 'added'));

  // Walk both sides together, and where a run of removals sits opposite a run of additions, mark
  // the overlap as one rewritten line on each side.
  let at = 0;
  let to = 0;
  while (at < left.length || to < right.length) {
    const goneFrom = at;
    while (at < left.length && left[at] === 'removed') {
      at += 1;
    }
    const cameFrom = to;
    while (to < right.length && right[to] === 'added') {
      to += 1;
    }
    const both = Math.min(at - goneFrom, to - cameFrom);
    pairUp(left, goneFrom, at, both);
    pairUp(right, cameFrom, to, both);
    at += 1;
    to += 1;
  }

  return { before: left, after: right };
}

/**
 * Two skeletons too large to compare line by line.
 *
 * <p>They are compared as WHOLES: identical is `same` throughout, and anything else marks every
 * line — which says "these differ and this page will not pretend to know where" rather than showing
 * a confident diff it never computed. Reachable only past {@link MOST_CELLS}.</p>
 */
function tooBig(before: readonly string[], after: readonly string[]): PairDiff {
  const identical = before.length === after.length
    && before.every((line, at) => line === after[at]);
  const mark = (lines: readonly string[], what: LineMark): LineMark[] =>
    lines.map(() => (identical ? 'same' : what));

  return { before: mark(before, 'removed'), after: mark(after, 'added') };
}
