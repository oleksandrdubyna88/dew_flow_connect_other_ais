import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { PROMPT_GROUPS, PROMPT_TEXTS } from '../helpPrompts';

/**
 * What the help PRINTS is what the server SENDS.
 *
 * <p>The prompts are embedded in the server binary, and the panel is drawn before any server has
 * started — so the help carries its own copy, and a copy drifts unless something holds it. This
 * is that something: edit a prompt and forget the help, and the build fails on the commit that
 * did it rather than on the day somebody notices the page is describing the old question.</p>
 */

const promptsDir = path.join(__dirname, '..', '..', '..', 'src_mcp', 'src', 'prompts');

function onDisk(id: string): string {
  return fs.readFileSync(path.join(promptsDir, `${id}.md`), 'utf8').replace(/\r\n/g, '\n');
}

test('every prompt the help prints is byte-for-byte what the server ships', () => {
  for (const id of Object.keys(PROMPT_TEXTS)) {
    assert.equal(
      PROMPT_TEXTS[id],
      onDisk(id),
      `${id}.md has changed. Regenerate helpPrompts.ts — the help is showing the old question.`,
    );
  }
});

test('every prompt file the server ships is printed in the help', () => {
  const shipped = fs
    .readdirSync(promptsDir)
    .filter((f) => f.endsWith('.md'))
    .map((f) => f.replace(/\.md$/, ''));

  for (const id of shipped) {
    assert.ok(PROMPT_TEXTS[id] !== undefined, `${id}.md is shipped but the help does not print it.`);
  }
  assert.equal(Object.keys(PROMPT_TEXTS).length, shipped.length);
});

test('the groups cover every prompt exactly once, so none is printed twice or not at all', () => {
  const grouped = PROMPT_GROUPS.flatMap((g) => g.ids);
  assert.equal(new Set(grouped).size, grouped.length, 'a prompt appears in two groups');
  assert.deepEqual(new Set(grouped), new Set(Object.keys(PROMPT_TEXTS)));
});

test('each role leads with its universal prompt', () => {
  // The lens is the deliberate pick; the universal one is what a round uses when nobody chose.
  // Printing a lens first would read as the default.
  // The conventions pass is the one group of ONE: it is not a lens on a role's question, it is a
  // different question that all three code roles ask in round 1, so it has nothing to lead.
  for (const group of PROMPT_GROUPS) {
    const expected = group.ids[0] === 'conventions' ? 1 : 3;
    assert.equal(group.ids.length, expected, `${group.role}: expected a universal prompt and two lenses`);
  }
  const leads = PROMPT_GROUPS.filter((g) => g.ids.length === 3).map((g) => g.ids[0]);
  assert.deepEqual(leads, ['plan-critique', 'architecture', 'security-reliability', 'uxdx-performance']);
});

test('a prompt is substantial enough to be the thing a reviewer actually reads', () => {
  for (const [id, text] of Object.entries(PROMPT_TEXTS)) {
    assert.ok(text.length > 400, `${id} is too short to be a review prompt`);
    assert.ok(
      text.includes('empty findings list is a valid answer'),
      `${id} no longer tells the reviewer that finding nothing is allowed — a reviewer told to always find something will.`,
    );
  }
});
