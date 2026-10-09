import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { ARTEFACT_VERSION, claudeSnippet, HALF_IDS, KNOWN_HALVES, readSnippetStatus, snippetNote, snippetStatus } from '../claudeSnippet';

/**
 * The feature gate's half of the pasted snippet (story S3.3 of `todo/PLAN_feature_review.md`, D11).
 *
 * <p><b>A mounted rule since v3 (2026-10-09).</b> It was this product's own file under D11 until the
 * operator ruled that the conventions carry it; its one source is now
 * `.agents/conventions/common/coai-feature-gate.md`, emitted by `prepare-gate.mjs` like every other
 * half, and this product keeps no copy (research/PLAN_the_feature_and_question_halves_are_shared_rules.md).</p>
 */

const FEATURE_ID = 'coai-feature';
const repo = (...parts: string[]): string => path.resolve(__dirname, '../../..', ...parts);

function featureHalf(): (typeof KNOWN_HALVES)[number] {
  const half = KNOWN_HALVES.find((one) => (one.id as string) === FEATURE_ID);
  assert.ok(half !== undefined, 'the snippet has no feature half — an AI obeying it never calls review_feature');

  return half;
}

test('the feature half is a row of the table, at v3, and travels in the paste', () => {
  const half = featureHalf();

  // v2: the half stopped telling callers to pass a `head` the tool did not declare (§9.30).
  // v3: it became the mounted rule, and its verdicts say what D23 does.
  assert.equal(half.version, 3);
  assert.ok(HALF_IDS.includes(FEATURE_ID));
  assert.ok(claudeSnippet().includes('<!-- coai-feature v3 -->'));
  assert.equal(ARTEFACT_VERSION, 15, 'the clipboard changed, so the artefact moved one — from the 14 of the feature half’s v2');
});

test('the half has one source — the mounted rule — and the product keeps no copy', () => {
  const half = featureHalf();
  const mounted = repo('.agents', 'conventions', 'common', 'coai-feature-gate.md');

  assert.ok(fs.existsSync(mounted), `run git submodule update --init .agents/conventions (${mounted})`);
  assert.ok(half.text.startsWith('<!-- coai-feature v3 -->\n## Reviewing the whole FEATURE before release'),
    'the generated constant starts at the marker: frontmatter and owns: lines are delivery metadata');
  assert.ok(fs.readFileSync(mounted, 'utf8').replace(/\r\n/g, '\n').endsWith(half.text), 'the generated constant is not the mounted rule');
  assert.ok(!fs.existsSync(repo('src_vs_code', 'src', 'featureRule.md')),
    'a product copy would be a second source for one half — delete src_vs_code/src/featureRule.md');
});

/**
 * D23, the one-round budget the server ships (`FeatureSecondRound.cs`): v2 of this half said `revise`
 * meant "call again", which the server refuses over the same base without a ground.
 */
test('it says one round is the budget, and the three grounds for a second', () => {
  const text = featureHalf().text;

  assert.match(text, /One round is the budget/);
  assert.match(text, /a\s+reviewer failure, a `blocking` finding, or the person asking for it/);
  assert.match(text, /no request of yours adds a\s+third/);
  assert.match(text, /`good_enough`/, 'good_enough closes the review on resolve');
  assert.match(text, /needs no new commits and asks only the reviewers that failed/);
  assert.match(text, /`again: true` with a DIFFERENT `baseRef` starts a fresh review/);
  assert.doesNotMatch(text, /continue_anyway/, 'a feature round never produces continue_anyway');
});

test('it says WHEN — only at the end of a plan of three or more epics, before the release', () => {
  const text = featureHalf().text;

  assert.match(text, /mcp__coai__review_feature/);
  assert.match(text, /THREE or more epics/, 'D17: the feature gate is for plans of three or more epics');
  assert.match(text, /every epic\s+is built/i, 'after all epics, not per epic');
  assert.match(text, /before the release/i);
  assert.match(text, /covered by `mcp__coai__review_code`/, 'a smaller plan is the code gate’s, and the paste must say so');
});

test('it says WHAT to pass — the plan’s path, the base before the first epic, the epics, the lessons', () => {
  const text = featureHalf().text;

  for (const argument of ['`planPath`', '`baseRef`', '`epics`', '`lessons`', '`repoPath`']) {
    assert.ok(text.includes(argument), `the paste never names ${argument}`);
  }
  assert.match(text, /commit BEFORE the first epic/, 'baseRef is the commit before the first epic, not a branch point guessed later');
  assert.match(text, /`pitfalls`, `blockers` and `findings`/);
  assert.match(text, /each non-empty/i, 'an empty lessons array is refused by the server');
  assert.match(text, /"none" with the reason/i, 'nothing to say is written as "none" with a reason, never []');
  assert.match(text, /`branch`/, 'an epic’s branch is what finds the history of a squash-merged epic');
});

test('it says what each verdict means — and that a skip does NOT block', () => {
  const text = featureHalf().text;

  assert.match(text, /`skipped` does NOT block/);
  assert.match(text, /tell the person/i, 'a skip is reported to the person, with its reason');
  assert.match(text, /`call_human` stops the release/);
  assert.match(text, /mcp__coai__ask_human/);
  assert.match(text, /NEW pull requests/, 'fixes land as new pull requests, not by rewriting merged epics');
  assert.match(text, /`again: true`/, 'a finished feature review is reopened only with again');
});

/**
 * §9.30 of `todo/PLAN_feature_review.md`, found by the D26 live run: this half told the caller to pass a
 * `head` — `origin/main` included — and to call again "with the new `head`", while the tool declared no
 * such argument. The SDK drops an undeclared argument without an error, so a caller following the paste
 * from another checkout got a clean review of the WRONG tree. The head reviewed is the checkout's HEAD, as
 * the conventions' feature-gate rule says; the paste must say that, and never offer `head` as a choice.
 */
test('it never offers `head` as a choice — the head reviewed is the checkout’s HEAD, so run it from the checkout that holds the feature', () => {
  const text = featureHalf().text;

  // `\s+` between words: prose wraps wherever it wraps, and a test pinned to one line break would go red
  // on a re-flow that changed no word.
  assert.match(text, /run\s+it\s+from\s+the\s+checkout\s+that\s+holds\s+the\s+finished\s+feature/i, 'where the caller must be');
  assert.match(text, /the\s+head\s+reviewed\s+is\s+that\s+checkout's\s+HEAD/, 'what is reviewed, in the conventions rule’s words');
  assert.match(text, /only\s+committed\s+work\s+is\s+read/);
  assert.doesNotMatch(text, /^- `head` —/m, '`head` is not a thing to pass: it chooses nothing');
  assert.doesNotMatch(text, /origin\/main/, 'naming a ref to review is the instruction that reviewed the wrong tree');
  assert.doesNotMatch(text, /new\s+`head`/, 'a later round reads the checkout, not an argument');
  assert.match(text, /naming\s+any\s+other\s+commit\s+is\s+refused/, 'the optional `head` is only a check, and it refuses a mismatch');
});

test('it says how to resolve and re-orient — `feature` is the plan’s path on resolve, status and ask_human', () => {
  const text = featureHalf().text;

  assert.match(text, /mcp__coai__resolve/);
  assert.match(text, /mcp__coai__status/);
  assert.match(text, /`feature: <planPath>`/);
});

test('a paste made before the feature half is OLDER, naming it', () => {
  const without = claudeSnippet().replace(/<!-- coai-feature v\d+ -->/, '');
  const status = snippetStatus(without);

  assert.deepEqual(status, { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION });
  assert.match(snippetNote(status), /the feature gate/);
});

/**
 * A repository that MOUNTS the shared rules is judged on the feature half like any other.
 *
 * <p>Until 2026-10-09 no mount could carry it, so a mount was judged without it. Every half is a shared
 * rule now: a mount from before the move lacks the file, and that is a pin to move — the same sentence a
 * mount missing any other sibling gets. A real PASTE is still judged whole and still wins.</p>
 */
test('a mount without the feature rule is older naming it — and a paste still wins', async (t) => {
  const root = await fs.promises.mkdtemp(path.join(os.tmpdir(), 'coai-feature-mount-'));
  t.after(() => fs.promises.rm(root, { recursive: true, force: true }));
  const files = new Map<string, string>();
  for (const half of KNOWN_HALVES) {
    files.set(`.agents/conventions/common/${half.file}`, fs.readFileSync(repo('.agents', 'conventions', 'common', half.file), 'utf8'));
  }
  const read = async (name: string): Promise<string> => files.get(name) ?? '';

  assert.deepEqual(await readSnippetStatus(read), { kind: 'current', current: ARTEFACT_VERSION }, 'six mounted rules and no paste');

  files.delete('.agents/conventions/common/coai-feature-gate.md');
  assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION },
    'a pin from before the move');

  files.set('.agents/conventions/common/coai-feature-gate.md', fs.readFileSync(repo('.agents', 'conventions', 'common', 'coai-feature-gate.md'), 'utf8'));
  files.set('CLAUDE.md', claudeSnippet().replace(/<!-- coai-feature v\d+ -->/, ''));
  assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION });
});
