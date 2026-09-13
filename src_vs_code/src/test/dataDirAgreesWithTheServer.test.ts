import assert from 'node:assert/strict';
import { test } from 'node:test';
import { coaiDataDir } from '../dataDir';

/**
 * The extension resolves the data directory exactly as `coai-mcp` does.
 *
 * <p>This is the seam `dataDir.ts` has always warned about in its own docstring: the EXTENSION
 * writes the Team-server token into this directory and the MCP shim READS it, so a divergence here
 * is a silent "not signed in" — the token is written somewhere nobody looks.</p>
 *
 * <p>Issue #115 added a way to partition a chosen directory per side, so Windows and WSL can be
 * pointed at one NAS without writing the same SQLite file. That rule is `PanelSettings` in C#, and
 * these are the cases where this half must agree with it.</p>
 *
 * <p>The side is a NAME rather than something derived, and that is what makes agreement possible at
 * all: deriving it would mean computing one string twice — `os.hostname()` here,
 * `Environment.MachineName` there — and those differ in case and in whether they carry a domain.</p>
 */

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

test('a chosen directory with no side named is used exactly as chosen', () => {
  // The partition is opt-in on both halves. Anyone who set COAI_DATA_DIR before this existed keeps
  // the directory they set, which is the promise the server makes too.
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: undefined }, () => {
    assert.equal(coaiDataDir(), '/srv/coai');
  });
});

test('a named side is appended, so two installations on one NAS do not share a token', () => {
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' }, () => {
    assert.equal(coaiDataDir(), '/srv/coai/windows');
  });
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'wsl' }, () => {
    assert.equal(coaiDataDir(), '/srv/coai/wsl');
  });
});

test('a side name that could leave the directory is refused, not rewritten', () => {
  // Rewriting is the dangerous repair: two different names sanitised into one would put two
  // installations back on one database, which is what the partition exists to prevent.
  for (const side of ['../shared', '..', '.', 'a/b', 'a\\b', '   ']) {
    withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: side }, () => {
      assert.equal(coaiDataDir(), '/srv/coai', `a side called '${side}' must not escape the root`);
    });
  }
});

test('the side is lower-cased, as the server lower-cases it', () => {
  // Two halves disagreeing on case is the same silent "not signed in" as disagreeing on the name.
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'Windows' }, () => {
    assert.equal(coaiDataDir(), '/srv/coai/windows');
  });
});

test('with nothing configured the default is untouched', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: 'windows' }, () => {
    // A side without a chosen directory partitions nothing: the default is per-platform already,
    // and moving somebody's existing history is exactly what this feature promises not to do.
    assert.ok(!coaiDataDir().endsWith('/windows'), 'the default must not grow a side');
    assert.ok(coaiDataDir().endsWith('coai-mcp'));
  });
});
