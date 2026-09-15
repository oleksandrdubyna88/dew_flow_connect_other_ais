import assert from 'node:assert/strict';
import { test } from 'node:test';
import { join, resolve } from 'node:path';
import {
  NOTHING_CHOSEN,
  chooseStorage,
  coaiDataDir,
  serverEnv,
  useStorageSettings,
  whereData,
} from '../dataDir';
import { serverRun } from '../roundsDbRead';

/**
 * The directory a person CHOSE, as opposed to one they exported into a shell.
 *
 * <p><b>Why there are layers at all.</b> `COAI_DATA_DIR` reaches the MCP server through the client
 * entry that spawns it; it does not reach a VS Code window, which has no such variable. So a choice
 * made in the panel has to be persisted where the extension host can read it — and the day it is,
 * there are two places an answer can come from and a person is entitled to know which one answered.
 * A panel reading `%LOCALAPPDATA%` while the server writes to a NAS is the exact state the storage
 * section exists to diagnose; this is the machinery that stops it being the NORMAL state for
 * everybody who uses the feature.</p>
 *
 * <p><b>The pair travels together.</b> A side partitions a directory, so the layer that names the
 * DIRECTORY names the side as well. Taking a directory from one layer and a side from another would
 * compose a path nobody configured — and the failure is silent, because the path exists and is
 * simply empty.</p>
 */

const ROOT = resolve('/srv/coai');
const NAS = resolve('/mnt/nas/coai');

/** Both settings layers, put back afterwards, so one test cannot decide the next one's answer. */
function withChoice(chosen: { directory: string; side: string }, shared: { directory: string; side: string }, run: () => void): void {
  useStorageSettings(chosen, shared);
  try {
    run();
  } finally {
    useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  }
}

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

const NOTHING = NOTHING_CHOSEN;

// ---------- the order the layers are asked in ----------

test('the environment beats a directory this side chose', () => {
  // A window launched from a shell that exports the variable should agree with a server launched
  // from that same shell — and it mirrors the server's own rule, where a variable beats the file.
  const chosen = chooseStorage({ directory: ROOT, side: 'windows' }, { directory: NAS, side: 'wsl' }, NOTHING);

  assert.equal(chosen.directory, ROOT);
  assert.equal(chosen.side, 'windows');
  assert.equal(chosen.source, 'environment');
});

test('a directory this side chose beats the shared setting', () => {
  const chosen = chooseStorage(NOTHING, { directory: NAS, side: 'wsl' }, { directory: ROOT, side: 'windows' });

  assert.equal(chosen.directory, NAS);
  assert.equal(chosen.side, 'wsl');
  assert.equal(chosen.source, 'this side');
});

test('the shared setting answers when this side has chosen nothing', () => {
  const chosen = chooseStorage(NOTHING, NOTHING, { directory: ROOT, side: 'windows' });

  assert.equal(chosen.directory, ROOT);
  assert.equal(chosen.source, 'shared setting');
});

test('with nothing named anywhere it is the platform default', () => {
  const chosen = chooseStorage(NOTHING, NOTHING, NOTHING);

  assert.equal(chosen.directory, '');
  assert.equal(chosen.source, 'default', 'an empty directory means the default, and says so');
});

// ---------- the pair travels together ----------

test('a side never partitions a directory named by a different layer', () => {
  // The silent one: `<nas>/windows` would exist, be empty, and be nobody's configuration.
  const chosen = chooseStorage({ directory: '', side: 'windows' }, { directory: NAS, side: '' }, NOTHING);

  assert.equal(chosen.directory, NAS);
  assert.equal(chosen.side, '', 'the side belongs to the layer that named the directory');
});

test('a side named while no layer names a directory is reported as doing nothing', () => {
  const chosen = chooseStorage({ directory: '', side: 'windows' }, NOTHING, NOTHING);

  assert.equal(chosen.side, '');
  assert.equal(chosen.ignoredSide, 'windows', 'a setting somebody made on purpose that has no effect');
  assert.equal(chosen.source, 'default');
});

// ---------- what the rest of the window reads ----------

test('a chosen directory is what this window resolves, with no variable set', () => {
  // The whole feature in one assertion: no environment, and the window still reads the NAS.
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'windows' }, NOTHING, () => {
      assert.equal(coaiDataDir(), join(NAS, 'windows'));
    });
  });
});

test('and the panel says which layer answered', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'windows' }, NOTHING, () => {
      const where = whereData(() => true);

      assert.equal(where.directory, join(NAS, 'windows'));
      assert.equal(where.side, 'windows');
      assert.equal(where.source, 'this side');
      assert.equal(where.refusal, '');
    });
  });
});

test('a refused side is refused wherever it was named', () => {
  // The grammar is the server's, and a side it will not start on must not be guessed at here either
  // — whether it arrived in a variable or in a setting.
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'wsl/node1' }, NOTHING, () => {
      const where = whereData(() => true);

      assert.match(where.refusal, /COAI_DATA_SIDE/);
      assert.equal(where.directory, '', 'no path is offered for a side the server refuses');
    });
  });
});

// ---------- what a server has to be told ----------

test('a server is handed the two keys, and only those two', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'windows' }, NOTHING, () => {
      assert.deepEqual(serverEnv(), { COAI_DATA_DIR: NAS, COAI_DATA_SIDE: 'windows' });
    });
  });
});

test('the root is handed over, never the resolved path', () => {
  // Handing `<nas>/windows` as COAI_DATA_DIR while also naming the side would resolve to
  // `<nas>/windows/windows` on the server. The root is what the layer named.
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'windows' }, NOTHING, () => {
      assert.equal(serverEnv()['COAI_DATA_DIR'], NAS);
      assert.notEqual(serverEnv()['COAI_DATA_DIR'], coaiDataDir());
    });
  });
});

test('a directory with no side offers no side key to paste', () => {
  // An empty `COAI_DATA_SIDE` in a client entry means exactly what no key means, and a person
  // reading their own config is owed neither the question nor the answer.
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: '' }, NOTHING, () => {
      assert.deepEqual(serverEnv(), { COAI_DATA_DIR: NAS });
    });
  });
});

test('the default directory needs no variables at all', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice(NOTHING, NOTHING, () => {
      assert.deepEqual(serverEnv(), {}, 'a client entry without either key reads the same place');
    });
  });
});

test('a refused side offers no variables to paste', () => {
  withEnv({ COAI_DATA_DIR: undefined, COAI_DATA_SIDE: undefined }, () => {
    withChoice({ directory: NAS, side: 'a:b' }, NOTHING, () => {
      assert.deepEqual(serverEnv(), {}, 'there is no configuration worth handing to a server here');
    });
  });
});

// ---------- and it reaches the process that reads the database ----------

test('a spawned read of the database is given the directory this window resolved', async () => {
  // The other half of the same defect, and the one nothing else would catch: the rounds list is
  // drawn by SPAWNING the server binary, which resolves its own directory from its own environment.
  // A child that inherits this window's environment — which has no COAI_DATA_DIR, because the choice
  // is a setting — asks the default directory about a history on a NAS and is told there is none.
  //
  // `process.execPath` stands in for the binary: node is certainly present, since it is running this.
  useStorageSettings({ directory: NAS, side: 'windows' }, NOTHING);
  const saved = { dir: process.env['COAI_DATA_DIR'], side: process.env['COAI_DATA_SIDE'] };
  delete process.env['COAI_DATA_DIR'];
  delete process.env['COAI_DATA_SIDE'];

  try {
    const answer = await serverRun(process.execPath)(
      ['-e', 'process.stdout.write(`${process.env.COAI_DATA_DIR}|${process.env.COAI_DATA_SIDE}`)'],
      10_000);

    assert.equal(answer.code, 0);
    assert.equal(answer.output, `${NAS}|windows`);
  } finally {
    useStorageSettings(NOTHING, NOTHING);
    if (saved.dir !== undefined) { process.env['COAI_DATA_DIR'] = saved.dir; }
    if (saved.side !== undefined) { process.env['COAI_DATA_SIDE'] = saved.side; }
  }
});

// ---------- the layer below stays exactly as it was ----------

test('with no setting anywhere, the environment answers as it always has', () => {
  // The shared vectors in `dataDirAgreesWithTheServer.test.ts` assert this half against the C# rule.
  // What is checked here is that adding the settings layers did not move it.
  withChoice(NOTHING, NOTHING, () => {
    withEnv({ COAI_DATA_DIR: ROOT, COAI_DATA_SIDE: 'windows' }, () => {
      assert.equal(coaiDataDir(), join(ROOT, 'windows'));
      assert.equal(whereData(() => true).source, 'environment');
    });
  });
});
