import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { ARTEFACT_VERSION, claudeSnippet, HALF_IDS, KNOWN_HALVES, QUESTION_VERSION, readSnippetStatus, snippetNote, snippetStatus } from '../claudeSnippet';

/**
 * The question half of the pasted snippet — ask the consultants before the person
 * (research/PLAN_the_feature_and_question_halves_are_shared_rules.md, D3/D4).
 *
 * <p>The server's phase rule lets planning questions and the first two batches after a plan's
 * `proceed` reach the person directly. On 2026-10-09 the operator asked for more: every question goes
 * to `ask_consultants` first, and the person is asked only what the answers did not settle and what is
 * theirs to decide. The rule is the conventions' `common/coai-question-consultant.md`; these tests hold
 * the paste to the sentences that carry it.</p>
 */

const QUESTION_ID = 'coai-question';
const repo = (...parts: string[]): string => path.resolve(__dirname, '../../..', ...parts);

function questionHalf(): (typeof KNOWN_HALVES)[number] {
  const half = KNOWN_HALVES.find((one) => (one.id as string) === QUESTION_ID);
  assert.ok(half !== undefined, 'the snippet has no question half — an AI obeying it asks the person first');

  return half;
}

test('the question half is the last row of the table, at v1, and travels in the paste', () => {
  const half = questionHalf();

  assert.equal(half.version, 1);
  assert.equal(QUESTION_VERSION, 1);
  assert.equal(HALF_IDS.at(-1), QUESTION_ID, 'the two consultants close the paste, the question one last');
  assert.equal(half.file, 'coai-question-consultant.md');
  assert.ok(claudeSnippet().includes('<!-- coai-question v1 -->'));
});

test('it raises the bar above the server: the consultants first, in every phase', () => {
  const text = questionHalf().text;

  assert.match(text, /mcp__coai__ask_consultants/);
  assert.match(text, /no question reaches the person before the consultants have had it/);
  assert.match(text, /in every phase/);
  assert.match(text, /The server's phase rule is the floor, not the target/);
});

test('it says what the person still gets — the unsettled, and three kinds always — with the answers beside it', () => {
  const text = questionHalf().text;

  assert.match(text, /what the answers did not settle/);
  assert.match(text, /three kinds always/);
  assert.match(text, /another owner's repository/, 'an action outside the working copy is the person’s');
  assert.match(text, /the person's own machine/, 'a change to their machine is the person’s');
  assert.match(text, /pure preference/);
  assert.match(text, /Show the person what the consultants said/);
});

test('it keeps the server’s mechanics — consultId, ask_in_conversation, the gate’s own question, productionRisk', () => {
  const text = questionHalf().text;

  assert.match(text, /`consultId`/);
  assert.match(text, /`ask_in_conversation`/);
  assert.match(text, /review gate's own question never goes to the consultants/);
  assert.match(text, /`productionRisk: true` with a `riskReason`/);
  assert.match(text, /without a `consultId`/, 'the fallback has a door when no consultant can be had');
});

test('the half is the mounted rule, verbatim, and the paste without it is OLDER naming it', async () => {
  const mounted = repo('.agents', 'conventions', 'common', 'coai-question-consultant.md');
  assert.ok(fs.existsSync(mounted), `run git submodule update --init .agents/conventions (${mounted})`);
  assert.ok(fs.readFileSync(mounted, 'utf8').replace(/\r\n/g, '\n').endsWith(questionHalf().text));

  const without = claudeSnippet().replace(/<!-- coai-question v\d+ -->/, '');
  const status = snippetStatus(without);
  assert.deepEqual(status, { kind: 'older', behind: [QUESTION_ID], current: ARTEFACT_VERSION });
  assert.match(snippetNote(status), /the question consultant/);

  // And a MOUNT from before the rule existed: five sibling files, no question one.
  const files = new Map<string, string>();
  for (const half of KNOWN_HALVES.filter((one) => (one.id as string) !== QUESTION_ID)) {
    files.set(`.agents/conventions/common/${half.file}`, fs.readFileSync(repo('.agents', 'conventions', 'common', half.file), 'utf8'));
  }
  assert.deepEqual(await readSnippetStatus(async (name) => files.get(name) ?? ''),
    { kind: 'older', behind: [QUESTION_ID], current: ARTEFACT_VERSION });
});
