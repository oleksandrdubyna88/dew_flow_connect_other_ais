import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';

import { spelledForTheOtherOs } from '../pathFamily';

/**
 * The TS half of `shared/path-family-vectors.json` — which roots are the OTHER operating system's. The server skips
 * exactly those (`QuestionRoots.OtherSide`, answered by `QuestionConsultSettingsTests.cs`); the Settings page says
 * "the other side's" beside exactly those. Two self-consistent loaders cannot notice they disagree, so both answer one
 * file. The JSON is validated into shape rather than cast.
 */

interface Vector {
  readonly path: string;
  readonly onWindows: boolean;
  readonly elsewhere: boolean;
  readonly why: string;
}

function vectors(): readonly Vector[] {
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const rows: unknown = (JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>)['vectors'];
  assert.ok(Array.isArray(rows), 'shared/path-family-vectors.json carries no `vectors` array');

  return rows.map((row: unknown, at: number) => {
    const one = row as Record<string, unknown>;
    assert.equal(typeof one['path'], 'string', `vector ${at} has no path`);
    assert.equal(typeof one['onWindows'], 'boolean', `vector ${at} has no Windows verdict`);
    assert.equal(typeof one['elsewhere'], 'boolean', `vector ${at} has no verdict for Linux, WSL or macOS`);

    return { path: one['path'] as string, onWindows: one['onWindows'] as boolean, elsewhere: one['elsewhere'] as boolean, why: String(one['why'] ?? '') };
  });
}

test('which roots are the other side\'s answers the shared vectors, on both platforms, as the server does', () => {
  const all = vectors();
  assert.ok(all.length > 5, 'the file is read, not an empty array');
  assert.ok(all.some((v) => v.onWindows) && all.some((v) => v.elsewhere), 'both directions are in the file');

  for (const vector of all) {
    assert.equal(spelledForTheOtherOs(vector.path, true), vector.onWindows, `on Windows: ${vector.path} — ${vector.why}`);
    assert.equal(spelledForTheOtherOs(vector.path, false), vector.elsewhere, `elsewhere: ${vector.path} — ${vector.why}`);
  }
});
