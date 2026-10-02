import assert from 'node:assert/strict';
import { test } from 'node:test';

import { askPerson, type Waiting, whileWorking } from '../personWait';

/**
 * Which panel operation a VS Code prompt holds up (research/PLAN_busy_mark_pauses_while_you_type.md §3.1).
 *
 * <p>Asked for on 2026-10-02, once the busy mark shipped: an action that opens an input box was "in flight" while the
 * person typed into it, so the bar ran over their typing. The operator's ruling — "пока печатаю не считаем" — is that
 * waiting on the person is not work. These tests prove the prompt finds ITS operation however deep it is opened, and
 * that a prompt opened outside any operation is just a prompt.</p>
 */

/** A `Waiting` that writes down what it was told, in order. */
function journal(name: string, said: string[]): Waiting {
  return { pause: () => { said.push(`${name} pause`); }, resume: () => { said.push(`${name} resume`); } };
}

/** A prompt the test answers when it chooses — as a person answers an input box. */
function prompt<T>(): { readonly open: () => Thenable<T>; readonly answer: (value: T) => void; readonly refuse: (why: Error) => void } {
  let answer: (value: T) => void = () => undefined;
  let refuse: (why: Error) => void = () => undefined;
  const shown = new Promise<T>((resolve, reject) => { answer = resolve; refuse = reject; });

  return { open: () => shown, answer, refuse };
}

async function deeper<T>(levels: number, at: () => Promise<T>): Promise<T> {
  await Promise.resolve();

  return levels === 0 ? at() : deeper(levels - 1, at);
}

test('a prompt opened outside any operation is a plain prompt: answered, and nothing paused', async () => {
  const said: string[] = [];
  const box = prompt<string>();
  const asked = askPerson(box.open);
  box.answer('typed');

  assert.equal(await asked, 'typed');
  assert.deepEqual(said, []);
});

test('a prompt several awaits below the operation pauses THAT operation while it is open, and resumes it after', async () => {
  const said: string[] = [];
  const box = prompt<string>();
  const work = whileWorking(journal('add a reviewer', said), () => deeper(3, async () => {
    said.push('work before the box');
    const typed = await askPerson(() => { said.push('box open'); return box.open(); });
    said.push(`work after the box: ${typed}`);
  }));

  await deeper(6, () => Promise.resolve());
  assert.deepEqual(said, ['work before the box', 'add a reviewer pause', 'box open'], 'paused before the box shows, not after');
  box.answer('https://example.test/v1');
  await work;

  assert.deepEqual(said.slice(3), ['add a reviewer resume', 'work after the box: https://example.test/v1']);
});

test('a prompt that fails still resumes its operation, and the failure reaches the caller', async () => {
  const said: string[] = [];
  const box = prompt<string>();
  const work = whileWorking(journal('close consultation', said), () => askPerson(box.open));
  box.refuse(new Error('the window closed under the box'));

  await assert.rejects(work, /the window closed under the box/u);
  assert.deepEqual(said, ['close consultation pause', 'close consultation resume'], 'a refused box never leaves the clock stopped');
});

test('two operations running side by side each pause only themselves', async () => {
  const said: string[] = [];
  const first = prompt<string>();
  const second = prompt<string>();
  const one = whileWorking(journal('one', said), () => deeper(1, () => askPerson(first.open)));
  const two = whileWorking(journal('two', said), () => deeper(2, () => askPerson(second.open)));
  await deeper(6, () => Promise.resolve());
  second.answer('b');
  await two;

  assert.deepEqual(said, ['one pause', 'two pause', 'two resume'], 'answering the second box resumes the second operation alone');
  first.answer('a');
  await one;
  assert.deepEqual(said.at(-1), 'one resume');
});
