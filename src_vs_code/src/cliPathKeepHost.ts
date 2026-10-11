import * as vscode from 'vscode';
import { droppedNoticeText, type DroppedValue } from './cliPathKeep';
import { CliPathKeeper } from './cliPathKeeper';
import { notify, notifyOnceThen, notifyResolved } from './notify';
import { reportRefusal, saveSetting, userLayer, writesTheOverlay } from './sideConfig';
import { vendorsFrom, type Vendor } from './vendors';

/**
 * The host half of the per-side CLI-path keeper (todo/PLAN_paths_per_side.md, E3 as built): the catalog rows of the user
 * layer, the copies in `globalState`, the notice through the funnel, and the one settings write. Every decision is
 * `cliPathKeep.ts`'s and `cliPathKeeper.ts`'s; this is the `vscode` around them.
 *
 * <p><b>Why only the user layer.</b> A side that keeps its own settings (`coai.perSideSettings`) reads its rows from its own
 * overlay, which only this side writes — another side's save cannot drop a member there.</p>
 *
 * <p><b>Why a notice and not a page button (yet).</b> The owner's rule is that every extension action is a panel button.
 * The Models card is drawn by E4 (two CLI-path fields); E3 changes no page. So for E3 the notice's **Restore** is the
 * action, and E4 draws the same restore beside the field — recorded in the plan.</p>
 */

/** The notice's one button. */
export const RESTORE = 'Restore';

/** The notice's code — keyed per row (`DroppedValue.key`), so two rows are two notices and fixing one clears one. */
const DROPPED = 'cli-path-value-dropped';

/** Starts the keeper: one look now, and the caller looks again on every `coai` settings change. */
export function startCliPathKeep(context: vscode.ExtensionContext): CliPathKeeper<Vendor> {
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  const keeper: CliPathKeeper<Vendor> = new CliPathKeeper<Vendor>({
    store: context.globalState,
    layer: () => (writesTheOverlay(config(), 'vendors') ? undefined : 'user'),
    rows: () => vendorsFrom(userLayer(config())('vendors')),
    write: async (rows) => {
      try {
        await saveSetting(context, config(), 'vendors', rows);
      } catch (error: unknown) {
        reportRefusal(context, 'vendors', error);
      }
    },
    say: (dropped) => { offer(keeper, dropped); },
    resolved: (subject) => { notifyResolved(DROPPED, subject); },
    refused: (dropped, reason) => { void notify(refusedNotice(dropped, reason)); },
    failed: (error) => { console.error('ConnectOtherAIs: the per-side CLI paths could not be kept', error); },
  });
  void keeper.check();

  return keeper;
}

/** The notice, once per run and per row, with **Restore** — which re-reads the row at the click. */
function offer(keeper: CliPathKeeper<Vendor>, dropped: DroppedValue): void {
  const text = droppedNoticeText(dropped);
  void notifyOnceThen(
    { as: 'warning', class: 'offer', source: 'cliPathKeep', code: 'cli-path-value-dropped', subject: dropped.key, title: text.title, cure: text.cure, action: RESTORE },
    (answer) => {
      if (answer === RESTORE) {
        void keeper.restore(dropped);
      }
    },
  );
}

/** A click that found the row changed since the offer: nothing written, and why. */
function refusedNotice(dropped: DroppedValue, reason: string): Parameters<typeof notify>[0] {
  return {
    as: 'warning',
    class: 'refusal',
    source: 'cliPathKeep',
    code: 'cli-path-restore-refused',
    subject: dropped.key,
    title: `Nothing was restored for the model "${dropped.id}": ${reason}.`,
    cure: 'Its CLI path is what your settings hold now. If a previously saved value is still missing, it is offered again.',
  };
}
