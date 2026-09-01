import assert from 'node:assert/strict';
import { test } from 'node:test';
import { claudeSnippet } from '../claudeSnippet';

/**
 * This gate is ADDITIONAL. It must never read as a replacement for the host AI's own review.
 *
 * <p>The concrete case that prompted this: the `feature-dev` plugin's Phase 6 launches three
 * Claude `code-reviewer` agents in parallel — simplicity, correctness, conventions — at exactly the
 * moment our snippet says to call `review_code`. Nothing in the snippet forbade that phase, and
 * nothing protected it either: our text is a numbered CONTRACT with "the server enforces it" and
 * "skipped stages are impossible", theirs is one phase in a workflow. Emphasis wins in practice,
 * and an agent that reads "this repository is reviewed before and after implementation" can
 * reasonably conclude the review step for this repo IS this gate.</p>
 *
 * <p>Which would be a bad trade in both directions. Those reviewers read the whole change with the
 * repository in context and cost seconds; this gate asks a DIFFERENT vendor's model the questions
 * the author's own model is worst placed to answer. The value is that they are not the same
 * reviewer — losing either one to save time loses the half you did not measure.</p>
 */

test('the snippet says out loud that it does not replace the host’s own review', () => {
  const snippet = claudeSnippet().toLowerCase();

  assert.match(snippet, /in addition/, 'nothing states that this runs alongside, not instead of');
  assert.match(snippet, /never instead of|not instead of/, 'the replacement reading is not closed off');
});

test('it names the parallel case, because that is where the minutes are', () => {
  // A code round is minutes of other people's CLIs. Read sequentially, an agent either waits for
  // this gate before its own reviewers or forgets them afterwards; both are avoidable by saying
  // "at the same time".
  const snippet = claudeSnippet().toLowerCase();

  assert.match(snippet, /parallel|at the same time|while/);
});

test('call_human stops the GATE, not the task', () => {
  // "Do not proceed on your own judgement" is about shipping over open findings. An agent that
  // reads it as "stop working" never reaches its own review phase or its summary.
  const snippet = claudeSnippet();

  assert.match(snippet, /Do not proceed on your own judgement/);
  assert.match(snippet, /own review|other reviewers|your own reviewers/i,
    'the stop instruction has nothing beside it saying what still runs');
});

test('no paragraph is printed twice', () => {
  // Found by reading the shipped text: the "never given a bare diff" paragraph appeared verbatim
  // twice, so everybody who pasted this got it twice. A snippet is a document somebody reads once
  // and trusts; a duplicated paragraph is the cheapest possible way to lose that.
  const paragraphs = claudeSnippet()
    .split(/\n\s*\n/)
    .map((p) => p.replace(/\s+/g, ' ').trim())
    .filter((p) => p.length > 80);

  const seen = new Map<string, number>();
  for (const p of paragraphs) {
    seen.set(p, (seen.get(p) ?? 0) + 1);
  }
  const repeated = [...seen.entries()].filter(([, n]) => n > 1).map(([p]) => p.slice(0, 70));

  assert.deepEqual(repeated, [], 'these paragraphs appear more than once');
});

test('the ordering contract is still stated, because the server does enforce it', () => {
  // Making the gate additive must not soften what IS true: review_code refuses without a passed
  // plan round, and an agent that does not know that meets it as a confusing error.
  // Newline-tolerant: the sentence wraps in the snippet, and asserting its line breaks
  // would make this a test about formatting.
  assert.match(claudeSnippet().replace(/\s+/g, ' '), /REFUSES until a plan round has reached/);
});
