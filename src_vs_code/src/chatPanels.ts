/**
 * Which conversation belongs to which Claude Code tab.
 *
 * <p>Pure — it knows nothing about `vscode`. It is handed things that can be revealed and disposed,
 * and it keeps them under the key `sessionKey.ts` decided on. Splitting it out is what makes the one
 * rule in it testable: <b>disposing one panel disposes ONE session, and only its own.</b></p>
 *
 * <p><b>This is the deviation from every other webview in this extension.</b> `RoundsLogPanel` and
 * the help page are singletons — "one webview panel per window, reused while open". This feature
 * needs one panel per Claude Code SESSION: the owner's requirement is that session 1's conversation
 * lives in its tab, session 2 opens its own, and both stay open while he moves between them. So the
 * field becomes a map, and disposal removes one entry rather than clearing a field.</p>
 *
 * <p>A `Map` keyed by object identity is the point, not an implementation detail: two Claude Code
 * tabs can both be called `main`, and a registry keyed by name would hand the second tab the first
 * tab's conversation. That failure is silent — the answer arrives, it is simply about somebody
 * else's question — which is why two of the plan gate's reviewers refused the label design.</p>
 */

/** What the registry needs from a panel. `vscode.WebviewPanel` satisfies it; so does a fake. */
export interface RevealablePanel {
  reveal(): void;
  dispose(): void;
}

/** What the registry needs from a conversation: only that it can be ended. */
export interface DisposableSession {
  dispose(): void;
}

export interface ChatEntry {
  readonly panel: RevealablePanel;
  readonly session: DisposableSession;
  /** The tab's label at the time it was opened — the panel's title, and the fallback key. */
  readonly label: string;
}

/** What `open` had to do, so a caller can say it out loud rather than infer it. */
export type OpenOutcome = 'revealed' | 'created';

/**
 * One conversation per source tab.
 *
 * <p>A class, not a set of functions over a passed-in map: it is a stateful service and the house
 * rule says those stay classes. What it must never become is a place where state is edited from
 * outside — everything below returns rather than exposes the map.</p>
 */
export class ChatPanels {
  private readonly entries = new Map<object, ChatEntry>();

  /** How many conversations are open. For the tests, and for a caller that wants to say so. */
  get size(): number {
    return this.entries.size;
  }

  has(key: object): boolean {
    return this.entries.has(key);
  }

  get(key: object): ChatEntry | undefined {
    return this.entries.get(key);
  }

  /** Every open panel, as the shape `sessionKey.ts` matches against. */
  known(): readonly { readonly key: object; readonly label: string }[] {
    return [...this.entries].map(([key, entry]) => ({ key, label: entry.label }));
  }

  /**
   * Reveal the conversation this tab already has, or create it.
   *
   * <p>`create` is a factory rather than a value so nothing is built for a tab that already has a
   * panel — building one would start a vendor process for a conversation nobody asked to begin.</p>
   */
  open(key: object, create: () => ChatEntry): { entry: ChatEntry; outcome: OpenOutcome } {
    const known = this.entries.get(key);
    if (known !== undefined) {
      known.panel.reveal();

      return { entry: known, outcome: 'revealed' };
    }

    const entry = create();
    this.entries.set(key, entry);

    return { entry, outcome: 'created' };
  }

  /**
   * Move an existing conversation onto a new key.
   *
   * <p>For the one case `sessionKey.ts` calls `rekey`: the host handed back a new `Tab` object for a
   * tab that is still open, and the panel would otherwise be orphaned beside it. Refuses rather than
   * overwrites when the destination is taken — two conversations must never collapse into one, and
   * this is the second door into that failure.</p>
   */
  rekey(from: object, to: object): boolean {
    const entry = this.entries.get(from);
    if (entry === undefined || this.entries.has(to)) {
      return false;
    }
    this.entries.delete(from);
    this.entries.set(to, entry);

    return true;
  }

  /**
   * Forget one conversation and end its session.
   *
   * <p>Called from the panel's own `onDidDispose`, which is why it does not dispose the panel: by
   * then VS Code already has. What it must do is dispose the SESSION — a vendor process that
   * outlives its tab is an authenticated child nobody can see and nobody will stop.</p>
   */
  close(key: object): boolean {
    const entry = this.entries.get(key);
    if (entry === undefined) {
      return false;
    }
    this.entries.delete(key);
    entry.session.dispose();

    return true;
  }

  /** End everything — the extension is deactivating, and no child may outlive it. */
  closeAll(): void {
    for (const key of [...this.entries.keys()]) {
      this.close(key);
    }
  }
}
