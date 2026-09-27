import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { ARTEFACT_VERSION, claudeSnippet, HALF_IDS, KNOWN_HALVES, readSnippetStatus, snippetNote, snippetStatus } from '../claudeSnippet';

/**
 * The feature gate's half of the pasted snippet (story S3.3 of `todo/PLAN_feature_review.md`, D11).
 *
 * <p><b>Product-owned, like the consultant half was until 2026-09-25.</b> It says when to call ONE tool
 * of ONE server, which the operator ruled is not shared material — so its source is
 * `src_vs_code/src/featureRule.md`, emitted by `prepare-gate.mjs` beside the mounted halves, and no
 * mount in any repository carries it.</p>
 */

const FEATURE_ID = 'coai-feature';
const repo = (...parts: string[]): string => path.resolve(__dirname, '../../..', ...parts);

function featureHalf(): (typeof KNOWN_HALVES)[number] {
  const half = KNOWN_HALVES.find((one) => (one.id as string) === FEATURE_ID);
  assert.ok(half !== undefined, 'the snippet has no feature half — an AI obeying it never calls review_feature');

  return half;
}

test('the feature half is a row of the table, at v1, and travels in the paste', () => {
  const half = featureHalf();

  assert.equal(half.version, 1);
  assert.ok(HALF_IDS.includes(FEATURE_ID));
  assert.ok(claudeSnippet().includes('<!-- coai-feature v1 -->'));
  assert.equal(ARTEFACT_VERSION, 13, 'the clipboard changed, so the artefact moved one — from the 12 the consultant move left');
});

test('the half is this product’s own file, byte for byte — never a copy in a mount', () => {
  const half = featureHalf();
  const own = repo('src_vs_code', 'src', 'featureRule.md');

  assert.ok(fs.existsSync(own), 'src_vs_code/src/featureRule.md is the source; it is missing');
  assert.equal(half.text, fs.readFileSync(own, 'utf8').replace(/\r\n/g, '\n'), 'the generated constant is not the file');
  assert.ok(!fs.existsSync(repo('.agents', 'conventions', 'common', 'coai-feature.md')),
    'a mounted copy would be a second source for one half');
});

test('it says WHEN — only at the end of a plan of three or more epics, before the release', () => {
  const text = featureHalf().text;

  assert.match(text, /mcp__coai__review_feature/);
  assert.match(text, /THREE or more epics/, 'D17: the feature gate is for plans of three or more epics');
  assert.match(text, /every epic\s+is built/i, 'after all epics, not per epic');
  assert.match(text, /before the release/i);
  assert.match(text, /covered by `mcp__coai__review_code`/, 'a smaller plan is the code gate’s, and the paste must say so');
});

test('it says WHAT to pass — the plan’s path, the base before the first epic, the head, the epics, the lessons', () => {
  const text = featureHalf().text;

  for (const argument of ['`planPath`', '`baseRef`', '`head`', '`epics`', '`lessons`', '`repoPath`']) {
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
  assert.match(text, /new `head`/, 'the next round is asked about the new head');
  assert.match(text, /`again: true`/, 'a finished feature review is reopened only with again');
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
 * A repository that MOUNTS the shared rules is judged on what a mount can carry.
 *
 * <p>No mount carries the feature half — it is this product's own — so a repository with four mounted
 * rules and no paste would be told, for ever, that it lacks something no mount can give it, with advice
 * (paste the whole block over the mount) its own shared-rule check forbids. The instruction reaches such
 * a repository through the `review_feature` tool description (D11). A real PASTE is still judged whole,
 * because that is the text the AI in that repository reads.</p>
 */
test('a mounting repository is not told it lacks a half no mount can carry — a paste still is', async (t) => {
  const root = await fs.promises.mkdtemp(path.join(os.tmpdir(), 'coai-feature-mount-'));
  t.after(() => fs.promises.rm(root, { recursive: true, force: true }));
  const files = new Map<string, string>();
  for (const name of ['coai-review-gate.md', 'coai-document-gate.md', 'coai-caller-model.md', 'coai-consultant.md']) {
    files.set(`.agents/conventions/common/${name}`, fs.readFileSync(repo('.agents', 'conventions', 'common', name), 'utf8'));
  }
  const read = async (name: string): Promise<string> => files.get(name) ?? '';

  assert.deepEqual(await readSnippetStatus(read), { kind: 'current', current: ARTEFACT_VERSION });

  files.set('CLAUDE.md', claudeSnippet().replace(/<!-- coai-feature v\d+ -->/, ''));
  assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: [FEATURE_ID], current: ARTEFACT_VERSION });
});
