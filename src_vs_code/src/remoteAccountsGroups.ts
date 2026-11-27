import type { ManagerWord } from './remoteAccountsReply';
import type { Block, Cell, Head, NamedCell, TableModel, ToolName, ToolRow } from './remoteAccountsTable';

/**
 * The three groupings of the accounts table and the order rule (E2.S2, entry 4.1 of the parent plan). Pure, and it RUNS IN THE PAGE:
 * `viewOf` and `viewState` are tested in node as ordinary exports and embedded into the page BY ASSIGNMENT from their own source text
 * (`var viewOf = <source>`), as `pageTables.ts` embeds `compareRows`, which is handed to `viewOf` as an ARGUMENT — a call by name dies under the
 * minifier. So their bodies take no import but types, keep every helper inside, and hold no template literal and no backtick.
 *
 * <p>One model gives all three views and none hides a place, a tool, an account or a failure. A view holds only strings, numbers and `null`s taken
 * from the model, and the page draws them by `textContent`. A dash — `null` — sorts last in both directions and is never a zero.</p>
 */

export type GroupBy = 'server' | 'account' | 'user';
export type SortKey = 'name' | 'account' | 'left5h' | 'reset5h' | 'leftWeek' | 'resetWeek' | 'age';
export type SortDir = 'asc' | 'desc';

/** What the tab keeps in the webview's own state: nothing else, and no value of the model. */
export interface ViewState {
  readonly v: 1;
  readonly groupBy: GroupBy;
  readonly sort: { readonly key: SortKey; readonly dir: SortDir; readonly tool: ToolName };
}

/** `pageTables.ts`'s `compareRows`, handed in. */
export type Compare = (a: Record<string, unknown>, b: Record<string, unknown>, key: string, dir: SortDir) => number;

/** A place whose last read failed, as an account's row or a tool's line says it. */
export interface Failure {
  readonly said: string;
  readonly at: number | null;
}

/** One place of an account block: where it is signed in, with that read's instant. */
export interface AccountPlace {
  /** The place's id (a host, or a WSL user's key); `shown` is what the person reads. */
  readonly place: string;
  readonly shown: string;
  /** How long that read took, in ms, or `null`. */
  readonly tookMs: number | null;
  readonly readAt: number;
  readonly failure: Failure | null;
  readonly version: string | null;
}

/** An account's usage: stands ONCE on its block. */
export interface AccountUsage {
  readonly five: Cell;
  readonly week: Cell;
  readonly models: readonly NamedCell[];
  readonly span: string | null;
  readonly plan: string | null;
}

export interface AccountBlock {
  /** The account's key, or `null` for an account that is not known: that block belongs to one place. */
  readonly key: string | null;
  readonly text: string;
  readonly usage: AccountUsage;
  readonly places: readonly AccountPlace[];
}

/** A Codex or Antigravity row of one place, with the failure of that place's last read. */
export interface ToolLine {
  readonly place: string;
  readonly shown: string;
  readonly row: ToolRow;
  readonly failure: Failure | null;
}

/** A place that is in no Claude block, with its sentence and what a press on it may do. */
export interface NoClaude {
  readonly place: string;
  readonly shown: string;
  readonly said: string;
  readonly manager: ManagerWord | null;
  readonly action: 'read-again' | 'copy-install' | null;
  readonly canRefuse: boolean;
}

export interface UserRow {
  readonly place: string;
  readonly shown: string;
  readonly tookMs: number | null;
  readonly head: Head;
  readonly olderAt: number | null;
  readonly line: string | null;
  readonly canRefuse: boolean;
  readonly canReadAgain: boolean;
  readonly failure: Failure | null;
  /** One cell for each tool: the row, or `null` — the tool's dash. */
  readonly cells: { readonly 'claude-code': ToolRow | null; readonly codex: ToolRow | null; readonly agy: ToolRow | null };
}

export type View =
  | { readonly groupBy: 'server'; readonly blocks: readonly Block[] }
  | { readonly groupBy: 'account'; readonly accounts: readonly AccountBlock[]; readonly codex: readonly ToolLine[]; readonly agy: readonly ToolLine[]; readonly none: readonly NoClaude[] }
  | { readonly groupBy: 'user'; readonly rows: readonly UserRow[] };

/* eslint-disable complexity, max-lines-per-function -- `viewOf` and `viewState` are embedded in the page by their own source text: a function so embedded may name nothing outside itself, so every helper is a function INSIDE it and the two bodies cannot be split into smaller exports. Their parts are small and named; the rule that keeps a function short is kept by that, not by the line count. */
/** An item with the values it is ordered by (never part of the view) and the value the view holds. */
interface Sorted<T> {
  readonly s: Record<string, string | number | null>;
  readonly v: T;
}

/**
 * The view of `state.groupBy`, sorted by `state.sort`. EMBEDDED in the page by its source text: no import but types, every helper inside, no backtick
 * and no template literal. `compare` is `compareRows`, handed in.
 */
export function viewOf(model: TableModel, state: ViewState, compare: Compare): View {
  const by = state.sort;
  const RANK: Record<string, string> = { 'claude-code': '1 Claude Code', codex: '2 Codex', agy: '3 Antigravity' };

  /** The values of a row to order by: a tool's rank in Server, the place's name elsewhere. A time is an AGE, so the newest read is the smallest. */
  function rowValues(row: ToolRow, name: string): Record<string, string | number | null> {
    return {
      name, account: row.account.text, left5h: row.five.left, reset5h: row.five.resetsAt, leftWeek: row.week.left, resetWeek: row.week.resetsAt, age: 0 - row.readAt,
    };
  }

  /** The key of the state, then equal keys by the name, ascending in both directions; a dash is last in both (compareRows). */
  function order<T>(items: readonly Sorted<T>[]): T[] {
    return items.slice().sort(function (a, b) {
      const first = compare(a.s, b.s, by.key, by.dir);

      return first !== 0 ? first : compare(a.s, b.s, 'name', 'asc');
    }).map(function (one) { return one.v; });
  }

  function failureOf(block: Block): Failure | null {
    return block.head.kind === 'failed' ? { said: block.head.said, at: block.head.at } : null;
  }

  function ageOf(at: number | null): number | null {
    return at === null ? null : 0 - at;
  }

  function server(): View {
    const blocks = model.blocks.map(function (block): Sorted<Block> {
      const rows = order(block.rows.map(function (row): Sorted<ToolRow> { return { s: rowValues(row, RANK[row.tool]), v: row }; }));
      const chosen = rows.find(function (row) { return row.tool === by.tool; });
      const first = chosen === undefined ? null : rowValues(chosen, block.host);
      const pick = function (key: string): string | number | null { return first === null ? null : first[key]; };

      return {
        s: {
          name: block.host, account: pick('account'), left5h: pick('left5h'), reset5h: pick('reset5h'), leftWeek: pick('leftWeek'), resetWeek: pick('resetWeek'), age: ageOf(block.head.at),
        },
        v: { ...block, rows },
      };
    });

    return { groupBy: 'server', blocks: order(blocks) };
  }

  interface Member { readonly block: Block; readonly row: ToolRow }
  interface Group { readonly id: string; readonly key: string | null; readonly members: Member[] }

  function accountBlock(group: Group): Sorted<AccountBlock> {
    const withNumbers = group.members.filter(function (one) { return one.row.span === null; });
    const newest = function (list: readonly Member[]): Member {
      return list.reduce(function (best, one) { return one.row.readAt > best.row.readAt ? one : best; });
    };
    const source = newest(withNumbers.length > 0 ? withNumbers : group.members);
    const usage: AccountUsage = { five: source.row.five, week: source.row.week, models: source.row.models, span: source.row.span, plan: source.row.account.plan };
    const places = order(group.members.map(function (one): Sorted<AccountPlace> {
      return {
        s: { name: one.block.host, account: null, left5h: null, reset5h: null, leftWeek: null, resetWeek: null, age: 0 - one.row.readAt },
        v: { place: one.block.host, shown: one.block.shown, tookMs: one.block.tookMs, readAt: one.row.readAt, failure: failureOf(one.block), version: one.row.version },
      };
    }));

    return {
      s: {
        name: source.row.account.text, account: source.row.account.text, left5h: usage.five.left, reset5h: usage.five.resetsAt, leftWeek: usage.week.left, resetWeek: usage.week.resetsAt, age: 0 - newest(group.members).row.readAt,
      },
      v: { key: group.key, text: source.row.account.text, usage, places },
    };
  }

  function account(): View {
    const groups: Group[] = [];
    const codex: Sorted<ToolLine>[] = [];
    const agy: Sorted<ToolLine>[] = [];
    const none: Sorted<NoClaude>[] = [];
    model.blocks.forEach(function (block) {
      let hasClaude = false;
      block.rows.forEach(function (row) {
        if (row.tool === 'claude-code' && row.found) {
          hasClaude = true;
          const id = row.accountKey === null ? 'place:' + block.host : row.accountKey;
          let group = groups.find(function (one) { return one.id === id; });
          if (group === undefined) {
            group = { id, key: row.accountKey, members: [] };
            groups.push(group);
          }
          group.members.push({ block, row });
        } else if (row.tool === 'codex') {
          codex.push({ s: rowValues(row, block.host), v: { place: block.host, shown: block.shown, row, failure: failureOf(block) } });
        } else if (row.tool === 'agy') {
          agy.push({ s: rowValues(row, block.host), v: { place: block.host, shown: block.shown, row, failure: failureOf(block) } });
        }
      });
      if (!hasClaude) {
        const copy = block.head.kind === 'jq-missing' && block.head.manager !== null && block.head.manager !== 'none';
        none.push({
          s: { name: block.host, account: null, left5h: null, reset5h: null, leftWeek: null, resetWeek: null, age: ageOf(block.head.at) },
          v: { place: block.host, shown: block.shown, said: block.noClaude, manager: block.head.manager, action: block.canReadAgain ? 'read-again' : copy ? 'copy-install' : null, canRefuse: block.canRefuse },
        });
      }
    });

    return { groupBy: 'account', accounts: order(groups.map(accountBlock)), codex: order(codex), agy: order(agy), none: order(none) };
  }

  function user(): View {
    const rows = model.blocks.map(function (block): Sorted<UserRow> {
      const cellOf = function (tool: string): ToolRow | null {
        const found = block.rows.find(function (row) { return row.tool === tool; });

        return found === undefined ? null : found;
      };
      const cells = { 'claude-code': cellOf('claude-code'), codex: cellOf('codex'), agy: cellOf('agy') };
      const cell = cells[by.tool];
      const pick = function (key: string): string | number | null { return cell === null ? null : rowValues(cell, block.host)[key]; };

      return {
        s: {
          name: block.host, account: pick('account'), left5h: pick('left5h'), reset5h: pick('reset5h'), leftWeek: pick('leftWeek'), resetWeek: pick('resetWeek'), age: ageOf(block.head.at),
        },
        v: {
          place: block.host, shown: block.shown, tookMs: block.tookMs, head: block.head, olderAt: block.olderAt, line: block.line, canRefuse: block.canRefuse, canReadAgain: block.canReadAgain, failure: failureOf(block), cells,
        },
      };
    });

    return { groupBy: 'user', rows: order(rows) };
  }

  return state.groupBy === 'server' ? server() : state.groupBy === 'account' ? account() : user();
}

/** A stored state read safely: anything that is not exactly a known state is Server, by name, ascending, Claude Code. Embedded in the page, like `viewOf`. */
export function viewState(raw: unknown): ViewState {
  const GROUPS = ['server', 'account', 'user'];
  const KEYS = ['name', 'account', 'left5h', 'reset5h', 'leftWeek', 'resetWeek', 'age'];
  const DIRS = ['asc', 'desc'];
  const TOOLS = ['claude-code', 'codex', 'agy'];
  const fallback: ViewState = { v: 1, groupBy: 'server', sort: { key: 'name', dir: 'asc', tool: 'claude-code' } };
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    return fallback;
  }
  const one = raw as Record<string, unknown>;
  const sort = one.sort;
  if (typeof sort !== 'object' || sort === null || Array.isArray(sort)) {
    return fallback;
  }
  const by = sort as Record<string, unknown>;
  if (one.v !== 1 || GROUPS.indexOf(one.groupBy as string) < 0 || KEYS.indexOf(by.key as string) < 0 || DIRS.indexOf(by.dir as string) < 0 || TOOLS.indexOf(by.tool as string) < 0) {
    return fallback;
  }

  return { v: 1, groupBy: one.groupBy as GroupBy, sort: { key: by.key as SortKey, dir: by.dir as SortDir, tool: by.tool as ToolName } };
}

/* eslint-enable complexity, max-lines-per-function */
