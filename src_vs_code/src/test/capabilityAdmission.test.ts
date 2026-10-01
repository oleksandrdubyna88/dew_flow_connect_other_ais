import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { admit, CAPABILITIES, isCapability, STANDINGS } from '../capabilityAdmission';
import { RUNTIME_CAPABILITIES, RUNTIMES } from '../runtimeCapabilities.generated';

/**
 * The TS half of `shared/capability-matrix-vectors.json` — the extension's admission over the same
 * table the server embeds (PLAN_question_consultant.md, D3; S1 acceptance 1).
 *
 * <p>A3 says an incompatible model + prompt pair is blocked in the UI AND refused by the server. That is
 * one rule only while both halves answer one set of vectors: two self-consistent loaders cannot notice
 * that they disagree, and the pair the panel lets a person save is then the pair the server never
 * launches, with nothing on either side saying so. `CapabilityMatrixTests.cs` is the other half.</p>
 *
 * <p>The JSON is VALIDATED into shape rather than cast: `JSON.parse(...) as Vector[]` is an `as` standing
 * in for a real type, and a fixture that lost its `admitted` field would read as `undefined` and quietly
 * compare unequal to `false`.</p>
 */

interface Vector {
  readonly runtime: string;
  readonly capability: string;
  readonly admitted: boolean;
  readonly standing: string;
  readonly flag: string;
  readonly why: string;
}

interface SeedRow {
  readonly runtime: string;
  readonly capability: string;
  readonly standing: string;
  readonly measuredWith: {
    readonly cli: string;
    readonly version: string;
    readonly date: string;
    readonly resultRef: string;
    readonly cells: readonly string[];
    readonly note: string;
  };
}

// out/test at run time, so three levels reach the repository root.
const shared = (file: string): unknown =>
  JSON.parse(readFileSync(join(__dirname, '..', '..', '..', 'shared', file), 'utf8'));

function field(row: Record<string, unknown>, name: string, where: string): string {
  const value = row[name];

  assert.equal(typeof value, 'string', `${where} has no string ${name}`);

  return value as string;
}

function vectors(): readonly Vector[] {
  const rows: unknown = (shared('capability-matrix-vectors.json') as Record<string, unknown>)['vectors'];

  assert.ok(Array.isArray(rows), 'shared/capability-matrix-vectors.json carries no `vectors` array');

  return rows.map((row: unknown, at: number) => {
    const one = row as Record<string, unknown>;
    const where = `vector ${at}`;

    assert.equal(typeof one['admitted'], 'boolean', `${where} has no admitted verdict`);

    return {
      runtime: field(one, 'runtime', where),
      capability: field(one, 'capability', where),
      admitted: one['admitted'] as boolean,
      standing: field(one, 'standing', where),
      flag: field(one, 'flag', where),
      why: field(one, 'why', where),
    };
  });
}

function seed(): readonly SeedRow[] {
  const rows: unknown = (shared('runtime-capabilities.json') as Record<string, unknown>)['rows'];

  assert.ok(Array.isArray(rows), 'shared/runtime-capabilities.json carries no `rows` array');

  return rows.map((row: unknown, at: number) => {
    const one = row as Record<string, unknown>;
    const measured = one['measuredWith'] as Record<string, unknown>;
    const where = `row ${at}`;
    const cells: unknown = measured['cells'];

    assert.ok(Array.isArray(cells), `${where} has no cells array`);

    return {
      runtime: field(one, 'runtime', where),
      capability: field(one, 'capability', where),
      standing: field(one, 'standing', where),
      measuredWith: {
        cli: field(measured, 'cli', where),
        version: field(measured, 'version', where),
        date: field(measured, 'date', where),
        resultRef: field(measured, 'resultRef', where),
        cells: cells.map((cell: unknown) => String(cell)),
        note: field(measured, 'note', where),
      },
    };
  });
}

const VECTORS = vectors();
const SEED = seed();

test('the vectors and the seed actually loaded', () => {
  // A comparison of two empty lists is the most convincing green there is.
  assert.ok(VECTORS.length > 15, `fifteen pairs plus the refusals by name, got ${VECTORS.length}`);
  assert.equal(SEED.length, RUNTIMES.length * CAPABILITIES.length, 'one row per runtime × capability');
});

test('the generated table is the seed, row for row and field for field', () => {
  assert.deepStrictEqual(
    RUNTIME_CAPABILITIES.map((r) => ({ ...r, measuredWith: { ...r.measuredWith, cells: [...r.measuredWith.cells] } })),
    SEED.map((r) => ({ ...r, measuredWith: { ...r.measuredWith, cells: [...r.measuredWith.cells] } })),
    'run `node src_vs_code/scripts/generate-runtime-capabilities.mjs` — the generated file is behind the seed',
  );
});

for (const vector of VECTORS) {
  test(`${vector.runtime} × ${vector.capability}: ${vector.admitted ? 'admitted' : 'refused'} — ${vector.why}`, () => {
    const admission = admit(vector.runtime, vector.capability);

    assert.equal(admission.admitted, vector.admitted, admission.reason);
    assert.equal(admission.standing, vector.standing, 'the row the decision was made from');
    assert.equal(admission.flag, vector.flag, 'D13: unconfined and default-deny are flagged, nothing else is');
    if (!vector.admitted) {
      assert.ok(admission.reason.includes(vector.runtime) && admission.reason.includes(vector.capability),
        `a refusal names the pair it refused: ${admission.reason}`);
    }
  });
}

test('the words are the file’s words, and a word outside them is refused rather than defaulted', () => {
  assert.deepStrictEqual([...CAPABILITIES], ['none', 'disk', 'web']);
  assert.deepStrictEqual([...STANDINGS], ['confined', 'unconfined', 'default-deny', 'unsupported', 'unmeasured']);
  assert.deepStrictEqual([...RUNTIMES], ['claude', 'codex', 'antigravity', 'local', 'api']);
  assert.equal(isCapability('disk'), true);
  assert.equal(isCapability('Disk'), false, 'the panel writes the file’s spelling, lower case, and nothing else');
  assert.equal(isCapability('shell'), false);
  for (const row of RUNTIME_CAPABILITIES) {
    assert.ok(STANDINGS.includes(row.standing), `${row.runtime} × ${row.capability}: '${row.standing}' is not a standing`);
    assert.ok(isCapability(row.capability), `${row.runtime}: '${row.capability}' is not a capability`);
  }
});

test('a refusal says what cures it: a measurement for an unmeasured pair, another prompt for an unsupported one', () => {
  assert.match(admit('api', 'web').reason, /not been measured/u);
  assert.match(admit('api', 'web').reason, /RESULTS_question_consultant_capabilities\.md/u);
  assert.match(admit('antigravity', 'web').reason, /cannot fetch a page headless/u);
  assert.match(admit('gemini', 'none').reason, /claude, codex, antigravity, local, api/u);
  assert.match(admit('claude', 'shell').reason, /none, disk, web/u);
});

test('a flagged admission carries the caveat a person must acknowledge (D13)', () => {
  assert.match(admit('codex', 'web').caveat, /can read this machine/u);
  assert.match(admit('antigravity', 'disk').caveat, /headless/u);
  assert.equal(admit('claude', 'web').caveat, '', 'a confined pair has nothing to acknowledge');
});
