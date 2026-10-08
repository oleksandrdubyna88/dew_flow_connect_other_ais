import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { phrasesFrom } from '../phrases';
import { rowsAfter, rowsOf } from '../phrasesEdit';
import { NAME_LIMIT } from '../savedRows';

/**
 * A fresh install starts with seven phrases (research/PLAN_default_phrases.md): the operator's own, in English, as the
 * DECLARED DEFAULT of `coai.phrases`.
 *
 * <p>A default, never a write: VS Code hands it only to a person whose settings hold no `coai.phrases` at all. A person
 * with phrases of their own, or with a list they emptied, keeps what they have — the operator's rule, "if people have
 * something, do not touch it". The second half of that promise — an emptied list STAYS empty — is what the last test
 * here and the editor scenario `theDefaultPhrasesInARealEditor` are for.</p>
 */

type Declared = { readonly default?: unknown };

function declaredDefault(): unknown {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { configuration: readonly { properties?: Record<string, Declared> }[] | { properties?: Record<string, Declared> } };
  };
  const sections = Array.isArray(manifest.contributes.configuration)
    ? manifest.contributes.configuration
    : [manifest.contributes.configuration];
  const declared = sections.flatMap((section) => (section.properties?.['coai.phrases'] === undefined ? [] : [section.properties['coai.phrases']]));
  assert.equal(declared.length, 1, 'coai.phrases is not declared exactly once');

  return declared[0]?.default;
}

/** The seven, word for word — a change here is a decision about what every new install sees. */
const SEVEN: readonly (readonly [string, string])[] = [
  ['PR, CodeRabbit, deploy',
    'Create the PR, read the PR comments and fix them, then deploy and release (if there is something to release), autonomously.'],
  ['What problem are we solving?',
    'Remind me what problem we are solving, where we are now, and what is left to do.'],
  ['Questions, suggestions, plan',
    'Review it again. If you have questions, ask them. If you find something that can be improved or fixed, say so. '
    + 'The result must be a plan of the highest quality.'],
  ['Continue',
    'Continue, following all the development rules, the coai gates and best practices, autonomously.'],
  ['All done?',
    'Is everything I asked you to do implemented, are the PRs created and merged, and is the deploy (release) done?'],
  ['Consultant',
    'Discuss it with the consultant.'],
  ['Progress?',
    'Tell me about the progress, measured against what I asked you to do: what is done and what is left, in percent and '
    + 'in approximate hours.\nIf there are open questions, highlight them.\nAfter the answer, continue what you were doing, '
    + 'autonomously.'],
];

test('a fresh install starts with the seven phrases, every one kept by the reader the panel uses', () => {
  const raw = declaredDefault();
  const phrases = phrasesFrom(raw);

  assert.ok(Array.isArray(raw), 'the default of coai.phrases is not a list');
  assert.equal(raw.length, SEVEN.length, 'the default does not hold exactly the seven phrases');
  assert.deepEqual(phrases.map((phrase) => [phrase.name, phrase.text]), SEVEN, 'a default phrase was dropped, renamed or reworded');
});

test('every default phrase has its own stable id, and every name fits on a button', () => {
  const raw = declaredDefault() as readonly Record<string, unknown>[];
  const ids = raw.map((row) => row['id']);

  // Pinned exactly: a colour and a button follow the id, so renaming one is a decision, not a tidy-up (CodeRabbit, #710).
  assert.deepEqual(ids, [
    'phrase-default-pr-coderabbit-deploy',
    'phrase-default-what-problem',
    'phrase-default-questions-plan',
    'phrase-default-continue',
    'phrase-default-all-done',
    'phrase-default-consultant',
    'phrase-default-progress',
  ], 'a default phrase id changed or moved');
  assert.equal(new Set(ids).size, ids.length, 'two default phrases share an id, so a click could copy the wrong one');
  assert.deepEqual(phrasesFrom(raw).map((phrase) => phrase.id), ids, 'the reader re-keyed an id, so the colour would move');
  for (const phrase of phrasesFrom(raw)) {
    assert.ok(phrase.name.length <= NAME_LIMIT, `"${phrase.name}" is longer than a button holds`);
  }
});

test('removing every default phrase leaves an EMPTY LIST to save, never "unchanged" — so the defaults cannot come back', () => {
  let rows: readonly Record<string, unknown>[] = rowsOf(declaredDefault());
  for (const row of [...rows]) {
    const after = rowsAfter(rows, { kind: 'remove', id: String(row['id']) });
    assert.equal(after.kind, 'rows', `removing ${String(row['id'])} saved nothing`);
    rows = after.kind === 'rows' ? after.rows : rows;
  }

  assert.deepEqual(rows, [], 'the last removal did not leave an empty list to write');
});
