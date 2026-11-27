/**
 * Where the accounts round stops asking a place for Antigravity's quota (revision 3 of the accounts script, the review of PR #1066).
 *
 * <p>An unknown slash command is sent to the model as a prompt, and a real model turn usually outlives the four seconds the quota command is given. So
 * ANY outcome of `agy -p /quota` that is not a read (it asked the model, it printed no bucket this version knows, it did not answer in time, it ended with
 * an error) blocks the place at that agy version, and the place's first call then carries `no-agy-quota` until the version changes.</p>
 *
 * <p><b>The block is not part of the round's memory.</b> The memory is cleared by a Stop, by a closed terminal and by a refusal, and a place that is read
 * again after any of them must not start a model turn again. The block is kept here, by the place's key, and — in the window — in `globalState`, so every
 * window and a reload share it. It holds the place, the agy version and why: <b>no email, no account, no host text of a reply</b> (the version passes
 * a closed form and is cut at 32 characters). A stale write of a window can drop a block (the reason `globalState` is not trusted with a refusal); the
 * cost is one more quota call at that place, which is the call the block exists to prevent, so the write merges again into the copy it reads.</p>
 *
 * <p>Pure — no `vscode`: the store is handed a read and a write.</p>
 */

/** Why a place is blocked: the outcome of the quota command that was not a read. */
export type AgyWhy = 'model-call' | 'format' | 'timeout' | 'error';
export const AGY_WHYS: readonly AgyWhy[] = ['model-call', 'format', 'timeout', 'error'];

/** `version`: the agy version at which the command went wrong; `''` stands for a version that was not known. */
export interface AgyBlock {
  readonly version: string;
  readonly why: AgyWhy;
}

/** The `globalState` key of the blocks: an object by place key. */
export const AGY_BLOCKS_KEY = 'coai.terminalAccountsAgyBlocks';

/** The most places kept: a place past it is not remembered (it is asked again). */
export const AGY_BLOCKS_MAX = 64;

const VERSION = /^[0-9.]{0,32}$/u;
const PLACE_MAX = 200;

const isRecord = (value: unknown): value is Readonly<Record<string, unknown>> => typeof value === 'object' && value !== null && !Array.isArray(value);

const versionOk = (value: unknown): value is string => typeof value === 'string' && VERSION.test(value);

/** One stored value as a block, or `undefined` when it is not one: a closed version and a closed reason, nothing else. */
function blockOf(value: unknown): AgyBlock | undefined {
  if (!isRecord(value)) {
    return undefined;
  }
  const why = AGY_WHYS.find((one) => one === value['why']);
  const version = value['version'];

  return why !== undefined && versionOk(version) ? { version, why } : undefined;
}

/** The blocks of a stored value: a value that is not an object is no block, and so is any entry that is not whole. Never throws. */
export function agyBlocksOf(raw: unknown): ReadonlyMap<string, AgyBlock> {
  if (!isRecord(raw)) {
    return new Map();
  }
  const entries = Object.entries(raw).flatMap(([place, value]): readonly (readonly [string, AgyBlock])[] => {
    const block = blockOf(value);

    return block === undefined || place.length === 0 || place.length > PLACE_MAX ? [] : [[place, block]];
  });

  return new Map(entries.slice(0, AGY_BLOCKS_MAX));
}

/** What the round and the window do with the blocks. */
export interface AgyBlocks {
  readonly get: (place: string) => AgyBlock | undefined;
  /** `undefined` removes the place's block. */
  readonly set: (place: string, block: AgyBlock | undefined) => void;
  /** A person's press on a place (*Read this host again*): its block is gone, once. */
  readonly clear: (place: string) => void;
  /** A person's *Refresh now*: the blocks a timeout or an error made are gone, the ones a model turn or a format made stay. */
  readonly clearTransient: () => void;
}

const asObject = (blocks: ReadonlyMap<string, AgyBlock>): Readonly<Record<string, AgyBlock>> => Object.fromEntries(blocks);

/** The blocks over a read and a write of one stored value: each change reads the stored value again and merges into it. */
export function agyBlockStore(read: () => unknown, write: (value: Readonly<Record<string, AgyBlock>>) => void): AgyBlocks {
  const change = (apply: (blocks: Map<string, AgyBlock>) => void): void => {
    const blocks = new Map(agyBlocksOf(read()));
    apply(blocks);
    write(asObject(blocks));
  };

  return {
    get: (place) => agyBlocksOf(read()).get(place),
    set: (place, block) => {
      if (JSON.stringify(agyBlocksOf(read()).get(place)) === JSON.stringify(block)) {
        return;
      }
      change((blocks) => {
        if (block === undefined) {
          blocks.delete(place);
        } else if (blocks.has(place) || blocks.size < AGY_BLOCKS_MAX) {
          blocks.set(place, block);
        }
      });
    },
    clear: (place) => {
      if (agyBlocksOf(read()).has(place)) {
        change((blocks) => { blocks.delete(place); });
      }
    },
    clearTransient: () => {
      change((blocks) => {
        for (const [place, block] of [...blocks]) {
          if (block.why === 'timeout' || block.why === 'error') {
            blocks.delete(place);
          }
        }
      });
    },
  };
}

/** The blocks in memory only: a test's, and the window's before it has a store. */
export function agyBlocksInMemory(initial: unknown = undefined): AgyBlocks {
  let held: unknown = initial;

  return agyBlockStore(() => held, (value) => { held = value; });
}

/** The agy part of a reply, as far as the block cares. */
export interface AgyReply {
  readonly version: string | null;
  readonly usage: { readonly state: string; readonly why?: string };
}

const whyOf = (usage: AgyReply['usage']): AgyWhy | undefined => AGY_WHYS.find((one) => usage.state === 'failed' && one === usage.why);

/** A reply that skipped the quota names another version than the block's: the version is known, and it is not the one the block was made at. */
const newVersion = (had: AgyBlock | undefined, version: string | null): boolean => had !== undefined && version !== null && version !== had.version;

const blockedAt = (why: AgyWhy, version: string | null): AgyBlock => ({ version: version ?? '', why });

/** A read lifts a block, and so does a skipped reply that names another version. */
const lifted = (had: AgyBlock | undefined, agy: AgyReply): boolean => agy.usage.state === 'read' || (agy.usage.state === 'skipped' && newVersion(had, agy.version));

/**
 * What the reply of a first call does to a place's block: the new block, `undefined` when the block is lifted, `'keep'` when the reply says nothing about it. Pure.
 * ANY outcome of the quota command that is not a read blocks; a read lifts; a reply that did not ask the quota (`skipped`, `no-jq`) lifts only by naming another version;
 * no agy in the reply, or no version in it, changes nothing.
 */
export function agyBlockAfter(had: AgyBlock | undefined, agy: AgyReply | undefined): AgyBlock | undefined | 'keep' {
  if (agy === undefined) {
    return 'keep';
  }
  const why = whyOf(agy.usage);
  if (why !== undefined) {
    return blockedAt(why, agy.version);
  }

  return lifted(had, agy) ? undefined : 'keep';
}

/** The part of VS Code's `Memento` the store needs: the window's `globalState` is one. */
export interface AgyMemento {
  readonly get: (key: string) => unknown;
  readonly update: (key: string, value: unknown) => PromiseLike<void>;
}

/** The blocks kept in a window's `globalState`, under {@link AGY_BLOCKS_KEY}: every window and a reload see the same. Nothing but the place, the version and the reason is written. */
export const agyBlocksOnMemento = (memento: AgyMemento): AgyBlocks =>
  agyBlockStore(() => memento.get(AGY_BLOCKS_KEY), (value) => { void memento.update(AGY_BLOCKS_KEY, value); });
