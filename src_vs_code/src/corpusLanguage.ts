/**
 * Which of the corpus's languages a stored `language` column means — ONE table, read by everything
 * on the review page that has to decide it.
 *
 * <p>It was `GRAMMARS` inside `codeHighlight.ts` until story 2.1 of the review-page plan, when the
 * complexity count needed the same decision: which of three languages a row is in, given whatever
 * the column happens to hold. Two tables would have agreed until the day one of them learned a
 * fourth spelling, so the shared half moved out (the reuse rule's second move) and both callers read
 * it here. `corpusLanguage.test.ts` pins it against `SourceLanguage` in `src_mcp`, which is where
 * the collector actually decides what it can read.</p>
 *
 * <p><b>The VALUES are canonical; the KEYS are every spelling that might reach us.</b> The collector
 * writes `SourceLanguage` names (`CSharp`), the extensions it reads suggest others (`cs`, `ts`), and
 * a person reading the column would write `c#`. Keys are matched trimmed and lowercased. The value
 * strings are also Shiki's own grammar ids, which is what lets the highlighter use them unchanged.</p>
 *
 * <p><b>A `Map`, not an object literal.</b> An object answers `GRAMMARS['constructor']` with
 * `Object.prototype.constructor` — truthy, a function, and typed as a string by the index signature
 * — and the highlighter would then hand Shiki a function as a grammar id. A column this extension's
 * own collector writes will not say `constructor`; the point is that the table is safe whatever it
 * is handed, which is the same argument `bugzReviewPanel.ts` makes for its `switch`.</p>
 *
 * <p><b>Typed `unknown`, deliberately.</b> `ReviewPair.language` says `string` and `roundsDbRead`
 * coerces it — but a page module's safety cannot rest on what its caller happens to do today. A
 * code reviewer found the highlighter's first version throwing on `null` BEFORE the fallback that
 * existed to catch exactly that, so one malformed row took the whole page down.</p>
 *
 * <p>Pure, and free of `node:` and `vscode`, like everything the review page imports.</p>
 */

/** The three languages the collector reads, by their canonical (and Shiki) ids. */
export type CorpusLanguage = 'csharp' | 'typescript' | 'javascript';

const ALIASES: ReadonlyMap<string, CorpusLanguage> = new Map<string, CorpusLanguage>([
  ['csharp', 'csharp'],
  ['cs', 'csharp'],
  ['c#', 'csharp'],
  ['typescript', 'typescript'],
  ['ts', 'typescript'],
  ['javascript', 'javascript'],
  ['js', 'javascript'],
]);

/**
 * The corpus language a column value names, or nothing for one this page does not read.
 *
 * <p>Nothing rather than a guess: a wrong grammar colours by luck, and a wrong keyword table counts
 * decisions that are not there. Both callers render "not this one" honestly when they get nothing.</p>
 */
export function corpusLanguage(language: unknown): CorpusLanguage | undefined {
  return ALIASES.get((typeof language === 'string' ? language : '').trim().toLowerCase());
}
