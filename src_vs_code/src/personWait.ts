import { AsyncLocalStorage } from 'node:async_hooks';

/**
 * Which panel operation a VS Code prompt holds up (todo/PLAN_busy_mark_pauses_while_you_type.md §3.1).
 *
 * <p>Asked for on 2026-10-02, once the busy mark shipped: an action that opens an input box was "in flight" for as long
 * as the box was open, so the bar ran over the person's typing. The operator's ruling — "пока печатаю не считаем" — is
 * that waiting on the person is not work. A prompt is opened one to four calls below the operation that waits on it,
 * in helpers that commands outside the panel share, so the operation is not passed down: it is carried, by
 * `AsyncLocalStorage`, through every `await` below the point `tracked` started it.</p>
 *
 * <p>Every prompt in this extension is opened through {@link askPerson}; `promptsWaitForThePerson.test.ts` makes one that
 * is not red. Outside a panel operation — a command from the palette, a question at startup — it is a plain prompt.</p>
 */

/** What an operation is told while the person has a prompt of it open. */
export interface Waiting {
  pause(): void;
  resume(): void;
}

const current = new AsyncLocalStorage<Waiting>();

/** Runs `work` as the operation `waiting` speaks for: a prompt opened anywhere beneath it pauses that operation. */
export function whileWorking<T>(waiting: Waiting, work: () => T): T {
  return current.run(waiting, work);
}

/**
 * Opens a prompt and waits for the person, with the operation it holds up paused meanwhile.
 *
 * <p>The operation is read before the prompt opens, so it is paused before the box is on screen; it is resumed in a
 * `finally`, so a box refused, dismissed or torn down with its window never leaves the clock stopped.</p>
 */
export async function askPerson<T>(prompt: () => Thenable<T>): Promise<T> {
  const waiting = current.getStore();
  waiting?.pause();
  try {
    return await prompt();
  } finally {
    waiting?.resume();
  }
}
