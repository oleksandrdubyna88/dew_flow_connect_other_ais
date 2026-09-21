import * as vscode from 'vscode';

import { CallEnd } from './callHierarchy';
import { Editor, Preparation } from './callHierarchyAsk';

/**
 * The editor, as story 3.3 needs it — and the only place a language provider is touched.
 *
 * <p>Its own module rather than a function in `panelProvider.ts`, which is already 3 722 lines
 * against a cap of 800: adding sixty more to it was the first draft and a reviewer was right that a
 * file over the limit is not a place to put anything new.</p>
 *
 * <p><b>A file the checkout does not have makes `openTextDocument` THROW</b> — measured, not assumed
 * (`research/module_tests.md`, story 3.3's gate). That throw is what separates *the file is gone*
 * from *there is no provider*, so it is caught here and turned into an absent line rather than
 * allowed to look like a provider failure.</p>
 *
 * <p><b>There is no cancellation token to pass.</b> `vscode.prepareCallHierarchy`,
 * `vscode.provideIncomingCalls` and `vscode.provideOutgoingCalls` are commands taking a URI and a
 * position, or an item — none of them accepts one. A bounded wait is what exists, and a row that is
 * pressed again supersedes its own request rather than accumulating a second.</p>
 */

/** The adapter the pure flow takes. */
export function callHierarchyEditor(): Editor {
  return {
    lineText: textOfLine,
    prepare: prepared,
    incoming: async (handle) => endsOf(await asked<vscode.CallHierarchyIncomingCall>('vscode.provideIncomingCalls', handle)),
    outgoing: async (handle) => endsOf(await asked<vscode.CallHierarchyOutgoingCall>('vscode.provideOutgoingCalls', handle)),
  };
}

/** One line of a file, or nothing at all when the checkout does not have it. */
async function textOfLine(file: string, line: number): Promise<string | undefined> {
  try {
    const opened = await vscode.workspace.openTextDocument(vscode.Uri.file(file));

    return line < opened.lineCount ? opened.lineAt(line).text : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Every symbol the provider prepares at that position, with the ITEMS carried through.
 *
 * <p>The handles travel rather than a single one, because which item is the row's method is a
 * question the pure side answers. Handing back `items[0]` was the first draft's defect and three
 * reviewers found it: a provider returning `[Totals, counted]` would have had its class's callers
 * counted under the method's name.</p>
 */
async function prepared(file: string, line: number, character: number): Promise<Preparation> {
  const items = await vscode.commands.executeCommand<vscode.CallHierarchyItem[] | undefined>(
    'vscode.prepareCallHierarchy', vscode.Uri.file(file), new vscode.Position(line, character));

  return {
    items: (items ?? []).map((one) => ({ name: one.name, detail: one.detail ?? '' })),
    handles: items ?? [],
  };
}

async function asked<T>(command: string, handle: unknown): Promise<readonly T[]> {
  return await vscode.commands.executeCommand<T[]>(command, handle) ?? [];
}

/**
 * The far end of each call, as a place a person can be taken to.
 *
 * <p><c>selectionRange</c> is declared non-optional by the API and is nevertheless missing from some
 * providers' items, so the whole range is the fallback: a `TypeError` inside this map would fail the
 * direction it is in rather than say what it found.</p>
 */
function endsOf(
  calls: readonly { readonly from?: vscode.CallHierarchyItem; readonly to?: vscode.CallHierarchyItem }[],
): readonly CallEnd[] {
  return calls.flatMap((one) => {
    const item = one.from ?? one.to;

    return item === undefined ? [] : [endOf(item)];
  });
}

function endOf(item: vscode.CallHierarchyItem): CallEnd {
  const at = (item.selectionRange ?? item.range).start;

  return {
    name: item.name,
    file: item.uri.fsPath,
    line: at.line,
    character: at.character,
    detail: item.detail ?? '',
  };
}
