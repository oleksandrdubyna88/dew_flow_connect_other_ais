import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { type HostKind, hostKindOf } from '../hostSide';
import { isWsl, namesWsl2 } from '../wslNetwork';

/**
 * ONE rule for which WSL a Linux kernel is, shared with the server (`shared/wsl-banner-vectors.json`, E3 of
 * research/PLAN_kimi_and_vibe_on_linux.md, code gate "one WSL-banner rule for the extension and the server").
 *
 * <p>The rule: the first line of `/proc/version` holding `WSL2` or `microsoft-standard` (any case) is WSL2; else holding
 * `microsoft` or `WSL` is WSL1; else it is no WSL at all. The server asserts its own predicate (`WslGenerations.Of`) against
 * this same file; the extension asserts `namesWsl2`, `isWsl` and `hostKindOf` here. Two self-consistent copies of a rule can
 * disagree, and the one place they would be seen is a person's machine — so both read one table, byte for byte the same file.</p>
 */

interface Vector {
  readonly id: string;
  readonly banner: string;
  readonly generation: 'none' | 'wsl1' | 'wsl2';
  readonly note?: string;
}

// reads-another-program: a positive claim about the shared vectors file the server's tests read too — its shape (id, banner,
// generation) is what this test needs, and a file with another shape fails the length and generation checks below loudly.
const FILE = JSON.parse(readFileSync(join(__dirname, '..', '..', '..', 'shared', 'wsl-banner-vectors.json'), 'utf8')) as { vectors: Vector[] };
const KIND_OF: Readonly<Record<Vector['generation'], HostKind>> = { none: 'linux', wsl1: 'wsl1', wsl2: 'wsl2' };

test('the vectors cover all three generations, so the loop below checks something of each', () => {
  assert.ok(FILE.vectors.length >= 8, `the file holds ${FILE.vectors.length} vectors`);
  for (const generation of ['none', 'wsl1', 'wsl2'] as const) {
    assert.ok(FILE.vectors.some((vector) => vector.generation === generation), `no vector says ${generation}`);
  }
});

for (const vector of FILE.vectors) {
  test(`banner vector ${vector.id}: ${vector.generation}`, () => {
    assert.equal(namesWsl2(vector.banner), vector.generation === 'wsl2', `namesWsl2 for ${vector.id}`);
    assert.equal(isWsl(vector.banner), vector.generation !== 'none', `isWsl for ${vector.id}`);
    assert.equal(hostKindOf('linux', vector.banner), KIND_OF[vector.generation], `hostKindOf for ${vector.id}`);
  });
}
