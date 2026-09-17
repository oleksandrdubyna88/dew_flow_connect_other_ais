/**
 * Let go of a conversation's process and its directory — both of them, whatever the other did.
 *
 * <p><b>The defect.</b> Four places did this as two bare statements:</p>
 *
 * <pre>thread.session.dispose();
 * thread.home.release();</pre>
 *
 * <p>If the dispose throws, the release never runs: a vendor process and its temporary directory
 * outlive the tab that owned them, and nothing says so. At two of the four it is worse than a leak —
 * `chatLaunch` and `chatTurn` install a REPLACEMENT session on the next lines, so a throw there
 * leaves the thread holding the old one while the new one is already running.</p>
 *
 * <p><b>This is not new code.</b> `ended()` in `chatArchive.ts` has done it correctly since its own
 * code round, with the reason recorded there: <i>"the disposal and the release are INDEPENDENT of
 * the stop and of each other, and run whatever happened before them"</i>. Measured on 2026-09-17,
 * one of the four sites was guarded and three were not. So this is that one lifted out, not a fourth
 * opinion about how to close a session (`reuse-first.md`: extract the shared half).</p>
 *
 * <p><b>The order is load-bearing and it is not alphabetical.</b> The release comes AFTER the
 * disposal, never before: a CLI still writing on its way out, into a directory that has already been
 * removed, throws where nobody is listening.</p>
 *
 * <p><b>What this cannot do, said rather than implied.</b> If `dispose()` itself throws there is no
 * stronger lever at this layer — `ChatSession.stop()` ends a TURN and leaves the session open, so it
 * is weaker, not stronger. The process is then genuinely leaked until the host exits. What changes is
 * that the leak is LOUD and the directory is still released, instead of a silent double failure. An
 * earlier draft of the plan said a failed dispose should escalate through `stop()`; reading that
 * method's own contract is what removed it.</p>
 *
 * @param say where the two failures are reported. Injected so this is a value with no console in it,
 *   and so a test can watch what was said rather than what was logged.
 */
export function retire(
  thread: { readonly session: { dispose: () => void }; readonly home: { release: () => void } },
  say: (what: string, reason: unknown) => void,
): void {
  try {
    // Safe to call twice, so a session already gone stays gone.
    thread.session.dispose();
  } catch (reason) {
    say('a chat session would not be disposed, so its process may outlive the tab', reason);
  }
  try {
    thread.home.release();
  } catch (reason) {
    say('a chat temp directory could not be released', reason);
  }
}
