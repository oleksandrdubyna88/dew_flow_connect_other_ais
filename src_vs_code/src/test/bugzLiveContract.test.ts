import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawnSync } from 'node:child_process';

import { parseBugs } from '../roundsDb';
import { readPairs } from '../roundsDbRead';
import { mayRank } from '../bugzView';
import { EXITS, outcomeOf, readSummary } from '../bugsSend';

/**
 * The REAL binary's answer, parsed by the REAL reader.
 *
 * <p><b>Why hand-written JSON was not enough.</b> Every other test of this contract feeds `parseBugs`
 * a fixture somebody typed, so both sides can agree perfectly with each other and disagree with what
 * `coai-mcp` actually prints. A field renamed on the server — `unprocessed` to something else — would
 * leave `parseBugs` spreading its defaults and returning `read: true`, and the panel would show a
 * corpus of zero candidates with a Collect button that looks fine. This product has shipped its two
 * halves out of step before; a contract nothing exercises end to end is a contract nobody is holding.
 * (Code round, codex, twice.)</p>
 *
 * <p><b>It skips when the binary is not built</b> rather than failing. The extension suite must run in
 * a checkout where nobody has built the server — that is an ordinary state, not a defect — and a test
 * that goes red for it would be a test people learn to ignore. When the binary IS there, which is
 * every CI run and every local run after a build, it is exercised.</p>
 */

/** The debug build, beside this package. */
function server(): string {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';

  return path.join(process.cwd(), '..', 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);
}

const built = fs.existsSync(server());

test('the real --bugs-json output parses, and says what the panel needs', { skip: built ? false : 'the server is not built' }, () => {
  // A data directory of its own: this asks the binary a question, and it must not be answered from
  // whatever database the person running the suite happens to have.
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-contract-'));
  const answer = spawnSync(server(), ['--bugs-json'], {
    encoding: 'utf8',
    env: { ...process.env, COAI_DATA_DIR: data },
    timeout: 60_000,
  });

  assert.equal(answer.status, 0, `--bugs-json exited ${answer.status}: ${answer.stderr}`);

  const corpus = parseBugs(answer.stdout);

  assert.ok(corpus.read, 'the panel could not understand what the server printed');

  // The fields the section actually renders. Named one by one rather than deep-equalled, because a
  // field ADDED to the server is not a breakage and must not fail this.
  assert.equal(typeof corpus.funnel.unprocessed, 'number', 'the waiting count drives the first line');
  assert.equal(typeof corpus.funnel.collected, 'number', 'and the corpus size gates the Review button');
  assert.equal(typeof corpus.lastRun.state, 'string');
  assert.equal(typeof corpus.lastRun.collected, 'number');
  assert.equal(corpus.lastRun.id, '', 'a fresh data directory has no runs');

  // The allowlist rides along, and what it says must be usable by the picker's own filter.
  assert.ok(corpus.rankingVendors.length > 0, 'the server should say which vendors may rank');
  for (const vendor of corpus.rankingVendors) {
    assert.ok(mayRank(`${vendor}/some-model`),
      `the server allows '${vendor}' but the panel would filter it out`);
  }

  fs.rmSync(data, { recursive: true, force: true });
});

/**
 * And the REAL summary, read by the REAL reader.
 *
 * <p>The same argument as the test above, for the other half of the same boundary: every other test
 * of the send feeds `readSummary` a fixture somebody typed, so both sides can agree perfectly with
 * each other and disagree with what `coai-mcp` actually prints. A property renamed in `UploadSummary`
 * would leave the TypeScript spreading its defaults and the section reporting "0 sent" for a send
 * that worked. A plan reviewer named exactly this, and this product has shipped its two halves out
 * of step before.</p>
 *
 * <p><b>A fresh data directory sends nothing</b>, which is the point: the run still prints its
 * summary, so the CONTRACT is exercised without a server, a key, or a pair. The address is one that
 * cannot resolve and is never reached — the run ends before it would need to.</p>
 */
test('the real --upload-pairs summary is the one the panel reads', { skip: built ? false : 'the server is not built' }, () => {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-send-contract-'));
  const answer = spawnSync(server(), ['--upload-pairs', '--server', 'https://bugs.invalid'], {
    encoding: 'utf8',
    // The key is never an argument, here least of all: this is the contract test for the path that
    // carries one.
    env: { ...process.env, COAI_DATA_DIR: data, COAI_BUGS_KEY: 'a-key-for-a-run-that-sends-nothing' },
    timeout: 60_000,
  });

  assert.equal(answer.status, EXITS.fine,
    `--upload-pairs exited ${answer.status}: ${answer.stderr}`);

  const summary = readSummary(answer.stdout);

  assert.ok(summary !== undefined,
    `the panel could not read what the server printed: ${JSON.stringify(answer.stdout)}`);
  // Named one by one rather than deep-equalled: a field ADDED to the server is not a breakage.
  assert.equal(typeof summary.offered, 'number', 'the denominator the button shows');
  assert.equal(typeof summary.accepted, 'number', 'what "sent" means in the section');
  assert.equal(typeof summary.duplicate, 'number', 'already held is a success and is counted apart');
  assert.equal(typeof summary.refused, 'number');
  assert.equal(typeof summary.trouble, 'string', 'the sentence the failure face renders');
  assert.equal(summary.offered, 0, 'a fresh data directory has nothing to send');

  // And the whole outcome, so the words a person reads are produced from the real thing.
  const outcome = outcomeOf(answer.status ?? 0, answer.stdout);
  assert.equal(outcome.kind, 'sent');
  assert.match(outcome.said, /Nothing was waiting/u);

  fs.rmSync(data, { recursive: true, force: true });
});

/** An old server exits 64, and that must read as "update it", never as "your pairs were refused". */
test('an unknown mode really does exit 64', { skip: built ? false : 'the server is not built' }, () => {
  const answer = spawnSync(server(), ['--send-the-pairs-please'], { encoding: 'utf8', timeout: 60_000 });

  assert.equal(answer.status, EXITS.tooOld,
    'the extension maps 64 onto "this machine\u2019s coai-mcp is older than sending"');
  assert.equal(outcomeOf(answer.status ?? 0, answer.stdout).kind, 'too-old');
});

/**
 * The REAL `--pairs-json` output, parsed by the REAL `readPairs` — story 2.1's own contract.
 *
 * <p>Two plan reviewers, on two providers, named the same risk and it is the one this file exists
 * for: `ReviewPair` is written twice, once as a C# record and once as a TypeScript interface, and
 * every other test of story 2.1 feeds one side a fixture the other side never produced. A field
 * spelled `headSha` on one side and `head_sha` on the other leaves both suites green while the page
 * renders four honest-looking absences — which is indistinguishable from an older server, and so
 * cannot even be noticed by looking.</p>
 *
 * <p>An EMPTY corpus is the right fixture here. What is being checked is the SHAPE of the envelope
 * and the reader's willingness to accept it; the field names are checked against a populated
 * database by the server's own `ThePairsThemselvesTests`, which can write rows this side cannot.
 * What only this test can see is that the two halves agree the answer is an object with `items`,
 * and that `readPairs` reports success rather than "the pairs could not be read".</p>
 */
test('the real --pairs-json envelope is the one readPairs accepts',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-pairs-'));
    try {
      const answer = await readPairs(server(), 5, async (args) => {
        const ran = spawnSync(server(), args, {
          encoding: 'utf8',
          env: { ...process.env, COAI_DATA_DIR: data },
          timeout: 60_000,
        });

        return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
      });

      assert.equal(answer.ok, true,
        `the reader refused the real binary's own output: ${answer.ok ? '' : answer.why}`);
      assert.deepEqual(answer.ok ? answer.pairs : undefined, [],
        'an empty corpus must read as no pairs, not as a failure and not as a phantom row');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/**
 * The SIXTEEN field names, derived from both sides rather than agreed by hand.
 *
 * <p><b>The risk two providers named, and the reason the live check above cannot close it.</b>
 * `ReviewPair` is written twice — a C# positional record serialised camelCase, and a TypeScript
 * interface — so `fixSha` against `fix_sha` would leave every suite green while the page rendered
 * honest-looking absences, indistinguishable from an older server and therefore unnoticeable by
 * looking. The live check runs the real binary through the real reader, but against an EMPTY corpus:
 * nothing in this repository can seed a pair from outside the collector, and adding a CLI mode to
 * make a test possible would be changing the product to suit its tests.</p>
 *
 * <p>So the names are compared at their SOURCES: the record's properties, camelCased the way
 * `JsonSourceGenerationOptions(PropertyNamingPolicy = CamelCase)` will case them, against the
 * interface's own fields. It is a structural read across projects — the third in this suite, after
 * `SourceLanguage` and `Placeholders.cs` — and it fails loudly on a missing file rather than
 * quietly.</p>
 *
 * <p><b>What it does not prove:</b> that a populated row survives the round trip. Only a seeded
 * corpus could, and `research/module_tests.md` records that gap rather than implying it is closed.</p>
 */
test('both halves of the wire name the same sixteen fields', () => {
  const read = (from: string): string => {
    const at = path.join(process.cwd(), '..', from);
    assert.ok(fs.existsSync(at), `${from} has moved — this test is reading nothing`);

    return fs.readFileSync(at, 'utf8');
  };

  const record = /public sealed record ReviewPair\(([^;]*)\);/u.exec(read('src_mcp/src/Store/ReviewPair.cs'));
  assert.ok(record !== null, 'the ReviewPair record is no longer declared as one positional list');
  const served = [...(record[1] ?? '').matchAll(/\b(?:long|int|string)\s+([A-Z][A-Za-z]*)/gu)]
    .map((found) => `${(found[1] ?? '').charAt(0).toLowerCase()}${(found[1] ?? '').slice(1)}`);

  const shape = /export interface ReviewPair \{([\s\S]*?)\n\}/u.exec(read('src_vs_code/src/bugzReviewPage.ts'));
  assert.ok(shape !== null, 'the ReviewPair interface is no longer declared where this looks');
  const wanted = [...(shape[1] ?? '').matchAll(/readonly ([a-zA-Z]+)\??:/gu)].map((found) => found[1]);

  assert.ok(served.length >= 16, `the record read as ${served.length} fields — the regex is wrong`);
  assert.deepEqual([...served].sort(), [...wanted].sort(),
    'the two halves of the --pairs-json contract no longer name the same fields');
});

/**
 * A POPULATED pair, written into the real database, printed by the real binary, parsed by the real
 * reader — every one of the sixteen fields checked by value.
 *
 * <p><b>This is the check the story shipped without, and said so rather than implying otherwise.</b>
 * Two reviewers asked for it; the answer at the time was that nothing here can seed a pair from
 * outside the collector, and adding a CLI mode to make a test possible would be changing the product
 * to suit its tests. Both halves of that were true and the conclusion was still wrong: Node 22.5
 * brought `node:sqlite` into the runtime, so a test can write the rows itself. The product is
 * untouched and the gap is closed.</p>
 *
 * <p><b>The server makes the schema; this only fills it.</b> A `--bugs-json` call first, so every
 * table and every migration is the binary's own work — a test that wrote its own `CREATE TABLE`
 * would be asserting against a schema it invented, which is the failure this whole file exists to
 * avoid. Then four rows, through the three tables `Pairs()` actually joins: `repo_path` comes from
 * `sessions`, `head_sha` from `rounds`, and the rest from `findings` and `collect_pairs`.</p>
 *
 * <p>The two shas are DIFFERENT on purpose. `head_sha` is the commit the reviewers read and
 * `fix_sha` the commit the walk found the fix in, and the page labels each side of the complexity
 * with its own — so a projection that read one where the other belonged would render a row that is
 * confidently wrong about half of itself, and only distinct values can catch it.</p>
 */
test('a seeded pair survives the real binary and the real reader, field by field',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-seeded-'));
    try {
      // The BINARY makes the schema, so what is filled below is the real thing.
      const made = spawnSync(server(), ['--bugs-json'], {
        encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
      });
      assert.equal(made.status, 0, `the server would not open its database: ${made.stderr}`);

      const { DatabaseSync } = await import('node:sqlite');
      const db = new DatabaseSync(path.join(data, 'coai.db'));
      try {
        db.exec(`
          INSERT INTO sessions (id, repo_path, branch, opened_utc)
            VALUES ('s1', 'D:/repo', 'main', '2026-09-18T00:00:00Z');
          INSERT INTO rounds (id, session_id, stage, number, status, verdict,
                              started_utc, completed_utc, head_sha)
            VALUES (1, 's1', 'CodeReview', 1, 'done', 'proceed',
                    '2026-09-18T00:00:00Z', '2026-09-18T00:01:00Z', 'aaaa111aaaa');
          INSERT INTO findings (id, round_id, ordinal, severity, category, file, line,
                                title, why, fix, fix_sha)
            VALUES (7, 1, 0, 'Major', 'Reliability', 'src/Totals.cs', 42,
                    'a race', 'two writers, one row', 'take the lock', 'bbbb222bbbb');
          INSERT INTO collect_pairs (finding_id, symbol_name, language,
                                     skeleton_before, skeleton_after, written_utc, keep)
            VALUES (7, 'method_1', 'CSharp',
                    'void method_1() { }', 'void method_1() { lock (var_1) { } }',
                    '2026-09-18T00:02:00Z', -1);
        `);
      } finally {
        db.close();
      }

      const answer = await readPairs(server(), 5, async (args) => {
        const ran = spawnSync(server(), args, {
          encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
        });

        return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
      });

      assert.ok(answer.ok, `the reader refused the binary's output: ${answer.ok ? '' : answer.why}`);
      assert.equal(answer.pairs.length, 1, 'one pair was seeded, so one must come back');
      assert.deepEqual(answer.pairs[0], {
        findingId: 7,
        symbolName: 'method_1',
        language: 'CSharp',
        skeletonBefore: 'void method_1() { }',
        skeletonAfter: 'void method_1() { lock (var_1) { } }',
        keep: -1,
        severity: 'Major',
        category: 'Reliability',
        title: 'a race',
        repoPath: 'D:/repo',
        headSha: 'aaaa111aaaa',
        fixSha: 'bbbb222bbbb',
        file: 'src/Totals.cs',
        line: 42,
        why: 'two writers, one row',
        fix: 'take the lock',
      }, 'a field was lost, defaulted or crossed with another on the way across the wire');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });
