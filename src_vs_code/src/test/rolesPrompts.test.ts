import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { promptFile, promptsDir } from '../rolesPrompts';

/**
 * The path a prompt's text is written to — the one the SERVER reads.
 *
 * <p>Neither half can see the other's spelling of it, which is what makes this worth its own test:
 * the extension writes and `RolePrompts` reads, and a divergence would be a prompt a person wrote
 * that no round ever asks. The shape is the same one the Team-server token file already has.</p>
 */

test('a prompt goes where the server reads overrides from', () => {
  assert.strictEqual(promptsDir('/data'), '/data/prompts');
  assert.strictEqual(promptFile('/data', 'architecture'), '/data/prompts/architecture.md');
});

test('every prompt this product ships has a path, so any of them can be rewritten', () => {
  for (const role of BUILTIN_ROLES) {
    for (const prompt of role.prompts) {
      assert.strictEqual(promptFile('/data', prompt.id), `/data/prompts/${prompt.id}.md`, prompt.id);
    }
  }
});

test('an id that is not a slug never becomes a path', () => {
  // It reaches a path HERE, so this refuses it here — not because the page or the server would let
  // one through, but because a rule held up only by another rule is one rename from neither.
  for (const id of ['../../escaped', 'Architecture', 'two words', '-leading', 'trailing/', '', 'a\\b']) {
    assert.strictEqual(promptFile('/data', id), undefined, id);
  }
});

test('a name Windows reserves for a device is refused too', () => {
  // It would compose, be accepted, and then fail to have its text written on one operating system
  // out of three — which is the worst of the three possible outcomes.
  for (const id of ['con', 'nul', 'com1', 'lpt9']) {
    assert.strictEqual(promptFile('/data', id), undefined, id);
  }
});

test('nothing is returned rather than something sanitised', () => {
  // A caller holding a refused id is a caller whose id came from somewhere it should not have.
  // Quietly writing `....escaped.md` would hide that, and the file would be read by nobody.
  assert.strictEqual(promptFile('/data', '../../secrets'), undefined);
});
