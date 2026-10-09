import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { ARTEFACT_VERSION, claudeSnippet, HALF_IDS, KNOWN_HALVES, readSnippetStatus, snippetNote, snippetStatus } from '../claudeSnippet';

/**
 * The feature gate's half of the pasted snippet (stories S3.3 and S3.5 of `todo/PLAN_feature_review.md`).
 *
 * <p><b>A shared rule since conventions #58 (S3.5, D24).</b> Until then it was this product's own file,
 * `src_vs_code/src/featureRule.md` (D11), and no mount carried it. v3 lives in
 * `.agents/conventions/common/coai-feature-gate.md`, so a paste and a mount carry the same text — and the
 * verdict section v2 had wrong (a one-round budget, three grounds for a second) is gone from both.</p>
 */

const FEATURE_ID = 'coai-feature';
const FEATURE_FILE = 'coai-feature-gate.md';
const repo = (...parts: string[]): string => path.resolve(__dirname, '../../..', ...parts);

function featureHalf(): (typeof KNOWN_HALVES)[number] {
  const half = KNOWN_HALVES.find((one) => (one.id as string) === FEATURE_ID);
  assert.ok(half !== undefined, 'the snippet has no feature half — an AI obeying it never calls review_feature');

  return half;
}

/** A shared rule's body as the paste carries it: from its marker on, line endings settled. */
function mountedBody(file: string): string {
  const source = fs.readFileSync(repo('.agents', 'conventions', 'common', file), 'utf8').replace(/\r\n/g, '\n');
  const marker = source.indexOf('<!-- coai-');
  assert.ok(marker > 0, `${file} carries no marker after its metadata`);

  return source.slice(marker);
}

test('the feature half is a row of the table, at v3, and travels in the paste', () => {
  const half = featureHalf();

  // v2: the half stopped telling callers to pass a `head` the tool did not declare (§9.30).
  // v3: the shared rule — the one-round budget and its three grounds (conventions #58).
  assert.equal(half.version, 3);
  assert.ok(HALF_IDS.includes(FEATURE_ID));
  assert.ok(claudeSnippet().includes('<!-- coai-feature v3 -->'));
  // The feature half's v2 moved the artefact from 13 to 14; any later change to the paste only raises it
  // (15: the consultant half's v4; 16: this half's v3). An exact pin here broke on every unrelated half.
  assert.ok(ARTEFACT_VERSION >= 16, `the feature half moved to the shared v3, so the artefact moved past 15 — it is ${ARTEFACT_VERSION}`);
});

test('the half is the mounted shared rule, byte for byte — and the product copy is gone', () => {
  const half = featureHalf();

  assert.equal(half.file as string, FEATURE_FILE, 'the row names the shared rule file a mount carries');
  assert.equal(half.text, mountedBody(FEATURE_FILE), 'the generated constant is not the mounted rule');
  assert.ok(!fs.existsSync(repo('src_vs_code', 'src', 'featureRule.md')),
    'src_vs_code/src/featureRule.md would be a second source for one half — the shared rule is the only one');
});

test('the verdicts are the server’s: one round, and a second only on its three grounds', () => {
  const text = featureHalf().text;

  assert.match(text, /One round is the budget/);
  assert.match(text, /a\s+reviewer failure, a `blocking` finding, or the person asking for it/);
  assert.match(text, /`again: true` with a DIFFERENT `baseRef` starts a fresh review/);
  assert.doesNotMatch(text, /reopened only with `again: true`, once the checkout's HEAD has moved/,
    'v2 promised a reopen the server refuses over the same base');
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
 * A repository that MOUNTS the shared rules is judged on every half again (S3.5; six since the question half).
 *
 * <p>While the feature half was this product's own, no mount could carry it, so a mount was judged on the
 * other four (S3.3a). Since conventions #58 a mount carries `coai-feature-gate.md` like any other half: a
 * mount that has it is current, and a mount pinned before it is told the feature gate is missing — which
 * it is, and the cure is moving its pin. A PASTE is judged whole, as it always was.</p>
 */
const MOUNTED_RULES: readonly string[] = KNOWN_HALVES.map((half) => half.file);

/** A reader over a fixed set of files — a fresh map per scenario, never one edited between them. */
function readerOver(entries: readonly (readonly [string, string])[]): (name: string) => Promise<string> {
  const files = new Map(entries);

  return async (name) => files.get(name) ?? '';
}

/** The real shared rules, as a mount at `folder` holds them. */
function mountAt(folder: string, rules: readonly string[] = MOUNTED_RULES): readonly (readonly [string, string])[] {
  return rules.map((name) => [`${folder}${name}`, fs.readFileSync(repo('.agents', 'conventions', 'common', name), 'utf8')] as const);
}

test('a mount is judged on all five halves — current with the feature rule, older on a pin from before it', async () => {
  const neutral = '.agents/conventions/common/';
  const beforeTheFeatureRule = mountAt(neutral, MOUNTED_RULES.filter((name) => name !== FEATURE_FILE));

  assert.deepEqual(await readSnippetStatus(readerOver(mountAt(neutral))), { kind: 'current', current: ARTEFACT_VERSION });
  assert.deepEqual(await readSnippetStatus(readerOver(beforeTheFeatureRule)),
    { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION },
    'a mount pinned before the feature rule lacks it, and is told so');
  assert.deepEqual(
    await readSnippetStatus(readerOver([...mountAt(neutral), ['CLAUDE.md', claudeSnippet().replace(/<!-- coai-feature v\d+ -->/, '')]])),
    { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION },
    'a paste without the half wins over a current mount');
});

test('the legacy mount path carries the feature rule too', async () => {
  assert.deepEqual(await readSnippetStatus(readerOver(mountAt('.claude/rules/shared/common/'))), { kind: 'current', current: ARTEFACT_VERSION });
});
