import * as assert from 'node:assert';
import { test } from 'node:test';
import { RUNTIMES, modelsFor } from '../models';
import { vendorsFrom } from '../vendors';

/**
 * A Team-server reviewer row, from `settings.json` to the dropdown it offers.
 *
 * This is the ground two separate defects have already been found on: a runtime added to the type
 * and not to the parser turned every `local` row into a codex one, silently, under its own name —
 * once in the extension and once in the MCP server, days apart.
 */

test('remote is a runtime this build knows, so a saved row survives being read', () => {
  const [vendor] = vendorsFrom([
    {
      id: 'remsoft-dev-codex',
      runtime: 'remote',
      model: 'gpt-5.6',
      baseUrl: 'https://coai.example.com',
      remoteVendor: 'codex',
      enabled: true,
    },
  ]);

  assert.strictEqual(vendor?.runtime, 'remote', 'a row that arrives as codex would run the wrong thing');
  assert.strictEqual(vendor?.baseUrl, 'https://coai.example.com');
  assert.strictEqual(vendor?.remoteVendor, 'codex');
});

test('every runtime the type knows survives the parser', () => {
  // The guard against a third copy of the list: add a runtime and this fails until the parser
  // accepts it, whatever anybody remembered to update.
  for (const runtime of RUNTIMES) {
    // `gemini` is the one exception, and it is deliberate rather than a gap: Google retired Code
    // Assist for individual accounts, so a saved gemini row is MIGRATED to the CLI Google pointed
    // at. `runtimeSurvives.test.ts` owns that behaviour.
    if (runtime === 'gemini') {
      continue;
    }

    const [vendor] = vendorsFrom([{ id: 'v', runtime, model: 'm', enabled: true }]);
    assert.strictEqual(vendor?.runtime, runtime, `${runtime} did not survive`);
  }
});

test('a row with no server vendor keeps none, rather than gaining an empty one', () => {
  // `coai.vendors` is JSON a person reads and edits; a `"remoteVendor": ""` on every codex row is
  // noise that means nothing.
  const [vendor] = vendorsFrom([{ id: 'codex', runtime: 'codex', model: 'm' }]);

  assert.ok(!('remoteVendor' in (vendor as object)));
});

test('the model list for a remote row is the server’s allowlist, not a curated one', () => {
  const offered = modelsFor('remote', [], '', undefined, [], ['gpt-5.6', 'gpt-5.6-mini']);

  assert.deepStrictEqual(offered.map((m) => m.id), ['gpt-5.6', 'gpt-5.6-mini']);
});

test('a saved model the server no longer offers is KEPT and marked', () => {
  // Dropping it would silently switch the reviewer to another model; showing it plainly would let a
  // round be sent for one the server will refuse. The same rule a local engine's list follows.
  const offered = modelsFor('remote', [], 'gpt-5.5-retired', undefined, [], ['gpt-5.6']);

  assert.strictEqual(offered[0]?.id, 'gpt-5.5-retired');
  assert.ok(offered[0]?.label.includes('does not offer it any more'));
  assert.strictEqual(offered[1]?.id, 'gpt-5.6');
});

test('a server that has not been asked yet offers nothing rather than a plausible default', () => {
  // A guessed list is worse than an empty one: it would let somebody pick a model this server was
  // never going to accept.
  assert.deepStrictEqual(modelsFor('remote', [], '', undefined, [], []), []);
});
