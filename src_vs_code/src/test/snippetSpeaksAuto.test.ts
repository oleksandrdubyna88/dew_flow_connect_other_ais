import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { KNOWN_HALVES } from '../claudeSnippet';

/**
 * The snippet's four gate halves speak policy `auto` (E3 of `todo/PLAN_auto_never_asks_a_person.md`, story S2).
 *
 * <p>Every assertion reads a generated rule constant — the shared rule's text as the paste carries it — so
 * this is a test of rule text, not of a webview page. A phrase is read with its whitespace collapsed: the
 * rules wrap their lines, and a wrap must not decide whether a sentence is there.</p>
 *
 * <p>Two things stay true from the start, before the conventions release moves the pin: the two sentences an
 * older server still makes true, and the absence of a promise wider than the released server makes
 * (`coai-mcp` 0.66.0) — a sentence about `auto` is conditional on `auto`.</p>
 */

/** The half's text, whitespace collapsed to single spaces. */
function half(id: string): string {
  const found = KNOWN_HALVES.find((one) => (one.id as string) === id);
  assert.ok(found !== undefined, `the snippet has no ${id} half`);

  return found.text.replace(/\s+/g, ' ');
}

const GATE = 'coai-snippet';
const DOCUMENT = 'coai-document';
const FEATURE = 'coai-feature';
const QUESTION = 'coai-question';

test('the gate half names not_reviewed, consult_first and what `auto` does', () => {
  const text = half(GATE);

  assert.ok(text.includes('`not_reviewed`'), 'the gate half never names the verdict a round nobody answered ends with');
  assert.ok(text.includes('NOT reviewed'), 'the gate half never says the stage was not reviewed');
  assert.ok(text.includes('`consult_first`'), 'the gate half never names the answer to an own question without a consultId');
  assert.ok(text.includes('Under `auto`'), 'the gate half never says what policy `auto` does');
});

test('the gate half keeps the two sentences an older server still makes true', () => {
  const text = half(GATE);

  assert.ok(text.includes('Do not proceed on your own judgement'), 'step 6 lost its refusal to proceed on a held gate');
  assert.ok(text.includes('REFUSES until a plan round has reached'), 'the order sentence lost its enforced contract');
});

test('the document half names not_reviewed and says the document was not reviewed', () => {
  const text = half(DOCUMENT);

  assert.ok(text.includes('`not_reviewed`'), 'the document half never names the verdict a round nobody answered ends with');
  assert.ok(text.includes('NOT reviewed'), 'the document half never says the document was not reviewed');
});

test('the feature half names not_reviewed and says only two grounds remain under `auto`', () => {
  const text = half(FEATURE);

  assert.ok(text.includes('`not_reviewed`'), 'the feature half never names the verdict a closing round nobody answered ends with');
  assert.ok(text.includes('only the first two grounds remain'), 'the feature half still promises the person a second round under `auto`');
});

test('the question half raises the floor under `auto` and names consult_first', () => {
  const text = half(QUESTION);

  assert.ok(text.includes('`consult_first`'), 'the question half never names the answer to an own question without a consultId');
  assert.ok(text.includes("the floor rises to this rule's bar"), 'the question half never says the floor rises under `auto`');
});

test('the question half spells the production risk by phase and the workflow step by batch', () => {
  const text = half(QUESTION);

  assert.ok(text.includes('While the plan is being formed'), 'the production-risk bullet never says which phases reach the person at once');
  assert.ok(text.includes("consult once for the step's batch"), 'the workflow-step bullet never says to consult once for the batch');
});

/**
 * The promises the released server does not make true: that nobody is ever asked, or that the AI's own question is the
 * only road to a person (a question declared a production risk keeps its own path), or that nothing reaches a person.
 */
const WIDER_PROMISE = /never asks (you|a person|the person)|nobody is ever asked|only (road|way|path) (to|left to) (you|a person|the person)|never reaches (you|a person|the person)/i;

test('the no-wider-promise pattern catches each shape it is there for', () => {
  for (const shape of [
    'The gate never asks a person.',
    'Nobody is ever asked under auto.',
    'That question is the only road to a person under Auto.',
    "The AI's own question is the only way to you.",
    'The only path to a person is your own question.',
    'A question never reaches the person under auto.',
  ]) {
    assert.match(shape, WIDER_PROMISE, `the pattern misses: ${shape}`);
  }
  assert.doesNotMatch('A production risk keeps its own path to the person.', WIDER_PROMISE, 'the pattern catches a sentence the server makes true');
});

test('no half promises that nobody is ever asked', () => {
  for (const id of [GATE, DOCUMENT, FEATURE, QUESTION]) {
    assert.doesNotMatch(half(id), WIDER_PROMISE,
      `${id} promises that nobody is ever asked, or that one road is the only road — an AI's own question still reaches the person after its consultants, and a production risk keeps its own path`);
  }
});

test('the gate half names the server that still asks, and the two cases an own question skips the consultants', () => {
  const gate = half(GATE);

  assert.ok(gate.includes('a server too old to know `auto`'), 'the gate half does not name the server that still asks a person by default');
  assert.ok(gate.includes('unless no consultant can be had or it is a production risk'),
    'the gate half does not state the two cases an own question reaches the person without a consultation');
});
