import * as assert from 'node:assert';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { response } from './responseFixture';

/**
 * The fixture that stopped pretending.
 *
 * <p>Eight hand-built objects across three files used to be asserted into `Response`, and every one
 * of them lacked `headers` — harmlessly, until the client started reading a response header and all
 * eight threw. `typescript/doctrine.md` forbids exactly that shape: "No `as` cast in a test fixture
 * standing in for a real type". The answer is not a better-documented pretence; it is the runtime's
 * own constructor, which cannot lack a member because it is the real thing.</p>
 */

/**
 * The SOURCE folder, not the one this file is running from.
 *
 * <p>`__dirname` is `out/test` — the compiled JavaScript, where a TypeScript `as` cast has already
 * been erased by the compiler. A check that read from there would pass against eight offenders and
 * prove nothing, which is exactly what it did on its first run.</p>
 */
const testSources = path.join(__dirname, '..', '..', 'src', 'test');

/**
 * The two files allowed to write the words, because they are about them.
 *
 * <p>An exclusion list rather than a comment stripper. The first version of this check stripped
 * comments with a regex so that this file's own doctrine note would not fail it — and a regex that
 * decides what is a comment gets `http://` and a `//` inside a string literal wrong, in a check whose
 * whole job is to be trusted. Naming the two files is smaller and cannot be fooled. Raised on the
 * code round.</p>
 */
const MAY_NAME_IT = ['responseFixture.ts', 'responseFixture.test.ts'];

/** Every `.ts` beneath the test folder, however deep. */
function testFiles(dir = testSources, prefix = ''): { file: string; code: string }[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const here = path.join(dir, entry.name);
    // Recursive, because `src/test/helpers/x.ts` is a place a fixture would land and a check that
    // read only the top level would pass while the invariant it names was false. Four reviewers.
    if (entry.isDirectory()) {
      return testFiles(here, `${prefix}${entry.name}/`);
    }

    return entry.name.endsWith('.ts')
      ? [{ file: `${prefix}${entry.name}`, code: fs.readFileSync(here, 'utf8') }]
      : [];
  });
}

test('no test fixture asserts a hand-built object into a Response', () => {
  const offenders = testFiles()
    .filter((f) => !MAY_NAME_IT.includes(path.basename(f.file)))
    .filter((f) => /\bas\s+Response\b/.test(f.code))
    .map((f) => f.file);

  assert.deepStrictEqual(
    offenders,
    [],
    `these still pretend to be a Response instead of building one: ${offenders.join(', ')}`,
  );
});

test('the fixture is a real Response, with everything a real one has', async () => {
  const r = response({ status: 200, body: '{"a":1}', headers: { 'X-Coai-Contract': '2' } });

  assert.ok(r instanceof Response, 'not a stand-in — the thing itself');
  assert.strictEqual(r.ok, true, 'ok is DERIVED from the status, never passed in');
  assert.strictEqual(r.status, 200);
  assert.strictEqual(r.headers.get('X-Coai-Contract'), '2');
  assert.strictEqual(r.headers.get('x-coai-contract'), '2', 'and the headers are case-insensitive');
  assert.strictEqual(await r.text(), '{"a":1}');
  // Free, and none of the hand-built ones had them.
  assert.deepStrictEqual(await response({ status: 200, body: '{"a":1}' }).json(), { a: 1 });
});

test('a response that names no headers still answers, rather than throwing', () => {
  // The failure this exists to prevent: eight fixtures omitted `headers` and threw the day something
  // read one. An absent header is `null` here, which is what the production code is written against.
  const r = response({ status: 200, body: '{}' });

  assert.strictEqual(r.headers.get('X-Coai-Contract'), null);
});

test('a failure status is not ok, and a 204 given a body says so', () => {
  assert.strictEqual(response({ status: 403, body: '{"error":"no"}' }).ok, false);
  assert.strictEqual(response({ status: 426 }).ok, false);
  assert.strictEqual(response({ status: 204 }).status, 204);

  // Named rather than swallowed. Quietly dropping the body would hide the caller's mistake, and a
  // test that believes it sent something a server cannot send is a test about nothing.
  assert.throws(
    () => response({ status: 204, body: 'should not be here' }),
    /a 204 carries no body/,
  );
});

test('two responses do not share their headers', () => {
  const first = response({ status: 200, headers: { 'X-Coai-Contract': '1' } });
  const second = response({ status: 200 });

  first.headers.set('X-Coai-Contract', '9');

  assert.strictEqual(second.headers.get('X-Coai-Contract'), null, 'one test may not colour another');
});
