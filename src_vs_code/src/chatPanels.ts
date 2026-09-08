/**
 * Which conversation belongs to which Claude Code tab.
 *
 * <p>Pure — it knows nothing about `vscode`. It is handed things that can be revealed, disposed and
 * posted to, and it keeps them under the key `sessionKey.ts` decided on. Splitting it out is what
 * makes the one rule in it testable: <b>closing one panel disposes ONE session, and only its own.</b></p>
 *
 * <p><b>This is the deviation from every other webview in this extension.</b> `RoundsLogPanel` and
 * the help page are singletons — "one webview panel per window, reused while open". This feature
 * needs one panel per Claude Code SESSION: the owner's requirement is that session 1's conversation
 * lives in its tab, session 2 opens its own, and both stay open while he moves between them.</p>
 *
 * <p>A `Map` keyed by object identity is the point, not an implementation detail: two Claude Code
 * tabs can both be called `main`, and a registry keyed by name would hand the second tab the first
 * tab's conversation. That failure is silent — the answer arrives, it is simply about somebody
 * else's question — which is why two of the plan gate's reviewers refused the label design.</p>
 *
 * <p><b>Two things the code round of 2026-09-08 changed.</b> A conversation now carries its own
 * `id`, stable for its whole life, because the tab KEY can move: a panel's callbacks closed over the
 * key they were created with, and after a `rekey` every message from that page was looked up under a
 * key nobody held any more — sends dropped, and a close that could not find its entry left the vendor
 * process running. Hooks take the `id`; only the map takes the key. And the LABEL moved out of the
 * entry into the registry, so a renamed tab can be relabelled without rebuilding anything: the label
 * is what the fallback in `sessionKey.ts` matches on, and a stale one would send it looking for a
 * name nobody wears.</p>
 */

/** What the registry needs from a panel: bring it forward, close it, and push it something. */
export interface RevealablePanel {
  reveal(): void;
  dispose(): void;
  post(message: unknown): void;
}

/** What the registry needs from a conversation: only that it can be ended. */
export interface DisposableSession {
  dispose(): void;
}

export interface ChatEntry {
  /**
   * This conversation, for as long as it exists.
   *
   * <p>Not the tab key: that can be replaced under a live tab, and the panel's own callbacks are
   * created once. Everything that has to survive a re-key — every hook the page calls — travels on
   * this instead.</p>
   */
  readonly id: object;
  readonly panel: RevealablePanel;
  readonly session: DisposableSession;
}

/** What `open` had to do, so a caller can say it out loud rather than infer it. */
export type OpenOutcome = 'revealed' | 'created';

/** One conversation and the name its tab wore when we last looked. */
interface Registered {
  readonly entry: ChatEntry;
  readonly label: string;
}

/**
 * One conversation per source tab.
 *
 * <p>A class, not a set of functions over a passed-in map: it is a stateful service and the house
 * rule says those stay classes. What it must never become is a place where state is edited from
 * outside — everything below returns rather than exposes the map, and the records inside it are
 * replaced rather than mutated.</p>
 */
export class ChatPanels {
  private readonly entries = new Map<object, Registered>();

  /**
   * Where each conversation currently sits, by its own id.
   *
   * <p>A second index rather than a scan. Every hook the page calls arrives with an id, so the
   * lookup is on the hot path of every message; a linear walk over the open tabs would work today
   * and become the wrong shape the moment anything streams. Maintained beside `entries` in the
   * four places that change it — open, rekey, close, closeAll — which is the cost of having it.
   * (gemini, the second code round.)</p>
   */
  private readonly whereById = new Map<object, object>();

  /** How many conversations are open. For the tests, and for a caller that wants to say so. */
  get size(): number {
    return this.entries.size;
  }

  has(key: object): boolean {
    return this.entries.has(key);
  }

  get(key: object): ChatEntry | undefined {
    return this.entries.get(key)?.entry;
  }

  /**
   * The conversation with this id, wherever its tab has got to.
   *
   * <p>The lookup every hook uses. A page created for tab A and re-keyed to B still calls back with
   * the id it was born with, and this is what turns that into the entry — which is the whole reason
   * the id exists.</p>
   */
  entryOf(id: object): ChatEntry | undefined {
    const key = this.whereById.get(id);

    return key === undefined ? undefined : this.entries.get(key)?.entry;
  }

  /** The key a conversation currently sits under, for a caller that must remove it by key. */
  keyOf(id: object): object | undefined {
    return this.whereById.get(id);
  }

  /** Every open panel, as the shape `sessionKey.ts` matches against. */
  known(): readonly { readonly key: object; readonly label: string }[] {
    return [...this.entries].map(([key, registered]) => ({ key, label: registered.label }));
  }

  /**
   * Reveal the conversation this tab already has, or create it.
   *
   * <p>`create` is a factory rather than a value so nothing is built for a tab that already has a
   * panel — building one would start a vendor process for a conversation nobody asked to begin.</p>
   */
  open(key: object, label: string, create: () => ChatEntry): { entry: ChatEntry; outcome: OpenOutcome } {
    const known = this.entries.get(key);
    if (known !== undefined) {
      known.entry.panel.reveal();

      return { entry: known.entry, outcome: 'revealed' };
    }

    const entry = create();
    this.entries.set(key, { entry, label });
    this.whereById.set(entry.id, key);

    return { entry, outcome: 'created' };
  }

  /**
   * The tab wears a new name.
   *
   * <p>The label is what the fallback in `sessionKey.ts` matches on when a tab's identity is lost, so
   * a cached one sends it looking for a name nobody wears — and it opens a second tab for a
   * conversation that already had one. (gemini, the code round.)</p>
   */
  relabel(key: object, label: string): boolean {
    const known = this.entries.get(key);
    if (known === undefined) {
      return false;
    }
    this.entries.set(key, { entry: known.entry, label });

    return true;
  }

  /**
   * Move an existing conversation onto a new key.
   *
   * <p>For the one case `sessionKey.ts` calls `rekey`: the host handed back a new `Tab` object for a
   * tab that is still open, and the panel would otherwise be orphaned beside it. Refuses rather than
   * overwrites when the destination is taken — two conversations must never collapse into one, and
   * this is the second door into that failure. Nothing is removed on a refusal.</p>
   */
  rekey(from: object, to: object): boolean {
    const known = this.entries.get(from);
    if (known === undefined || this.entries.has(to)) {
      return false;
    }
    this.entries.delete(from);
    this.entries.set(to, known);
    this.whereById.set(known.entry.id, to);

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
    const known = this.entries.get(key);
    if (known === undefined) {
      return false;
    }
    this.entries.delete(key);
    this.whereById.delete(known.entry.id);
    known.entry.session.dispose();

    return true;
  }

  /**
   * The same, for a caller that holds the conversation rather than the tab.
   *
   * <p>Every hook is passed an id, so without this each of them would have to bridge id to key
   * before closing — an asymmetric boundary where one call in the set works differently from the
   * rest, and the kind of seam a second, drifting cleanup path grows out of. (gemini, the second
   * code round.)</p>
   */
  closeById(id: object): boolean {
    const key = this.whereById.get(id);

    return key === undefined ? false : this.close(key);
  }

  /**
   * End everything — the extension is deactivating, and nothing may outlive it.
   *
   * <p>This one DOES dispose the panel, and that is the difference from `close`. Deactivation is not
   * a tab closing: nobody has told VS Code about these panels, so a session disposed without its
   * panel leaves a tab open that answers nothing — a zombie whose composer still takes text.
   * (gemini, the code round, twice.) The entry is removed BEFORE the panel is disposed, so the
   * `onDidDispose` this triggers finds nothing and returns rather than disposing a session twice.</p>
   */
  closeAll(): void {
    for (const key of [...this.entries.keys()]) {
      const known = this.entries.get(key);
      if (known === undefined) {
        continue;
      }
      this.entries.delete(key);
      this.whereById.delete(known.entry.id);
      known.entry.session.dispose();
      known.entry.panel.dispose();
    }
  }
}
