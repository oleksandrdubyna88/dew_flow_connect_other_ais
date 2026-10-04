import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FeaturesCache, hasFeature, modifiedMs, readBinaryFeatures } from '../binaryFeatures';
import type { Run } from '../roundsDbRead';

/**
 * What the installed coai-mcp says it accepts (`--features`), read once per binary — so a catalog field or a flag is
 * sent only when the binary lists it (todo/PLAN_one_model_catalog.md, epic 2: "capability, not version numbers").
 *
 * <p>Anything short of a clean answer is NO features: a field sent to a binary that ignores it is the silence the list
 * exists to end, and holding one back from a binary that would have taken it costs only the feature.</p>
 */

const answering = (code: number, output: string): Run => async () => ({ code, output });

test('a binary that lists its capabilities is believed, each once', async () => {
  const answer = await readBinaryFeatures(answering(0, '{"features":["bugzRuntime","bugzRuntime","systemPrompt"]}'));

  assert.deepEqual(answer.features, ['bugzRuntime', 'systemPrompt']);
  assert.equal(hasFeature(answer, 'bugzRuntime'), true);
  assert.equal(hasFeature(answer, 'cliEffort'), false);
});

test('64 is a binary from before the list: no features, and it says so', async () => {
  const answer = await readBinaryFeatures(answering(64, 'unknown argument'));

  assert.deepEqual(answer.features, []);
  assert.match(answer.why, /older/u);
});

test('a failure or an answer that is not the list is no features, never a guess', async () => {
  for (const [code, output] of [[1, 'boom'], [0, 'not json'], [0, '{"features":"bugzRuntime"}'], [0, '{"features":[7,"x"]}']] as const) {
    const answer = await readBinaryFeatures(answering(code, output));

    assert.deepEqual(answer.features.filter((one) => one !== 'x'), [], `${code} ${output}`);
    assert.notEqual(answer.why, '', 'and the reason is kept, for the card that says what is not sent');
  }
});

test('the binary is asked with --features and nothing else', async () => {
  const asked: (readonly string[])[] = [];
  await readBinaryFeatures(async (args) => {
    asked.push(args);

    return { code: 0, output: '{"features":[]}' };
  });

  assert.deepEqual(asked, [['--features']]);
});

test('the cache key changes when the binary file does, and a missing file is 0 rather than a throw', async () => {
  const { mkdtempSync, writeFileSync, utimesSync, rmSync } = await import('node:fs');
  const { join } = await import('node:path');
  const { tmpdir } = await import('node:os');
  const dir = mkdtempSync(join(tmpdir(), 'coai-features-'));
  try {
    const file = join(dir, 'coai-mcp');
    writeFileSync(file, 'one');
    utimesSync(file, new Date(1_000_000), new Date(1_000_000));
    const first = await modifiedMs(file);
    utimesSync(file, new Date(2_000_000), new Date(2_000_000));

    assert.notEqual(await modifiedMs(file), first, 'an updated binary is a new question');
    assert.equal(await modifiedMs(join(dir, 'absent')), 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the binary is asked once per file, again after an update, and not at all when none is installed', async () => {
  const { mkdtempSync, writeFileSync, utimesSync, rmSync } = await import('node:fs');
  const { join } = await import('node:path');
  const { tmpdir } = await import('node:os');
  const dir = mkdtempSync(join(tmpdir(), 'coai-features-cache-'));
  try {
    const file = join(dir, 'coai-mcp');
    writeFileSync(file, 'one');
    utimesSync(file, new Date(1_000_000), new Date(1_000_000));
    let asked = 0;
    const runFor = (): Run => async () => {
      asked += 1;

      return { code: 0, output: '{"features":["bugzRuntime"]}' };
    };
    const cache = new FeaturesCache();

    await cache.of(file, runFor);
    await cache.of(file, runFor);
    assert.equal(asked, 1, 'a render every few seconds is not a spawn every few seconds');
    utimesSync(file, new Date(2_000_000), new Date(2_000_000));
    assert.deepEqual((await cache.of(file, runFor)).features, ['bugzRuntime']);
    assert.equal(asked, 2, 'an updated binary is asked again');
    assert.deepEqual((await cache.of(undefined, runFor)).features, []);
    assert.equal(asked, 2);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
