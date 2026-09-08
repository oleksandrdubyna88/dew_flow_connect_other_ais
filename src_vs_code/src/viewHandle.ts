/**
 * A webview view this extension holds between paints, and the two things that go wrong with one.
 *
 * <p>Its own module, with no `vscode` import, because both of those things are decisions rather than
 * API calls — and a decision that lives inside `panelProvider` is a decision no test can reach. The
 * provider keeps the API; this keeps the rules.</p>
 *
 * <p><b>The failure it exists for.</b> 2026-09-08: a person ran a plan review, the gate asked them a
 * question, and opening the rounds log to answer it produced `Webview is disposed`. `panelProvider`
 * held a `WebviewView` and never subscribed to `onDidDispose` — while both sibling panels
 * (`helpPanel`, `roundsLogPanel`) did and cleared their handles there. VS Code disposes a view when
 * it is hidden, the handle survived, and the escalation watcher repainted into it every five
 * seconds.</p>
 */
export class ViewHandle<T> {
  private current: T | undefined;

  /** What can be painted, or `undefined` — which is an ordinary state, not a failure. */
  get view(): T | undefined {
    return this.current;
  }

  /** VS Code resolved a view: this one is now the live one, whatever was here before. */
  hold(view: T): void {
    this.current = view;
  }

  /**
   * A view was disposed. The handle clears ONLY if that view is the one still held.
   *
   * <p>Because a disposal callback can arrive late. VS Code re-creates a hidden view when it is
   * shown again, so the order can be: A resolved, A hidden, B resolved, A's `onDidDispose` fires.
   * An unconditional `this.view = undefined` there blanks the LIVE view, and the sidebar then stops
   * updating until something else happens to resolve it — a silent half of the bug this class was
   * written for. Raised by codex on the plan round, before a line of it existed.</p>
   */
  release(view: T): void {
    if (this.current === view) {
      this.current = undefined;
    }
  }
}

/**
 * Whether a rejection is the disposal we expect, rather than a failure worth reporting.
 *
 * <p>A `postMessage` to a live view can also reject — a payload it cannot structure-clone, a
 * host channel that fell over — and swallowing that alongside the expected one leaves a stale
 * sidebar with nothing anywhere saying why. So the expected one is recognised by NAME and every
 * other rejection keeps its reporter. Raised on the plan round.</p>
 *
 * <p>Matched on the substring VS Code itself throws (`Webview is disposed`), case-insensitively and
 * against the message rather than the type: the API rejects with a plain `Error`, so there is no
 * type to test, and this is the string a person saw in a notification.</p>
 */
export function isDisposedRejection(reason: unknown): boolean {
  const message = reason instanceof Error ? reason.message : String(reason ?? '');
  return message.toLowerCase().includes('webview is disposed')
    || message.toLowerCase().includes('webview is not initialized');
}
