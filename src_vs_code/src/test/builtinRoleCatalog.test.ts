import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { CONVENTIONS_ID, PROMPTS, ROLES, promptsFor, universalFor } from '../prompts';

/**
 * The panel's catalog against the seed both halves read.
 *
 * <p>This is one half of what replaced a test that parsed C# SOURCE with a regular expression to
 * check that two lists of twenty-five prompts still matched. That test broke on a reformat and was
 * blind to any field it had not been taught. Now each half asserts its own LOADER against
 * `shared/builtin-roles.json`: `BuiltinRoleCatalogTests.cs` for the server's embedded copy, this
 * for the panel's generated one. A seed edit that either side misses goes red on that side, for the
 * reason it actually happened.</p>
 *
 * <p>It is therefore also the test that fails when somebody edits the seed and forgets to run
 * <code>node scripts/generate-builtin-roles.mjs</code>.</p>
 */

interface SeedPrompt {
  readonly id: string;
  readonly label: string;
  readonly purpose: string;
}

interface SeedRole {
  readonly id: string;
  readonly name: string;
  readonly stage: string;
  readonly programmingTask: boolean;
  readonly prompts: readonly SeedPrompt[];
}

// out/test at run time, so three levels reach the repository root — the same walk
// `teamServers.test.ts` makes for its own shared fixture.
const SEED: readonly SeedRole[] = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'builtin-roles.json'), 'utf8'),
).roles;

test('the seed actually loaded', () => {
  // Without this, every assertion below would pass vacuously over an empty list — and a comparison
  // of two empty lists is the most convincing green there is.
  assert.ok(SEED.length >= 5, `the seed has ${SEED.length} roles`);
});

test('the generated catalog is the seed, field for field', () => {
  assert.deepStrictEqual(
    BUILTIN_ROLES.map((r) => ({
      id: r.id,
      name: r.name,
      stage: r.stage,
      programmingTask: r.programmingTask,
      prompts: r.prompts.map((p) => ({ id: p.id, label: p.label, purpose: p.purpose })),
    })),
    SEED.map((r) => ({
      id: r.id,
      name: r.name,
      stage: r.stage,
      programmingTask: r.programmingTask,
      prompts: r.prompts.map((p) => ({ id: p.id, label: p.label, purpose: p.purpose })),
    })),
    'run `node scripts/generate-builtin-roles.mjs` — the generated file is behind the seed',
  );
});

test('the panel keeps its own word for the result stage', () => {
  // The seed says `result` because a later plan gives that stage a document kind and "result" is
  // what both are. Everything already written in the panel says `code`, and this mapping is why
  // none of it had to change when the catalog became generated.
  assert.deepStrictEqual(
    ROLES.map((r) => r.stage),
    SEED.map((r) => (r.stage === 'plan' ? 'plan' : 'code')),
  );
  assert.deepStrictEqual(
    ROLES.map((r) => r.label),
    SEED.map((r) => r.name),
    'a role is drawn under the name the seed gives it',
  );
});

test('a role’s universal prompt is its first one, and there is exactly one', () => {
  for (const role of ROLES) {
    const mine = promptsFor(role.id);
    const universal = mine.filter((p) => p.universal);

    assert.strictEqual(universal.length, 1, `${role.id} has ${universal.length} universal prompts`);
    assert.strictEqual(universal[0]!.id, mine[0]!.id, `${role.id}: the first prompt is the universal one`);
    assert.strictEqual(universalFor(role.id).id, mine[0]!.id);
  }
});

test('every prompt id is unique across the whole catalog', () => {
  // One prompt id names one text for the whole product — it is the file name under
  // `<dataDir>/prompts/`, so two roles claiming one id would be two roles sharing one override.
  // The server refuses a seed that breaks this; the panel would silently draw the same text twice.
  const ids = PROMPTS.map((p) => p.id);

  assert.deepStrictEqual([...new Set(ids)].length, ids.length, `duplicate prompt id in ${ids.join(', ')}`);
});

test('the conventions prompt belongs to the conventions role and to nothing else', () => {
  const owners = PROMPTS.filter((p) => p.id === CONVENTIONS_ID).map((p) => p.role);

  assert.deepStrictEqual(owners, ['Conventions']);
});
