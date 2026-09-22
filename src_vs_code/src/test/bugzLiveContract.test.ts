import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawnSync } from 'node:child_process';

import { parseBugs } from '../roundsDb';
import { keysFileIn, readFileAt, readPairs, readRealMethod, writeDecide } from '../roundsDbRead';
import { REMOVAL_REASONS, TREE_REASONS, TREE_STATES } from '../reviewTree';
import { readTrees, readTreeAt, removeTree } from '../reviewTreeRead';
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
 * The REAL `--real-method` output, parsed by the REAL `readRealMethod` — story 2.3's contract.
 *
 * <p>The same argument as the pairs check above: `RealMethod` is written twice, and every other test
 * of it feeds one side a fixture the other never produced. An EMPTY corpus is the right fixture:
 * what only this can see is that the real binary's envelope for "no such pair" is one the reader
 * accepts as a domain answer — `ok: true` with the reason on the method — rather than as a failed
 * read. The populated case is the server's own `TheRealMethodTests`, which can make a git repository
 * this side cannot.</p>
 */
test('the real --real-method answer for a pair nobody has is a reason, not a failure',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-realmethod-'));
    try {
      const read = await readRealMethod(server(), 1, async (args) => {
        const ran = spawnSync(server(), args, {
          encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
        });

        return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
      });

      assert.ok(read.ok, `the reader refused the real binary's own output: ${read.ok ? '' : read.why}`);
      assert.equal(read.method.findingId, 1);
      assert.equal(read.method.reason, 'pair_not_found', 'an empty corpus has no pair 1, and the binary must say so as data');
      assert.equal(read.method.before.reason, 'pair_not_found');
      assert.equal(read.method.after.reason, 'pair_not_found');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/**
 * A bad `--id` is 65 from the real binary, and the reader does NOT read it as "update the server".
 *
 * <p>The rule in both directions, observed on the shipped artefact: a mode the binary has must never
 * exit 64 whatever is wrong with the request, because 64 is the one code the reader turns into
 * "this machine's coai-mcp is older than the un-anonymised view".</p>
 */
test('a request fault from the real binary is 65, and is not read as an old server',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-realmethod-65-'));
    try {
      const ran = spawnSync(server(), ['--real-method'], {
        encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
      });
      assert.equal(ran.status, 65, `a missing --id must be 65, never 64: ${ran.stderr}`);

      const read = await readRealMethod(server(), Number.NaN, async () => ({ code: ran.status ?? 1, output: ran.stderr ?? '' }));
      assert.equal(read.ok, false);
      assert.equal(read.ok ? true : read.tooOld, false, 'a request fault must not send somebody to update a server that is fine');
      assert.match(read.ok ? '' : read.why, /--real-method needs --id/u);
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

  const shape = /export interface ReviewPair \{([\s\S]*?)\n\}/u.exec(read('src_vs_code/src/reviewPair.ts'));
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
      await seedOnePair(data);

      const answer = await readPairs(server(), 5, realRunIn(data));

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
        // Seeded with values rather than left at their defaults: an empty field that came back empty
        // proves nothing about its NAME, and a rename is exactly what this contract exists to catch.
        comment: 'it bit us twice',
        sentUtc: '2026-09-18T00:03:00Z',
        commentLost: 'the first one stays',
      }, 'a field was lost, defaulted or crossed with another on the way across the wire');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/**
 * A decision with words, through the extension's REAL writer and the REAL binary, read back by the real
 * reader (story 4.2).
 *
 * <p>The seam a plan reviewer named (codex): every other test of `--pairs-decide` runs it in-process or
 * stubs the spawn, so the argv, the file the writer hands over and the document the binary reads could
 * all drift apart with both halves green. This hands the file over the way the panel does and reads the
 * answer back off the binary's own `--pairs-json`.</p>
 *
 * <p>It seeds an UNSENT pair: the one it shares with the test above is sent, and changing a sent pair's
 * words is refused — which is its own assertion below, through the same real path.</p>
 */
test('a decision with words crosses the real writer and the real binary, and a refusal is not "too old"',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-decided-'));
    try {
      await seedOnePair(data, { sent: false });
      const run = realRunIn(data);

      const written = await writeDecide(server(), [{ findingId: 7, keep: 1, comment: '  two\r\nlines  ' }], keysFileIn(data), run);
      assert.deepEqual(written, { ok: true, decided: 1 }, 'the binary took the file the writer handed it');

      const read = await readPairs(server(), 5, run);
      assert.ok(read.ok);
      assert.equal(read.pairs[0]?.keep, 1);
      assert.equal(read.pairs[0]?.comment, 'two\nlines', 'the words as the person meant them: LF, and trimmed');

      const refused = await writeDecide(
        server(), [{ findingId: 7, keep: 1, comment: `looks${String.fromCharCode(7)}harmless` }], keysFileIn(data), run);
      assert.equal(refused.ok, false);
      assert.ok(!refused.ok && refused.tooOld !== true, 'a comment the rule refused is not a binary too old for comments');
      assert.ok(!refused.ok && refused.why.includes('U+0007'), `the refusal names the code point: ${refused.ok ? '' : refused.why}`);
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/** A `Run` that spawns the real binary against one data directory — the way the panel's `serverRun` does. */
function realRunIn(data: string): (args: readonly string[]) => Promise<{ code: number; output: string }> {
  return async (args) => {
    const ran = spawnSync(server(), [...args], {
      encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
    });

    return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
  };
}

/**
 * One pair, seeded into a database whose schema the BINARY made — so what is filled is the real thing.
 *
 * <p>`sent` decides whether the pair has been acknowledged and carries a lost comment, which is what
 * the field-by-field test reads back; a decision test needs it unsent, because a sent pair's words
 * cannot change.</p>
 */
async function seedOnePair(data: string, { sent = true }: { readonly sent?: boolean } = {}): Promise<void> {
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
                                     skeleton_before, skeleton_after, written_utc, keep,
                                     comment, sent_utc, comment_lost)
            VALUES (7, 'method_1', 'CSharp',
                    'void method_1() { }', 'void method_1() { lock (var_1) { } }',
                    '2026-09-18T00:02:00Z', -1,
                    ${sent ? "'it bit us twice', '2026-09-18T00:03:00Z', 'the first one stays'" : "'', '', ''"});
        `);
      } finally {
        db.close();
      }
}


/**
 * The POPULATED real-method contract: a real git repository, the real binary, the real reader.
 *
 * <p>The other real-method live test asks an empty corpus and gets a reason back, which proves the
 * envelope and nothing else — `source`, `className`, `kind` and the line spans could all be renamed
 * or dropped and it would stay green. A code reviewer said so, and this is the answer: two commits
 * of one C# file, a pair seeded to point at them, and every field compared to what the file
 * actually contains.</p>
 *
 * <p>The two commits differ on purpose, and the method MOVED between them — a using line is added
 * above it — so the after side can only be found by NAME. That is the collector's own rule and the
 * reason this mode does not look the method up by line twice.</p>
 */
test('a seeded pair reads its real method back out of a real repository',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-real-data-'));
    const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-real-repo-'));
    const git = (...args: readonly string[]): string => {
      const ran = spawnSync('git', [...args], { cwd: repo, encoding: 'utf8', timeout: 60_000 });
      assert.equal(ran.status, 0, `git ${args.join(' ')}: ${ran.stderr}`);

      return (ran.stdout ?? '').trim();
    };

    try {
      git('init', '-q');
      git('config', 'user.email', 'test@example.invalid');
      git('config', 'user.name', 'The Test');

      const before = 'namespace Shop;\n\npublic class Totals\n{\n    public int GetOrAdd(int n)\n    {\n        return n + 1;\n    }\n}\n';
      fs.writeFileSync(path.join(repo, 'Totals.cs'), before, 'utf8');
      git('add', 'Totals.cs');
      git('commit', '-q', '-m', 'the method as the reviewers read it');
      const headSha = git('rev-parse', 'HEAD');

      // The fix MOVES the method down the file, so locating it by the head line would find the
      // wrong thing and only the name can carry the correspondence.
      const after = 'using System;\nusing System.Text;\n\nnamespace Shop;\n\npublic class Totals\n{\n    public int GetOrAdd(int n)\n    {\n        return checked(n + 1);\n    }\n}\n';
      fs.writeFileSync(path.join(repo, 'Totals.cs'), after, 'utf8');
      git('commit', '-q', '-a', '-m', 'the fix, with the method moved down');
      const fixSha = git('rev-parse', 'HEAD');

      const made = spawnSync(server(), ['--bugs-json'], {
        encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
      });
      assert.equal(made.status, 0, `the server would not open its database: ${made.stderr}`);

      const { DatabaseSync } = await import('node:sqlite');
      const db = new DatabaseSync(path.join(data, 'coai.db'));
      try {
        const put = (sql: string, ...values: readonly (string | number)[]): void => {
          const statement = db.prepare(sql);
          statement.run(...values);
        };
        put(`INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s1', ?, 'main', '2026-09-18T00:00:00Z')`,
          repo.split('\\').join('/'));
        put(`INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc, head_sha)
             VALUES (1, 's1', 'CodeReview', 1, 'done', 'proceed', '2026-09-18T00:00:00Z', '2026-09-18T00:01:00Z', ?)`,
          headSha);
        put(`INSERT INTO findings (id, round_id, ordinal, severity, category, file, line, title, why, fix, fix_sha)
             VALUES (7, 1, 0, 'Major', 'Reliability', 'Totals.cs', 7, 'a race', 'two writers', 'take the lock', ?)`,
          fixSha);
        put(`INSERT INTO collect_pairs (finding_id, symbol_name, language, skeleton_before, skeleton_after, written_utc, keep)
             VALUES (7, 'GetOrAdd', 'CSharp', 'int method_1(int var_1) { }', 'int method_1(int var_1) { checked }', '2026-09-18T00:02:00Z', -1)`);
      } finally {
        db.close();
      }

      const read = await readRealMethod(server(), 7, async (args) => {
        const ran = spawnSync(server(), args, {
          encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
        });

        return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
      });

      assert.equal(read.ok, true, read.ok ? '' : read.why);
      if (!read.ok) {
        return;
      }

      const method = read.method;
      assert.equal(method.findingId, 7);
      assert.equal(method.name, 'GetOrAdd');
      assert.equal(method.reason, '', 'a pair whose repository is right there has no whole-pair reason');

      // The BEFORE side: the method as it was, un-anonymised, with its class.
      assert.equal(method.before.reason, '');
      assert.equal(method.before.className, 'Totals', 'the class is the story, and it is not stored anywhere');
      assert.equal(method.before.kind, 'method_declaration');
      assert.match(method.before.source, /public int GetOrAdd\(int n\)/u, 'the REAL name, not method_1');
      assert.match(method.before.source, /return n \+ 1;/u);
      assert.ok(!method.before.source.includes('checked'), 'the before side is the head commit, not the fix');

      // The AFTER side: found by NAME at a commit that moved it, and it is the FIXED text.
      assert.equal(method.after.reason, '');
      assert.equal(method.after.className, 'Totals');
      assert.match(method.after.source, /return checked\(n \+ 1\);/u, 'the after side is the fix commit');
      assert.ok(method.after.startLine > method.before.startLine,
        'the method moved down the file, which is exactly why the after side is found by name');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
      fs.rmSync(repo, { recursive: true, force: true });
    }
  });

/** The real spawn, against one data directory — what every live check of one mode does. */
function runIn(data: string): (args: readonly string[]) => Promise<{ code: number; output: string }> {
  return async (args) => {
    const ran = spawnSync(server(), args, {
      encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
    });

    return { code: ran.status ?? 1, output: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
  };
}

/**
 * The REAL `--file-at` output, parsed by the REAL `readFileAt` — story 3.1's contract, both halves.
 *
 * <p>The same argument as the real-method checks above: `FileAtRevision` is written twice, and every
 * other test of it feeds one side a fixture the other never produced. An empty corpus proves the
 * envelope for "no such pair" is a domain answer the reader accepts; the populated case below reads
 * a real repository through the real binary and the real reader and compares the TEXT to the file.</p>
 */
test('the real --file-at answer for a pair nobody has is a reason, not a failure',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-fileat-'));
    try {
      const read = await readFileAt(server(), { findingId: 1, headSha: '', file: '' }, runIn(data));

      assert.ok(read.ok, `the reader refused the real binary's own output: ${read.ok ? '' : read.why}`);
      assert.equal(read.file.findingId, 1);
      assert.equal(read.file.reason, 'pair_not_found', 'an empty corpus has no pair 1, and the binary must say so as data');
      assert.equal(read.file.text, '');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

test('a missing --id on --file-at is 65 from the real binary, and is not read as an old server',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-fileat-65-'));
    try {
      const ran = spawnSync(server(), ['--file-at'], {
        encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
      });
      assert.equal(ran.status, 65, `a missing --id must be 65, never 64: ${ran.stderr}`);

      const read = await readFileAt(server(), { findingId: Number.NaN, headSha: '', file: '' }, async () => ({ code: ran.status ?? 1, output: ran.stderr ?? '' }));
      assert.equal(read.ok, false);
      assert.equal(read.ok ? true : read.tooOld, false, 'a request fault must not send somebody to update a server that is fine');
      assert.match(read.ok ? '' : read.why, /--file-at needs --id/u);
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/**
 * A seeded pair reads its FILE back out of a real repository, at the commit the reviewers read —
 * and every failure reason the page renders comes off the real binary through the real reader.
 *
 * <p>Three pairs, one repository: one whose commit and path are real, one whose commit the
 * repository never had, one whose path was not at that commit. The text is compared to the file
 * the fixture wrote, so a mode that read the working tree instead of the object — the working tree
 * has been changed since — would be red here.</p>
 */
test('a seeded pair reads its file at its revision out of a real repository, and the reasons come off the real binary',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-fileat-data-'));
    const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-fileat-repo-'));
    const git = (...args: readonly string[]): string => {
      const ran = spawnSync('git', [...args], { cwd: repo, encoding: 'utf8', timeout: 60_000 });
      assert.equal(ran.status, 0, `git ${args.join(' ')}: ${ran.stderr}`);

      return (ran.stdout ?? '').trim();
    };

    try {
      git('init', '-q');
      git('config', 'user.email', 'test@example.invalid');
      git('config', 'user.name', 'The Test');
      const asReviewed = 'namespace Shop;\n\npublic class Totals\n{\n    public int GetOrAdd(int n) => n + 1;\n}\n';
      fs.writeFileSync(path.join(repo, 'Totals.cs'), asReviewed, 'utf8');
      git('add', 'Totals.cs');
      git('commit', '-q', '-m', 'the file as the reviewers read it');
      const headSha = git('rev-parse', 'HEAD');
      // The working tree moves on, so a read of the checkout instead of the object would differ.
      fs.writeFileSync(path.join(repo, 'Totals.cs'), '// changed since\n', 'utf8');

      const made = spawnSync(server(), ['--bugs-json'], {
        encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000,
      });
      assert.equal(made.status, 0, `the server would not open its database: ${made.stderr}`);

      const { DatabaseSync } = await import('node:sqlite');
      const db = new DatabaseSync(path.join(data, 'coai.db'));
      try {
        const put = (sql: string, ...values: readonly (string | number)[]): void => {
          db.prepare(sql).run(...values);
        };
        put(`INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s1', ?, 'main', '2026-09-18T00:00:00Z')`,
          repo.split('\\').join('/'));
        put(`INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc, head_sha)
             VALUES (1, 's1', 'CodeReview', 1, 'done', 'proceed', '2026-09-18T00:00:00Z', '2026-09-18T00:01:00Z', ?)`, headSha);
        put(`INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc, head_sha)
             VALUES (2, 's1', 'CodeReview', 2, 'done', 'proceed', '2026-09-18T00:00:00Z', '2026-09-18T00:01:00Z', ?)`,
          '0123456789abcdef0123456789abcdef01234567');
        put(`INSERT INTO findings (id, round_id, ordinal, severity, category, file, line, title, why, fix, fix_sha)
             VALUES (7, 1, 0, 'Major', 'Reliability', 'Totals.cs', 5, 'a race', 'two writers', 'take the lock', ?)`, headSha);
        put(`INSERT INTO findings (id, round_id, ordinal, severity, category, file, line, title, why, fix, fix_sha)
             VALUES (8, 2, 0, 'Major', 'Reliability', 'Totals.cs', 5, 'a race', 'two writers', 'take the lock', ?)`, headSha);
        put(`INSERT INTO findings (id, round_id, ordinal, severity, category, file, line, title, why, fix, fix_sha)
             VALUES (9, 1, 1, 'Major', 'Reliability', 'Elsewhere.cs', 5, 'a race', 'two writers', 'take the lock', ?)`, headSha);
        for (const id of [7, 8, 9]) {
          put(`INSERT INTO collect_pairs (finding_id, symbol_name, language, skeleton_before, skeleton_after, written_utc, keep)
               VALUES (?, 'GetOrAdd', 'CSharp', 'int method_1(int var_1) { }', 'int method_1(int var_1) { checked }', '2026-09-18T00:02:00Z', -1)`, id);
        }
      } finally {
        db.close();
      }

      const real = await readFileAt(server(), { findingId: 7, headSha, file: 'Totals.cs' }, runIn(data));
      assert.equal(real.ok, true, real.ok ? '' : real.why);
      if (!real.ok) {
        return;
      }
      assert.equal(real.file.reason, '', 'the commit and the path are real, so the file reads');
      assert.equal(real.file.sha, headSha, 'the document names the commit it is of');
      assert.equal(real.file.path, 'Totals.cs');
      assert.equal(real.file.text.split('\r\n').join('\n'), asReviewed, 'the file AS IT WAS, not the working tree that has moved on');

      const gone = await readFileAt(server(), { findingId: 8, headSha: 'ffffffffffffffffffffffffffffffffffffffff', file: 'Totals.cs' }, runIn(data));
      assert.equal(gone.ok ? gone.file.reason : gone.why, 'commit_unreachable', 'a commit the repository never had');

      const moved = await readFileAt(server(), { findingId: 9, headSha, file: 'Elsewhere.cs' }, runIn(data));
      assert.equal(moved.ok ? moved.file.reason : moved.why, 'file_not_in_commit', 'a path that was not there at that commit');
      assert.equal(moved.ok ? moved.file.path : '', 'Elsewhere.cs', 'and the page can say which path');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
      fs.rmSync(repo, { recursive: true, force: true });
    }
  });

/**
 * Story 3.2a's contract, the half a running test cannot reach: every reason word the SERVER can
 * answer is a word the EXTENSION knows.
 *
 * <p>This is the finding the plan round raised — the server could answer a reason nothing on this
 * side consumes, and both suites would stay green while a row said "the checkout could not be made"
 * for a fact it could have named. A word added to `ReviewTreeReason` and not to `TREE_REASONS` is
 * now a red test rather than a silent shrug.</p>
 *
 * <p>The C# side is read for its VALUES, not its names: the words that cross the wire are the string
 * literals, and four of them are deliberately aliases of `RealMethodReason` so that a person meets
 * one spelling of one fact across three modes.</p>
 */
test('every reason the checkout mode can answer is a reason this panel knows', () => {
  const at = path.join(process.cwd(), '..', 'src_mcp/core/Collecting/ReviewTree.cs');
  assert.ok(fs.existsSync(at), 'ReviewTree.cs has moved — this test is reading nothing');
  // Comment-stripped, per the house rule: a regex over raw source can match a constant somebody
  // wrote INSIDE a comment, and would then pass on a vocabulary the compiler never saw.
  // (Code round, codex.)
  const source = stripped(fs.readFileSync(at, 'utf8'));

  const klass = /public static class ReviewTreeReason\s*\{([^}]*)\}/u.exec(source);
  assert.ok(klass !== null, 'ReviewTreeReason is no longer declared where this looks');
  const body = klass[1] ?? '';

  // Two spellings of a constant: a literal word, and an alias of a sibling mode's constant.
  const literals = [...body.matchAll(/public const string \w+ = "([a-z_]+)";/gu)].map((m) => m[1]);
  const aliased = [...body.matchAll(/public const string \w+ = RealMethodReason\.(\w+);/gu)]
    .map((m) => reasonValueOf(source, m[1] ?? ''));

  const served = [...literals, ...aliased];
  assert.ok(served.length >= 7, `ReviewTreeReason read as ${served.length} words — the regex is wrong`);
  assert.deepEqual([...served].sort(), [...TREE_REASONS].sort(),
    'the server can answer a reason word this panel does not know, or knows one it cannot answer');
});

/**
 * A reason constant's VALUE, followed through however many aliases it takes.
 *
 * <p>The vocabulary is deliberately shared: `ReviewTreeReason.RepoPathMissing` is
 * `RealMethodReason.RepoPathMissing`, which is `SkipReason.RepoPathMissing`, which is finally
 * `"repo_path_missing"` in `CollectOutcome.cs` — three hops so that one fact has one spelling
 * across four modes. The literal is what crosses the wire, so the literal is what this resolves, by
 * looking for the name's one literal definition anywhere in the vocabulary's folder.</p>
 */
function reasonValueOf(_nearby: string, name: string): string {
  const folder = path.join(process.cwd(), '..', 'src_mcp/core/Collecting');
  const found = fs.readdirSync(folder)
    .filter((file) => file.endsWith('.cs'))
    .map((file) => new RegExp(`public const string ${name} = "([a-z_]+)";`, 'u')
      .exec(stripped(fs.readFileSync(path.join(folder, file), 'utf8'))))
    .find((hit) => hit !== null);
  assert.ok(found !== undefined && found !== null, `no literal defines ${name} anywhere in Collecting/`);

  return found[1] ?? '';
}

/** C# source with its comments removed, so a structural regex cannot match prose ABOUT the code. */
function stripped(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//gu, ' ').replace(/(^|[^:])\/\/.*$/gmu, '$1');
}

/**
 * The REAL `--tree-at` answer, parsed by the REAL reader — story 3.2a's contract, both halves.
 *
 * <p>The structural test above compares the two vocabularies as TEXT, which is worth having and is
 * not the same as running the thing. A reviewer put it exactly right: the mode could answer a shape
 * the reader refuses while both suites stayed green. This runs the BUILT binary against an empty
 * corpus and asserts the reader accepts its own output — the envelope, the reason as DATA at exit 0,
 * the id echoed, and every collection present.</p>
 *
 * <p>An empty corpus on purpose: a populated one would check a repository out, which is minutes and
 * a worktree to clean up, and `AReviewTreeTests` already does that against real git. What only this
 * can answer is whether the two halves of the WIRE agree.</p>
 */
test('the real --tree-at answer for a pair nobody has is a reason, not a failure',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-treeat-'));
    try {
      const read = await readTreeAt(server(), { findingId: 1, headSha: '', repoPath: '' }, runIn(data));

      assert.ok(read.ok, `the reader refused the real binary's own output: ${read.ok ? '' : read.why}`);
      assert.equal(read.tree.findingId, 1);
      assert.equal(read.tree.reason, 'pair_not_found', 'an empty corpus has no pair 1, and the binary must say so as data');
      assert.equal(read.tree.path, '', 'nothing was made, so there is nothing to open');
      assert.equal(read.tree.reused, false);
      assert.deepEqual([...read.tree.emptyMounts], []);
      assert.deepEqual([...read.tree.trees], []);
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

test('a missing --id on --tree-at is 65 from the real binary, and is not read as an old server',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-treeat-65-'));
    try {
      const { code } = await runIn(data)(['--tree-at']);

      assert.equal(code, 65, 'a request fault is 65; 64 would send the page down the too-old path');

      const read = await readTreeAt(server(), { findingId: 1, headSha: '', repoPath: '' }, async () => ({ code, output: '' }));
      assert.equal(read.ok, false);
      assert.equal(read.ok === false && read.tooOld, false, 'a request fault must never read as an old server');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

/** The literal VALUES a C# constant class spells, comments stripped first. */
function wordsOf(file: string, klass: string): readonly string[] {
  const at = path.join(process.cwd(), '..', file);
  assert.ok(fs.existsSync(at), `${file} has moved — this test is reading nothing`);
  // Character classes rather than escapes: in a TEMPLATE literal a lone backslash is eaten before
  // the regex ever sees it, which turned this pattern into `ReviewTreeStates*{` and made both
  // vocabulary tests fail inside their own helper rather than on the thing they check.
  const found = new RegExp(`public static class ${klass}[\\s]*[{]([^}]*)[}]`, 'u')
    .exec(stripped(fs.readFileSync(at, 'utf8')));
  assert.ok(found !== null, `${klass} is no longer declared where this looks`);
  const body = found[1] ?? '';

  return [
    ...[...body.matchAll(/public const string \w+ = "([a-z_]+)";/gu)].map((m) => m[1] ?? ''),
    ...[...body.matchAll(/public const string \w+ = (?:\w+)\.(\w+);/gu)]
      .map((m) => reasonValueOf('', m[1] ?? '')),
  ];
}

/**
 * Story 3.2b's two vocabularies, across both halves.
 *
 * <p>A word the server can answer and this side does not know is a row that says nothing useful; a
 * word this side knows and the server cannot answer is a sentence nobody will ever read. Both are
 * invisible in a green suite, which is why they are a test.</p>
 */
test('every state the list can answer is a state this panel knows', () => {
  const served = wordsOf('src_mcp/core/Collecting/ReviewTrees.cs', 'ReviewTreeState');

  assert.ok(served.length >= 5, `ReviewTreeState read as ${served.length} words — the regex is wrong`);
  assert.deepEqual([...served].sort(), [...TREE_STATES].sort());
});

test('every reason a removal can answer is a reason this panel knows', () => {
  const served = wordsOf('src_mcp/core/Collecting/ReviewTrees.cs', 'ReviewTreeRemovalReason');

  assert.ok(served.length >= 8, `ReviewTreeRemovalReason read as ${served.length} words — the regex is wrong`);
  assert.deepEqual([...served].sort(), [...REMOVAL_REASONS].sort());
});

test('the real --trees answer on a machine holding none is a list, not a failure',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-trees-'));
    try {
      const read = await readTrees(server(), runIn(data));

      assert.ok(read.ok, `the reader refused the real binary's own output: ${read.ok ? '' : read.why}`);
      assert.equal(read.answer.reason, '', 'an empty machine is an empty list, never a reason');
      assert.deepEqual([...read.answer.trees], []);
      assert.ok(read.answer.root.length > 0, 'and it still says where they would be');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

test('the real --tree-remove refuses a name it holds no record for, as data',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-treerm-'));
    try {
      const name = 'coai-review-00000000-000000000000';
      const read = await removeTree(server(), name, false, runIn(data));

      assert.ok(read.ok, `the reader refused the real binary's own output: ${read.ok ? '' : read.why}`);
      assert.equal(read.answer.name, name, 'the answer must be about the name that was asked');
      assert.equal(read.answer.reason, 'not_ours');
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });

test('a missing --tree is 65 from the real binary, and is not read as an old server',
  { skip: built ? false : 'the server is not built' }, async () => {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-treerm-65-'));
    try {
      const { code } = await runIn(data)(['--tree-remove']);

      assert.equal(code, 65, 'a request fault is 65; 64 would send the page down the too-old path');

      const read = await removeTree(server(), 'x', false, async () => ({ code, output: '' }));
      assert.equal(read.ok, false);
      assert.equal(read.ok === false && read.tooOld, false);
    } finally {
      fs.rmSync(data, { recursive: true, force: true });
    }
  });
