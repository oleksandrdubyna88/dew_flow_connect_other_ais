import { ChatTabMemory } from './chatTabs';
import { ChatStoreFile } from './chatStoreFile';

/**
 * The three things a window binds ONCE, and everything that reads them.
 *
 * <p>Extracted from `chatCommand.ts` unchanged, second and for the same reason as the type before
 * it: the write queue reads `store`, the page push reads `memory`, and the turn, the hooks and the
 * restore each read `pulse`. With the handles left in the command file, whichever module came out
 * first would have had to import from it.</p>
 *
 * <p>They are `export let`, which is a live binding: an importer reads whatever the setter last put
 * there, and only this module can assign. That is the same guarantee the module-level variables gave
 * while they were private to one file, and it is why no call site changed.</p>
 *
 * <p>Why handles at all rather than parameters: the command has no context and neither do the
 * callbacks a panel is wired with, so threading a store through both to reach two call sites would
 * put a parameter on six signatures to carry a value that never changes. Absent means absent — no
 * store, no memento, no heartbeat — which is every test of this file's pure neighbours, and every
 * use is guarded.</p>
 *
 * <p><b>SonarCloud flags all three as "exporting mutable 'let' binding, use 'const' instead", and
 * that is kept rather than silenced.</b> The rule is right in general and wrong here: the mutability
 * IS the capability — it is what makes an importer see what `activate` bound rather than the
 * `undefined` that was there at import time — and it is confined to this file, three names and three
 * setters, with no other module able to assign. The alternative is three accessor functions, which
 * would change every call site in fifteen modules to buy the same guarantee with more ceremony. Left
 * as a reported issue so the next reader finds this paragraph rather than assuming it was missed.</p>
 *
 * <p>No `vscode` here, and nothing it imports reaches one either, so this module is NOT in
 * `sonar.coverage.exclusions` — and it has tests of its own, in `src/test/chatHost.test.ts`.</p>
 */

/**
 * The MEMENTO — where conversations were kept before the store on disk, and still written until the
 * migration has confirmed every record is there. Set once, in `activate`; unset by {@link retireMemento}.
 *
 * <p>A module-level handle rather than a parameter on six signatures: the command has no context and
 * neither do the callbacks a panel is wired with, and threading a store through both to reach two
 * call sites would be a wide change for a narrow need. It is absent in tests of this file's pure
 * neighbours and in every window whose migration has succeeded, and every use is guarded.</p>
 */
export let memory: ChatTabMemory | undefined;

export function rememberChatsIn(store: ChatTabMemory): void {
  memory = store;
}

/**
 * The store on disk is the ONLY store from here on, in this window.
 *
 * <p>Called by `activate` when the migration reports that every memento record is confirmed on disk
 * and the key is emptied. Until then the two are written together — a store that cannot be reached
 * must not leave a person's next words written NOWHERE, which is what an unconditional cut-over did
 * in the first draft of this story (A4's plan round). Unbinding the handle is the whole gate: every
 * `memory?.` below becomes a no-op, and nothing else has to know.</p>
 */
export function retireMemento(): void {
  memory = undefined;
}

/**
 * The conversation store on disk — the source of truth, read by the reload serializer and written on
 * every push.
 *
 * <p>Bound the same way and for the same reason as the memento above. Absent means no store, which
 * is every test of this file's pure neighbours: every use is guarded.</p>
 */
export let store: ChatStoreFile | undefined;

export function keepChatsIn(onDisk: ChatStoreFile): void {
  store = onDisk;
}

/**
 * Told whenever the set of open conversations changes — a thread registered, restored, closed, or
 * re-minted under a new id — so this window's heartbeat (`chatStoreHeartbeat.ts`) announces it at once
 * rather than on its next minute. Bound the way the store above is; absent means no heartbeat, which
 * is every test of this file's pure neighbours.
 */
export let pulse: (() => void) | undefined;

export function pulseChatsThrough(onChange: () => void): void {
  pulse = onChange;
}
