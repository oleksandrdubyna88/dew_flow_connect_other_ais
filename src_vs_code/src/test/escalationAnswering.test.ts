import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  DISMISSED,
  answerEscalation,
  type AnswerDeps,
  type AnswerFiles,
  type Picked,
} from '../escalationAnswering';
import type { Escalation } from '../escalations';

/**
 * Answering one card, end to end with the dialog and the disk injected — the informational card's Dismiss path included.
 *
 * <p>The convention under test: a card of kind `info` is told, not asked. Its Dismiss button writes ONE answer file beside the card with
 * decision `""` and text `"dismissed"` and shows NO dialog. The server never reads that file; it only takes the card off the screen.</p>
 */

const OWN = 'C:/coai';
const FROM = 'D:/wsl-store';
const NOW = '2026-10-22T10:00:00.000Z';

/** A card of THIS window's own store: it carries neither `from` (an own card has none) nor `kind` (an older server wrote none); `overrides` adds either. */
function card(overrides: Partial<Escalation> = {}): Escalation {
  return {
    id: 'info-1', sessionId: 's', repoPath: 'D:/repo', branch: 'feat/x', question: 'Nothing is held yet.', openFindings: [], askedUtc: NOW,
    ...overrides,
  };
}

/** A card read from another installation's store (`FROM`), so its answer goes back beside it. */
function cardFromTheStore(overrides: Partial<Escalation> = {}): Escalation {
  return card({ from: FROM, ...overrides });
}

/** An in-memory disk: a map of path to text, with the same rename the real one has. `rename` moves the temp onto the target. */
class MemoryDisk implements AnswerFiles {
  readonly files = new Map<string, string>();
  readonly writes: string[] = [];

  async exists(path: string): Promise<boolean> {
    return this.files.has(path);
  }

  async write(path: string, bytes: Uint8Array): Promise<void> {
    this.writes.push(path);
    this.files.set(path, new TextDecoder().decode(bytes));
  }

  async rename(from: string, to: string): Promise<void> {
    const text = this.files.get(from);
    if (text === undefined) {
      throw new Error(`ENOENT: no such file, rename '${from}'`);
    }
    this.files.delete(from);
    this.files.set(to, text);
  }
}

/** A dialog that fails the test if it is ever opened. */
const noDialog = async (): Promise<Picked | undefined> => assert.fail('a dialog was shown for a card that asks for nothing');

function deps(disk: AnswerFiles, ask: AnswerDeps['ask']): AnswerDeps {
  return { dataDir: OWN, files: disk, ask, now: () => NOW };
}

test('dismissing an informational card writes exactly one answer file beside the card and shows no dialog', async () => {
  const disk = new MemoryDisk();

  const outcome = await answerEscalation(cardFromTheStore({ kind: 'info' }), deps(disk, noDialog));

  assert.deepEqual(outcome, { kind: 'written', target: `${FROM}/escalations/info-1.answer.json` });
  assert.deepEqual([...disk.files.keys()], [`${FROM}/escalations/info-1.answer.json`], 'exactly one file, beside the card, and no temp left behind');
  assert.deepEqual(JSON.parse(disk.files.get(`${FROM}/escalations/info-1.answer.json`)!), {
    id: 'info-1', answer: 'dismissed', decision: '', answeredUtc: NOW,
  });
  assert.equal(DISMISSED.decision, '');
  assert.equal(DISMISSED.text, 'dismissed');
});

test('a dismissal of an own card (no `from`) is written into this window\'s own directory', async () => {
  const disk = new MemoryDisk();

  await answerEscalation(card({ kind: 'info' }), deps(disk, noDialog));

  assert.deepEqual([...disk.files.keys()], [`${OWN}/escalations/info-1.answer.json`]);
});

test('a card that asks for a decision shows the dialog and writes what was chosen', async () => {
  const disk = new MemoryDisk();
  const asked: string[] = [];

  const outcome = await answerEscalation(cardFromTheStore({ id: 'ask-1', kind: 'notice' }), deps(disk, async (c) => {
    asked.push(c.id);

    return { decision: 'continue', text: 'Keep going — more rounds' };
  }));

  assert.deepEqual(asked, ['ask-1'], 'the dialog is how a card that asks is answered');
  assert.equal(outcome.kind, 'written');
  assert.deepEqual(JSON.parse(disk.files.get(`${FROM}/escalations/ask-1.answer.json`)!), {
    id: 'ask-1', answer: 'Keep going — more rounds', decision: 'continue', answeredUtc: NOW,
  });
});

test('closing the dialog is not answering: nothing is written and the question stays open', async () => {
  const disk = new MemoryDisk();

  const outcome = await answerEscalation(cardFromTheStore({ kind: 'notice' }), deps(disk, async () => undefined));

  assert.deepEqual(outcome, { kind: 'closed' });
  assert.equal(disk.files.size, 0);
});

test('a card with no kind (an older server) is a question: the dialog is shown', async () => {
  const disk = new MemoryDisk();
  let shown = 0;

  await answerEscalation(cardFromTheStore(), deps(disk, async () => {
    shown += 1;

    return undefined;
  }));

  assert.equal(shown, 1, 'taking the dialog away from a card nobody here understands would leave a round blocked behind it');
});

test('an answer already sitting there is never overwritten — first writer wins', async () => {
  const disk = new MemoryDisk();
  disk.files.set(`${FROM}/escalations/info-1.answer.json`, 'the other window answered first');

  const outcome = await answerEscalation(cardFromTheStore({ kind: 'info' }), deps(disk, noDialog));

  assert.deepEqual(outcome, { kind: 'already-answered' });
  assert.deepEqual(disk.writes, [], 'the second window wrote nothing');
  assert.equal(disk.files.get(`${FROM}/escalations/info-1.answer.json`), 'the other window answered first');
});

test('an id that is not a name is refused before any path is made of it', async () => {
  const disk = new MemoryDisk();

  const outcome = await answerEscalation(cardFromTheStore({ id: '../../.ssh/authorized_keys', kind: 'info' }), deps(disk, noDialog));

  assert.deepEqual(outcome, { kind: 'refused-id' });
  assert.equal(disk.files.size, 0);
});

test('a write the disk refuses is returned with the folder it tried, never swallowed', async () => {
  const refusing: AnswerFiles = {
    exists: async () => false,
    write: async () => {
      throw new Error('ENOENT: the share went away');
    },
    rename: async () => undefined,
  };

  const outcome = await answerEscalation(cardFromTheStore({ kind: 'info' }), deps(refusing, noDialog));

  assert.equal(outcome.kind, 'failed');
  assert.equal(outcome.kind === 'failed' && outcome.dir, `${FROM}/escalations`);
  assert.match(String(outcome.kind === 'failed' && (outcome.reason as Error).message), /share went away/);
});
