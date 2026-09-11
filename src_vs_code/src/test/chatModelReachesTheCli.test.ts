import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHAT_RUNTIMES, launchSpecFor } from '../cliChatLaunch';
import { Vendor } from '../vendors';

/**
 * The model the person picked is the model the CLI is told to use.
 *
 * <p>Audit finding 7 of 2026-09-09. The chat offers a model per provider and a Team-server chat
 * sends the choice; a LOCAL chat did not. `launchSpecFor` built its command line from
 * `adapter.argv(resume)`, and the adapter contract had no parameter a model could travel in — so all
 * three adapters omitted it, every one of them correctly, and no test could have caught it. The
 * audit compiled the launch for each runtime and swapped one model name for another: byte-identical
 * command lines.</p>
 *
 * <p>What that costs is not only a wrong model. The tab records the label from the PICK, so the
 * answer is attributed to a model that never ran, and the spending line prices it as that model —
 * in a product whose own README says the rounds table is the product.</p>
 */

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'antigravity',
    runtime: 'antigravity',
    model: 'the-row-default',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

/**
 * A row on one of the chat runtimes.
 *
 * <p>One cast, here, rather than four at the call sites. `CHAT_RUNTIMES` is DERIVED from the adapter
 * map — its members are exactly the runtimes a chat can speak to — but it is typed `readonly
 * string[]`, and narrowing it is what this does. It is not a fixture standing in for a real type,
 * which is the `as` the TypeScript doctrine warns about: if the map grows a runtime `Vendor` does not
 * know, the compiler catches it at the map, not here.</p>
 */
function onRuntime(runtime: string): Vendor {
  return vendor({ runtime: runtime as Vendor['runtime'] });
}

/** The flag each vendor's own CLI takes, as this repository already verified for its reviewers. */
const FLAG: Readonly<Record<string, string>> = {
  antigravity: '--model',
  claude: '--model',
  codex: '-m',
};

test('changing the model changes the command line, for every runtime', () => {
  // The runtimes come from CHAT_RUNTIMES rather than a list typed here: a fourth adapter must not be
  // able to join the product without this guarantee being asked of it. That is the same reason the
  // defect existed at all — nothing forced an adapter to answer the question.
  for (const runtime of CHAT_RUNTIMES) {
    const a = launchSpecFor(onRuntime(runtime), 'C:/temp/empty', { resume: '', model: 'model-a' });
    const b = launchSpecFor(onRuntime(runtime), 'C:/temp/empty', { resume: '', model: 'model-b' });

    assert.notDeepStrictEqual(
      [...a.args],
      [...b.args],
      `${runtime}: two different models produced the same command line, so the picker decides nothing`,
    );
    assert.ok(a.args.includes('model-a'), `${runtime} did not carry model-a`);
    assert.ok(b.args.includes('model-b'), `${runtime} did not carry model-b`);
  }
});

test('each runtime uses its own vendor flag, in the spelling that vendor takes', () => {
  for (const runtime of CHAT_RUNTIMES) {
    const spec = launchSpecFor(onRuntime(runtime), 'C:/temp/empty', { resume: '', model: 'chosen' });
    const flag = FLAG[runtime];

    assert.ok(flag !== undefined, `${runtime} has no known model flag — add one, do not guess`);
    const at = spec.args.indexOf(flag);
    assert.ok(at >= 0, `${runtime} did not pass ${flag}`);
    assert.strictEqual(
      spec.args[at + 1],
      'chosen',
      `${runtime} passed ${flag} without the model after it`,
    );
  }
});

test('an empty model sends no model flag at all, so the CLI keeps its own default', () => {
  // The picker's first entry is "the first one that can answer", and a Team-server row may carry no
  // model either. Sending an empty string would be a different thing from sending nothing: one asks
  // the CLI for a model called "", the other does not ask.
  for (const runtime of CHAT_RUNTIMES) {
    const spec = launchSpecFor(onRuntime(runtime), 'C:/temp/empty', { resume: '', model: '' });

    assert.ok(!spec.args.includes(''), `${runtime} passed an empty argument`);
    assert.ok(
      !spec.args.includes('-m') && !spec.args.includes('--model'),
      `${runtime} asked for a model when none was chosen`,
    );
  }
});

test('the model travels beside the resume, not instead of it', () => {
  // codex is the only per-turn vendor: its second turn resumes a stored thread, and the model has to
  // survive that. A launch that carried one or the other would be right in half the conversation.
  const spec = launchSpecFor(
    vendor({ runtime: 'codex' }),
    'C:/temp/empty',
    { resume: 'thread-abc', model: 'gpt-5.6-luna' },
  );

  assert.ok(spec.args.includes('resume'));
  assert.ok(spec.args.includes('thread-abc'));
  assert.ok(spec.args.includes('gpt-5.6-luna'));
});
