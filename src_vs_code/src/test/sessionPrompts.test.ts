import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { humanSaid } from '../claudeQuestion';
import { MOST_PER_PROMPT, MOST_PROMPTS, promptIn, promptsIn } from '../sessionPrompts';

/**
 * What the person wrote in the lines of a tail (E2, the shared prompt rule), and how far it reads.
 *
 * <p>`promptsIn` takes the function that says what a line holds as a seam, so a test can COUNT the lines it parsed.</p>
 */

const said = (text: string): string => JSON.stringify({ type: 'user', message: { content: text }, origin: { kind: 'human' } });
const other = JSON.stringify({ type: 'ai-title', aiTitle: 'a title' });

/** The reference the loop replaced: every line mapped, filtered, then cut to the most. It must give the same prompts. */
const everyLine = (lines: readonly string[]): readonly string[] =>
  lines.map((line) => promptIn(line)).filter((prompt) => prompt.length > 0).slice(0, MOST_PROMPTS);

test('S4-C: a tail of 100000 lines is read only as far as the 200 prompts that are shown', () => {
  const prompts = Array.from({ length: MOST_PROMPTS }, (_, index) => said(`prompt ${index}`));
  const tail = [...Array.from({ length: 50 }, () => other), ...prompts, ...Array.from({ length: 100_000 - 50 - MOST_PROMPTS }, () => said('later'))];
  let parsed = 0;
  const counted = (line: string): string => {
    parsed += 1;

    return humanSaid(line);
  };

  const got = promptsIn(tail, counted);

  assert.equal(tail.length, 100_000);
  assert.equal(got.length, MOST_PROMPTS);
  assert.equal(got[0], 'prompt 0', 'the earliest prompt is not first');
  assert.equal(got[MOST_PROMPTS - 1], `prompt ${MOST_PROMPTS - 1}`);
  assert.ok(parsed <= MOST_PROMPTS + 50, `${parsed} lines were parsed for ${MOST_PROMPTS} prompts: the loop reads past the ones that are shown`);
  assert.deepEqual(got, everyLine(tail), 'stopping early gave other prompts than reading every line');
});

test('S4-C: fewer prompts than the most are all read, in order, each cut as the file reader cuts it', () => {
  const tail = [said('first'), other, '{not json', said('x'.repeat(MOST_PER_PROMPT + 10)), said('last')];
  const got = promptsIn(tail, humanSaid);

  assert.deepEqual(got, everyLine(tail));
  assert.equal(got.length, 3);
  assert.ok((got[1] ?? '').includes('(cut here'), 'a long prompt was not cut');
  assert.deepEqual(promptsIn([], humanSaid), []);
});
