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

/** Every `.ts` under the test folder, as source, with comments stripped. */
function sourcesWithoutComments(): { file: string; code: string }[] {
  return fs
    .readdirSync(testSources)
    .filter((f) => f.endsWith('.ts'))
    .map((file) => ({
      file,
      // The note in this very file names the cast it replaced, and a doctrine comment naming a
      // forbidden shape must not be what fails the check that forbids it. Raised on the plan round.
      code: fs
        .readFileSync(path.join(testSources, file), 'utf8')
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/(^|[^:])\/\/.*$/gm, '$1'),
    }));
}

test('no test fixture asserts a hand-built object into a Response', () => {
  const offenders = sourcesWithoutComments()
    .filter((s) => /\bas\s+Response\b/.test(s.code))
    .map((s) => s.file);

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

test('a failure status is not ok, and 204 carries no body', () => {
  assert.strictEqual(response({ status: 403, body: '{"error":"no"}' }).ok, false);
  assert.strictEqual(response({ status: 426 }).ok, false);
  // The constructor REFUSES a body on a 204. A hand-built object would have carried one happily and
  // been wrong about what a server can send.
  assert.strictEqual(response({ status: 204 }).status, 204);
});

test('two responses do not share their headers', () => {
  const first = response({ status: 200, headers: { 'X-Coai-Contract': '1' } });
  const second = response({ status: 200 });

  first.headers.set('X-Coai-Contract', '9');

  assert.strictEqual(second.headers.get('X-Coai-Contract'), null, 'one test may not colour another');
});
