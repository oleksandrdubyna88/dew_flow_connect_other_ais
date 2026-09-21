import assert from 'node:assert/strict';
import { test } from 'node:test';
import { join, resolve } from 'node:path';
import { readFileSync } from 'node:fs';
import { coaiDataDir, dataSideName, whereData } from '../dataDir';
import { serverNoticesPath } from '../notificationsFile';

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
    assert.equal(coaiDataDir(), join(ROOT, 'windows'));
  });
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'wsl' }, () => {
    assert.equal(coaiDataDir(), join(ROOT, 'wsl'));
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
    assert.equal(coaiDataDir(), join(ROOT, 'windows'));
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

// ---------- the shared vectors, which are the only thing that can catch a drift ----------

/**
 * The vectors both halves assert, read from the file that sits outside both of them.
 *
 * <p>Each side's own unit tests are self-consistent and therefore blind to a divergence — the
 * failure is only visible from outside, as a token written where the shim does not look or a panel
 * giving migration advice the server's startup log contradicts. Raised by codex and gemini
 * independently on the plan round of this half.</p>
 */
interface Vector {
  readonly why: string;
  readonly dataDir: string;
  readonly dataSide: string;
  readonly dir: string;
  readonly settingsPath: string;
  readonly logsPath: string;
  readonly serverNoticesPath: string;
  readonly refused: boolean;
  readonly rootHasDatabase: boolean;
  readonly dirExists: boolean;
  readonly notes: readonly string[];
}

const VECTORS: readonly Vector[] = JSON.parse(
  readFileSync(resolve(__dirname, '../../..', 'shared/data-side-vectors.json'), 'utf8'),
).vectors;
/**
 * `<root>` and `<default>` stand for paths the two languages spell for themselves.
 *
 * <p>Built with `join`, never by concatenating the vector text: on Windows ROOT is
 * `C:\srv\coai` and appending `/windows` produced a mixed-separator string that the
 * production resolver — and `Path.Combine` on the C# side — never writes. The first version
 * did exactly that and would have failed on the platform this feature is FOR. (codex, code
 * round; the `replaceAll` it also flagged was a no-op.)</p>
 */
function expected(vector: Vector): string {
  return vector.dir === '<default>'
    ? coaiDataDirWithNothingSet()
    : join(ROOT, ...vector.dir.replace('<root>', '').split('/').filter((part) => part.length > 0));
}

function coaiDataDirWithNothingSet(): string {
  let answer = '';
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    answer = coaiDataDir();
  });

  return answer;
}

/**
 * `<root>/windows/settings.json` against the directory this side resolved, built the same way.
 *
 * <p>Through `join` for exactly the reason `expected` gives: on Windows ROOT is `C:\\srv\\coai`
 * and appending `/windows/settings.json` is a mixed-separator string neither production resolver
 * ever writes.</p>
 */
function expectedUnder(vector: Vector, leaf: string): string {
  return join(expected(vector), leaf);
}

test('every shared vector puts the settings file and the logs where the server puts them', () => {
  // Added 2026-09-18. Until then `SettingsFile.DataDirFrom` on the server was a SECOND resolver with
  // no side and no trim, so these two were the only things in the data directory that did not move
  // with the side: two installations sharing one NAS - the whole reason a side exists - shared one
  // settings file and overwrote each other in silence.
  //
  // Asserting the DIRECTORY could not see it, because the directory was already right. The server's
  // own suite asserts these same two fields, which is the only way two implementations are held to
  // each other rather than each to itself.
  // `<root>` in a vector path is the ROOT, not the resolved directory - the resolved one already
  // carries the side, so expanding it here put the side in twice. The first version did exactly
  // that and said so: `C:/srv/coai/windows/windows/settings.json`.
  const asPath = (spelled: string, vector: Vector): string =>
    expectedUnder(vector, spelled.replace(vector.dir, '').split('/')
      .filter((part) => part.length > 0).join('/'));

  for (const vector of VECTORS.filter((one) => !one.refused)) {
    assert.equal(asPath(vector.settingsPath, vector), expectedUnder(vector, 'settings.json'), vector.why);
    assert.equal(asPath(vector.logsPath, vector), expectedUnder(vector, 'logs'), vector.why);
    assert.ok(vector.settingsPath.startsWith(vector.dir + '/'),
      `${vector.settingsPath} does not sit under the directory this case resolved`);
    assert.ok(vector.logsPath.startsWith(vector.dir + '/'),
      `${vector.logsPath} does not sit under the directory this case resolved`);

    // Added with story 1.3 of the server-notices plan. This side has DERIVED this path since
    // 2026-09-17 and has had nothing to read, because nothing writes it; the moment the server
    // does, the two halves have to agree about where — and disagreeing is the silent failure, the
    // server writing while this side reads an empty directory and says nothing.
    //
    // Through `serverNoticesPath`, the function the product actually calls, never a second
    // spelling of the same name in a test.
    assert.equal(
      serverNoticesPath(expected(vector)),
      expectedUnder(vector, 'server-notices.jsonl'),
      vector.why,
    );
    assert.equal(asPath(vector.serverNoticesPath, vector), expectedUnder(vector, 'server-notices.jsonl'), vector.why);
    assert.ok(vector.serverNoticesPath.startsWith(vector.dir + '/'),
      `${vector.serverNoticesPath} does not sit under the directory this case resolved`);
  }
});

test('a refused side names no settings file and no log root', () => {
  // The other direction, and it is not decoration: a fixture that carried a path for a refused side
  // would be describing where data goes for a configuration the product will not start on.
  for (const vector of VECTORS.filter((one) => one.refused)) {
    assert.equal(vector.settingsPath, '', vector.why);
    assert.equal(vector.logsPath, '', vector.why);
    assert.equal(vector.serverNoticesPath, '', vector.why);
  }
});

test('every shared vector resolves to the directory the server resolves', () => {
  for (const vector of VECTORS) {
    withEnv(
      {
        COAI_DATA_DIR: vector.dataDir === '' ? undefined : ROOT,
        COAI_DATA_SIDE: vector.dataSide === '' ? undefined : vector.dataSide,
      },
      () => {
        const where = whereData(() => true);
        if (vector.refused) {
          assert.notEqual(where.refusal, '', vector.why);
          assert.equal(where.directory, '', `${vector.why}: a refused side has no directory to offer`);
          return;
        }
        assert.equal(where.refusal, '', vector.why);
        assert.equal(where.directory, expected(vector), vector.why);
      },
    );
  }
});

test('every shared vector warns about exactly what the server warns about', () => {
  for (const vector of VECTORS) {
    if (vector.refused) {
      continue;
    }
    withEnv(
      {
        COAI_DATA_DIR: vector.dataDir === '' ? undefined : ROOT,
        COAI_DATA_SIDE: vector.dataSide === '' ? undefined : vector.dataSide,
      },
      () => {
        const resolved = expected(vector);
        const where = whereData((path) =>
          path === join(ROOT, 'coai.db') ? vector.rootHasDatabase : path !== resolved || vector.dirExists);

        const kinds = where.notes.map((n) => (n.includes('coai.db') ? 'loose-database' : 'new-directory'));
        assert.deepEqual(kinds, [...vector.notes], vector.why);
      },
    );
  }
});

test('a refused side is a rendered sentence, not a thrown panel', () => {
  // The server refuses to START on this, so the panel is the only place the reason can be read —
  // and a panel that threw would hide it behind a blank section. codex raised the wiring; this is
  // the half of it that lives in the pure function.
  withEnv({ COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'wsl/node1' }, () => {
    const where = whereData(() => true);

    assert.match(where.refusal, /COAI_DATA_SIDE/);
    assert.match(where.refusal, /refuses to start/);
    assert.equal(where.directory, '');
  });
});

test('the side name this window was given is readable on its own', () => {
  withEnv({ COAI_DATA_SIDE: '  Windows  ' }, () => {
    assert.equal(dataSideName(), 'windows', 'trimmed and lower-cased, as the server reads it');
  });
  withEnv({ COAI_DATA_SIDE: undefined }, () => {
    assert.equal(dataSideName(), '');
  });
});
