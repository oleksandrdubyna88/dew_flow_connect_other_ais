import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { test } from 'node:test';

import { NOTHING_CHOSEN, dataDirFinding, namedRoot, useStorageSettings, whereData } from '../dataDir';

/**
 * A NAMED data directory that does not exist is always red (research/PLAN_paths_per_side.md E5.7; decided with the operator,
 * D3): the finding beside `whereData`, pure over the chosen storage and what the disk said about its ROOT.
 */

const ROOT = resolve('/srv/coai');

function withEnv(vars: Record<string, string | undefined>, run: () => void): void {
  const saved: Record<string, string | undefined> = {};
  for (const [name, value] of Object.entries(vars)) {
    saved[name] = process.env[name];
    if (value === undefined) {
      delete process.env[name];
    } else {
      process.env[name] = value;
    }
  }
  try {
    run();
  } finally {
    for (const [name, value] of Object.entries(saved)) {
      if (value === undefined) {
        delete process.env[name];
      } else {
        process.env[name] = value;
      }
    }
  }
}

test('a named root that is not there is a MISSING finding about the data directory, in the error tone', () => {
  const finding = dataDirFinding({ root: ROOT, source: 'this side' }, 'absent', 'windows');

  assert.deepEqual(finding, {
    key: 'coai.dataDirectory', place: 'setup/mcp', value: ROOT, family: 'windows', state: 'missing', owner: '',
  }, 'a named Z:\\coai that does not exist yields no finding today — only the quiet "is not there yet" note');
});

test('the default location is never checked: no finding whatever the disk says, so a first start is never red', () => {
  assert.equal(dataDirFinding(undefined, 'absent'), undefined);
  assert.equal(dataDirFinding(undefined, 'unknown'), undefined);
});

test('a root the disk could not answer for is UNKNOWN — never missing — and one that is there, or not asked yet, is no finding', () => {
  assert.equal(dataDirFinding({ root: ROOT, source: 'environment' }, 'unknown', 'posix')?.state, 'unknown');
  assert.equal(dataDirFinding({ root: ROOT, source: 'environment' }, 'here', 'posix'), undefined);
  assert.equal(dataDirFinding({ root: ROOT, source: 'shared setting' }, 'pending', 'posix'), undefined);
});

test('a missing NAMED root keeps no quiet "is not there yet" note — the mark says it; a missing side folder inside an existing root keeps the note', () => {
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' }, () => {
    const rootGone = whereData(() => false);
    assert.equal(rootGone.notes.some((note) => note.includes('is not there yet')), false,
      'today a missing named root is a quiet note beside the bright mark — the same folder said twice, in two tones');

    const sideGone = whereData((path) => path === ROOT);
    assert.equal(sideGone.notes.some((note) => note.includes('is not there yet')), true,
      'the side folder inside an existing root is created lazily, so its absence stays the quiet note');
  });
});

test('the named root is the layer that answered — the environment, this side or the shared setting — and nothing for the default', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
    try {
      assert.equal(namedRoot(), undefined, 'with nothing named the default location is in use, which is never a named root');
      useStorageSettings({ directory: '/srv/coai', side: 'windows' }, NOTHING_CHOSEN);
      assert.deepEqual(namedRoot(), { root: ROOT, source: 'this side' }, 'the ROOT, never <root>/<side>: the side folder inside an existing root is created lazily');
    } finally {
      useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
    }
  });
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: undefined }, () => {
    assert.deepEqual(namedRoot(), { root: ROOT, source: 'environment' });
  });
});
