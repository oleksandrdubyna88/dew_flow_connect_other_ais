import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { parseManyFindings } from '../roundsDb';

/**
 * The batch findings contract, compared against the OTHER SIDE for real.
 *
 * <p>Two reviewers of the code round asked for this, and the conventions rule they cited says why:
 * two suites that each compare their own list against themselves both stay green while the contract
 * between them is broken. Here the C# record and `parseManyFindings` independently own the same
 * field names — rename `SessionId` on the server and every server test passes, every extension test
 * passes, and a bulk export silently marks every selected round failed.</p>
 *
 * <p>So this reads the server's own source and asserts the names the parser actually looks for.
 * `dataDirAgreesWithTheServer.test.ts` is the established pattern in this repository for exactly
 * this seam, and it is used here rather than a second mechanism.</p>
 *
 * <p><b>It also runs the parser against a payload shaped like the real one</b>, so the check is not
 * only that the names match but that a document of that shape reads as loaded rounds.</p>
 */

/** `src_mcp` as it sits beside `src_vs_code`; the suite runs from the extension's own root. */
const SERVER = resolve(process.cwd(), '..', 'src_mcp', 'src');

function serverSource(...parts: readonly string[]): string {
  return readFileSync(join(SERVER, ...parts), 'utf8');
}

/**
 * The record the server serialises, as declared.
 *
 * <p>Read by name rather than by position: what must agree is the JSON, and the JSON is named after
 * these properties through the camel-case naming policy in `ServerJsonContext`.</p>
 */
function batchRecordFields(): readonly string[] {
  const source = serverSource('Store', 'RoundsQuery.cs');
  const at = source.indexOf('public sealed record LoggedRoundOfMany(');
  assert.notEqual(at, -1, 'LoggedRoundOfMany is gone from the server — the batch contract moved');
  const declaration = source.slice(at, source.indexOf(');', at));

  return [...declaration.matchAll(/^\s{4}(?:\w+<[^>]+>|\w+\??)\s+(\w+)/gm)].map((found) => found[1] ?? '');
}

test('every field the extension reads out of a batch answer is one the server declares', () => {
  const declared = batchRecordFields().map((name) => name.charAt(0).toLowerCase() + name.slice(1));

  // Exactly what `parseManyFindings` reads. Listed here rather than derived, because the point is to
  // fail when the two DISAGREE — deriving both from one source would be the defect this prevents.
  for (const wanted of ['sessionId', 'stage', 'number', 'known', 'findings']) {
    assert.ok(declared.includes(wanted),
      `the server no longer declares '${wanted}' on LoggedRoundOfMany, so every exported round would `
      + `read as failed. It declares: ${declared.join(', ')}`);
  }
});

test('the extension asks for the keys by the names the server deserialises', () => {
  const source = serverSource('Server', 'ServerJsonContext.cs');
  const at = source.indexOf('public sealed record RoundKeyDto(');
  assert.notEqual(at, -1, 'RoundKeyDto is gone — the keys file contract moved');
  const declaration = source.slice(at, source.indexOf(');', at));

  // `readManyFindings` writes {sessionId, stage, number}; these are the properties receiving them.
  for (const wanted of ['SessionId', 'Stage', 'Number']) {
    assert.ok(declaration.includes(`string ${wanted}`) || declaration.includes(`int ${wanted}`),
      `the keys file names '${wanted.toLowerCase()}', which RoundKeyDto no longer has: ${declaration}`);
  }
});

test('the mode the extension spawns is the one the server dispatches on', () => {
  const program = serverSource('Program.cs');

  assert.ok(program.includes('"--findings-many" => Startup.FindingsMany'),
    'the extension spawns `--findings-many`; the server no longer classifies it, so every bulk '
    + 'export would take the old-server fallback and spawn once per round');
  assert.ok(program.includes('--keys-file'), 'the server no longer reads a keys file');
});

test('a document shaped like the server\'s own answer reads as loaded rounds', () => {
  // The camel-case names above, in the shape `LoggedManyFindings` serialises to.
  const parsed = parseManyFindings(JSON.stringify({
    rounds: [{
      sessionId: 's1',
      stage: 'CodeReview',
      number: 2,
      known: true,
      findings: [{ ordinal: 0, severity: 'Major', title: 'the retry never gives up', isGating: true }],
    }],
  }));

  assert.equal(parsed?.length, 1);
  assert.equal(parsed?.[0]?.known, true);
  assert.equal(parsed?.[0]?.findings[0]?.title, 'the retry never gives up');
  assert.equal(parsed?.[0]?.findings[0]?.isGating, true);
});
