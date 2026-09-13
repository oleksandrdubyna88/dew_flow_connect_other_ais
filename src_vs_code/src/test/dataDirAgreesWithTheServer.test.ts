import assert from 'node:assert/strict';
import { test } from 'node:test';
import { resolve } from 'node:path';
import { coaiDataDir } from '../dataDir';

/** The configured root as the server also resolves it — absolute, and native to this platform. */
const ROOT = resolve('/srv/coai');

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
    assert.equal(coaiDataDir(), ROOT);
  });
});

test('a named side is appended, so two installations on one NAS do not share a token', () => {
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' }, () => {
    assert.equal(coaiDataDir(), `${ROOT}/windows`);
  });
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'wsl' }, () => {
    assert.equal(coaiDataDir(), `${ROOT}/wsl`);
  });
});

test('a side that was asked for and cannot be used is REFUSED, never the shared root', () => {
  // The finding seven reviewers raised on the server's half. Falling back to the root is the
  // feature inverted: `wsl/node1` is a plausible thing to type, and treating it as "no side" puts
  // this installation and every other one on the root's single database.
  for (const side of ['../shared', '..', '.', 'a/b', 'a\\b', 'wsl/node1', 'a:b', 'a b']) {
    withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: side }, () => {
      assert.throws(() => coaiDataDir(), /COAI_DATA_SIDE/u,
        `a side called '${side}' must be refused rather than silently ignored`);
    });
  }
});

test('a side name within the grammar is accepted, and the grammar is the server\'s', () => {
  // An explicit allowlist on both sides, because `Path.GetInvalidFileNameChars()` in C# is
  // platform-dependent — a colon is refused on Windows and accepted on Linux — and the two halves
  // disagreeing on one name means a token written where the server does not read it.
  for (const side of ['windows', 'wsl-ubuntu', 'box_2', 'node.1']) {
    withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: side }, () => {
      assert.equal(coaiDataDir().replaceAll('\\', '/').toLowerCase().endsWith(`/srv/coai/${side}`), true,
        `'${side}' is within the grammar and must resolve under the root`);
    });
  }
});

test('a whitespace-only directory means unset, as it does in the server', () => {
  withEnv({ COAI_DATA_DIR: '   ', COAI_DATA_SIDE: undefined }, () => {
    assert.ok(coaiDataDir().endsWith('coai-mcp'), 'whitespace is not a configured directory');
  });
});

test('the side is lower-cased, as the server lower-cases it', () => {
  // Two halves disagreeing on case is the same silent "not signed in" as disagreeing on the name.
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'Windows' }, () => {
    assert.equal(coaiDataDir(), `${ROOT}/windows`);
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
