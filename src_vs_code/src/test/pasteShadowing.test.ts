import assert from 'node:assert/strict';
import { test } from 'node:test';
import { clientTargetsLine, CLIENT_TARGETS } from '../mcpBlock';

/**
 * A paste target that can be silently overruled has to say so.
 *
 * <p>Claude Code reads `~/.claude.json` at two levels: a top-level `mcpServers` object (user
 * scope) and a per-project one under `projects["…"].mcpServers` (local scope). The project entry
 * WINS. Somebody who follows this extension's instruction, pastes at the top level and restarts
 * gets no signal at all that their paste was read and outranked — the file contains both, and
 * nothing in it says which one ran.</p>
 *
 * <p>Reported from a macOS checkout, where it cost an hour on top of a separate defect. The fix is
 * a sentence, and this is what keeps the sentence there.</p>
 */

test('the ~/.claude.json target warns that a project entry outranks it', () => {
  const claudeCode = CLIENT_TARGETS.find((t) => t.path === '~/.claude.json');

  assert.ok(claudeCode !== undefined, 'the Claude Code target must still exist');
  assert.match(
    claudeCode.note,
    /projects/,
    'the note has to name the per-project entry, because that is where somebody must look',
  );
  assert.match(claudeCode.note, /precedence|takes precedence|wins/i, 'and say which one wins');
});

test('a target with nothing to warn about carries no note, so the line stays readable', () => {
  for (const target of CLIENT_TARGETS.filter((t) => t.path !== '~/.claude.json')) {
    assert.equal(target.note, '', `${target.path} has no shadowing problem and must not invent one`);
  }
});

test('the line a person actually reads carries the caveat', () => {
  const line = clientTargetsLine(CLIENT_TARGETS);

  assert.ok(line.includes('~/.claude.json'), 'every path is still offered');
  assert.ok(line.includes('.vscode/mcp.json'), 'every path is still offered');
  assert.ok(
    line.includes('projects'),
    'the warning must survive into the message — a note nobody renders is a comment',
  );
});

test('the three targets are still the three files a person would edit', () => {
  assert.deepEqual(
    CLIENT_TARGETS.map((t) => t.path),
    ['~/.claude.json', '<project>/.mcp.json', '.vscode/mcp.json'],
  );
});
