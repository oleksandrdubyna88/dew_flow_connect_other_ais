/**
 * Rescores the recorded answers without asking the models again.
 *
 * The first pass's `batchesQuestions` was wrong: it looked for "at the end" and the models write
 * "deferred to the end of the summary" and "interrupt the operator only once". The answers are kept
 * whole in the run files precisely so a broken metric costs a rescore rather than an hour of GPU —
 * and so the fix can be seen against the same data rather than a new sample.
 */
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const repo = process.argv[2];
const dir = join(repo, 'artifacts', 'campaign');

const BATCHED = /(end of (?:the )?summary|final summary|at the end|in a batch|batched|all at once|only once|once with all|interrupt(?:ed|ing)? (?:the )?(?:operator|person|user|you) only|aggregated|grouped)/i;
const REVIEW_LOOP = /(review_code|code review|review the diff|review its diff|gate)/i;
const COMMIT_LOOP = /(commit)/i;

function rescore(row) {
  const a = row.answerText;
  if (!a) return { ok: false };
  const units = a.units ?? [];
  const kidCounts = units.map((u) => u.children?.length ?? 0);
  const children = kidCounts.reduce((n, k) => n + k, 0);
  const kinds = units.map((u) => `${u.kind ?? ''} ${u.title ?? ''}`).join(' ');
  const text = `${a.approach ?? ''} ${a.questionPolicy ?? ''} ${a.modelPolicy ?? ''}`;
  const inRange = units.length >= 2 && units.length <= 4;
  const twoLevel = children > 0;
  const everyChildInRange = kidCounts.every((k) => k >= 2 && k <= 4);
  const verdict = row.verdict;

  return {
    ok: true,
    units: units.length,
    children,
    inRange,
    twoLevel,
    shapeAsOrdered: verdict === 'Epics'
      ? inRange && twoLevel && everyChildInRange
      : verdict === 'Stories'
      ? inRange && !twoLevel
      : inRange,
    epicWord: /epic/i.test(kinds),
    storyWord: /\bstor(y|ies)\b/i.test(`${kinds} ${text}`),
    reviewsEachUnit: a.reviewsEachUnit === true,
    commitsEachUnit: a.commitsEachUnit === true,
    // The booleans are ill-posed for the epic arm — a plan built as ONE unit has no "every unit
    // before the next" — so the loop is also looked for in the prose, where it can be stated.
    mentionsReview: REVIEW_LOOP.test(`${text} ${kinds}`) || a.reviewsEachUnit === true,
    mentionsCommit: COMMIT_LOOP.test(`${text} ${kinds}`) || a.commitsEachUnit === true,
    batchesQuestions: BATCHED.test(a.questionPolicy ?? ''),
    namesFable: /fable/i.test(text),
    namesOpus: /opus/i.test(text),
    kinds: units.map((u) => u.kind ?? '').join(' | '),
  };
}

for (const run of ['A', 'B']) {
  const file = join(dir, `runs-${run}.json`);
  if (!existsSync(file)) continue;
  const rows = JSON.parse(readFileSync(file, 'utf8'));
  for (const row of rows) row.score = rescore(row);
  writeFileSync(file, JSON.stringify(rows, null, 1));
  console.log(`rescored ${rows.length} rows in runs-${run}.json`);
}
