import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { collectWithPick, type CollectPorts } from '../bugzCollect';
import type { BugzInputs } from '../bugzPick';
import type { CatalogUse } from '../catalogFields';
import { LOCAL_PRESET, type Vendor } from '../vendors';

/**
 * The collect path, RUN (E5.1's code round, finding 6): the provider's `collectBugs` hands its reads and its two effects
 * to `collectWithPick` as ports, so what it does with a stranded pick, with none and with one that holds is decided by
 * code a test runs — with in-memory ports standing in for the notification and the process — rather than by code only
 * an extension host reaches. What a test cannot reach — the provider building the ports — is pinned by its source below.
 */

function localRow(id: string, model: string, uses: readonly CatalogUse[] = []): Vendor {
  return { ...LOCAL_PRESET, id, model, ...(uses.length === 0 ? {} : { uses }) };
}

function inputs(rows: readonly Vendor[], saved: string): BugzInputs {
  return { rows, saved, engines: {}, serverVendors: ['local'], byRuntime: true };
}

/** Ports that record what the collect did: the sentences it refused with, and the argument lists it started. */
function recording(read: BugzInputs): { ports: CollectPorts; refused: string[]; started: (readonly string[])[] } {
  const refused: string[] = [];
  const started: (readonly string[])[] = [];

  return {
    refused,
    started,
    ports: {
      inputs: () => Promise.resolve(read),
      refuse: (sentence) => { refused.push(sentence); return Promise.resolve(); },
      start: (args) => { started.push(args); return Promise.resolve(); },
    },
  };
}

test('a stranded pick is refused by a sentence naming Models, and NO collect starts', async () => {
  const run = recording(inputs([localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b', ['bugz'])], 'local/qwen3.5'));

  await collectWithPick(run.ports);

  assert.deepEqual(run.started, [], 'a collect started with a pick nobody holds');
  assert.equal(run.refused.length, 1);
  assert.match(run.refused[0]!, /local\/qwen3\.5[\s\S]*Models/u);
});

test('no pick at all is refused, and NO collect starts — never one run unranked', async () => {
  const run = recording(inputs([localRow('local', 'qwen3.5')], ''));

  await collectWithPick(run.ports);

  assert.deepEqual(run.started, [], 'a collect started with no model');
  assert.equal(run.refused.length, 1);
});

test('a pick that holds starts the collect with that model and the row\'s runtime, and refuses nothing', async () => {
  const run = recording(inputs([localRow('local-2', 'gemma4:27b', ['bugz'])], 'local-2/gemma4:27b'));

  await collectWithPick(run.ports);

  assert.deepEqual(run.refused, []);
  assert.deepEqual(run.started, [['--collect-bugs', '--model', 'local-2/gemma4:27b', '--runtime', 'local']]);
});

test('the provider\'s collect goes through collectWithPick: its refusal is the no-ranking-model notice, its start the one spawn', () => {
  // The ports are built in the provider, which only an extension host constructs — so their wiring is pinned by the
  // source: one call into the decision, the refusal routed to `no-ranking-model`, and no spawn of `--collect-bugs`
  // anywhere else in the method.
  const source = readFileSync(join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');
  const method = source.slice(source.indexOf('private async collectBugs('), source.indexOf('private async watchCollect('));

  assert.match(method, /collectWithPick\(\{/u, 'the provider decides the collect somewhere else');
  assert.match(method, /code: 'no-ranking-model'/u, 'the refusal is not the no-ranking-model notice');
  assert.doesNotMatch(method, /collectArgs\(/u, 'the provider builds the arguments itself, past the decision');
});
