import { type PanelFocus, withholdsRepaint } from './panelView';
import { isDisposedRejection, ViewHandle } from './viewHandle';

/**
 * One webview the panel paints — the sidebar today, the Settings tab beside it — and the three things
 * that belong to IT rather than to the panel: which view is held, what was last painted into it, and
 * whether somebody is typing in it.
 *
 * <p>These were single fields on `PanelProvider` while there was one webview, and two defects lived in
 * them (`todo/PLAN_settings_page.md`):</p>
 * <ul>
 *   <li><b>F2</b> — the painted key survived a new view. VS Code re-creates a view it disposed on hide,
 *       and a view resolved again with an unchanged state matched the old key, skipped the html write
 *       and was posted live regions into an empty document. {@link attach} forgets what was painted.</li>
 *   <li><b>F3</b> — a late disposal of a REPLACED view cleared the LIVE view's edit hold, because only
 *       the handle release was ownership-checked. {@link detach} forgets the hold only for the view it
 *       actually holds.</li>
 * </ul>
 *
 * <p>No `vscode` import, like `viewHandle.ts`, so every rule here is run by a test.</p>
 */

export interface SurfaceWebview {
  html: string;
  postMessage(message: unknown): Thenable<boolean>;
}

export interface SurfaceView {
  readonly webview: SurfaceWebview;
}

/** What a paint did — so the caller knows whether the live regions still have to be posted. */
export type PaintStep = 'painted' | 'patch' | 'gone';

export class SurfaceSlot<V extends SurfaceView = SurfaceView> {
  private readonly handle = new ViewHandle<V>();

  private painted = '';

  /** When focus was gained (0 = nobody is editing), which control, and where the caret was. */
  private since = 0;

  private editingId = '';

  private caret: readonly [number, number] = [0, 0];

  constructor(private readonly now: () => number = Date.now) {}

  get view(): V | undefined {
    return this.handle.view;
  }

  /** A new document: nothing in it is painted yet, and nothing in it is focused. */
  attach(view: V): void {
    this.handle.hold(view);
    this.painted = '';
    this.forgetEditing();
  }

  /** Only the view this slot holds is let go of; a late callback from a replaced one changes nothing. */
  detach(view: V): void {
    if (this.handle.view !== view) {
      return;
    }
    this.handle.release(view);
    this.forgetEditing();
  }

  /**
   * The page reported focus in or out of a control.
   *
   * <p>Only the FIRST focus starts the hold's clock: tabbing from one control to the next must not renew
   * it, because a cap a focus change renews is a cap with no bound. The id and the caret are refreshed
   * every time — the paint that eventually lands must find the control the person is in NOW.</p>
   *
   * @returns true when editing ENDED, which is when the withheld paint may land
   */
  edited(editing: boolean, id: string, start: number, end: number): boolean {
    if (!editing) {
      this.forgetEditing();

      return true;
    }
    if (this.since === 0) {
      this.since = this.now();
    }
    this.editingId = id;
    this.caret = [start, end];

    return false;
  }

  /** Where to put the caret back after a paint, or nothing when nobody is editing here. */
  focus(): PanelFocus | undefined {
    return this.since > 0 ? { id: this.editingId, start: this.caret[0], end: this.caret[1] } : undefined;
  }

  /** The next paint writes html even if nothing drawn changed — a refused write snapping back. */
  forceRepaint(): void {
    this.painted = '';
  }

  /**
   * Paints when what is drawn changed and nobody is typing here; otherwise says to patch.
   *
   * <p>The key is recorded only AFTER the write succeeded. Recorded first, a disposal between the two
   * left this slot claiming a paint it never made, and the view VS Code creates next would match the key
   * and receive live regions into an empty document. Only the disposal is swallowed.</p>
   *
   * @param html built only when a paint is due — the key is cheap, the page is not
   */
  paint(key: string, html: () => string): PaintStep {
    const view = this.handle.view;
    if (view === undefined) {
      return 'gone';
    }
    if (!this.due(key)) {
      return 'patch';
    }
    if (!written(view, html)) {
      return 'gone';
    }
    this.painted = key;

    return 'painted';
  }

  /** What is drawn changed, and nobody is typing on this page (or the hold ran out). */
  private due(key: string): boolean {
    return key !== this.painted && !withholdsRepaint(this.since, this.now());
  }

  /** A message to the held view, if any — a disposal is expected and dropped, anything else is said. */
  post(message: unknown, failure = 'the panel could not be updated'): void {
    this.handle.view?.webview.postMessage(message).then(undefined, (error: unknown) => {
      if (!isDisposedRejection(error)) {
        console.error(`ConnectOtherAIs: ${failure}`, error);
      }
    });
  }

  private forgetEditing(): void {
    this.since = 0;
    this.editingId = '';
    this.caret = [0, 0];
  }
}

/** The html written, or false when the view was disposed under the write — anything else is thrown. */
function written(view: SurfaceView, html: () => string): boolean {
  try {
    view.webview.html = html();

    return true;
  } catch (error) {
    if (!isDisposedRejection(error)) {
      throw error;
    }

    return false;
  }
}

/** Whether ANY of the slots holds a view — what a probe asks before spending anything on a page. */
export function anyHeld(slots: readonly SurfaceSlot[]): boolean {
  return slots.some((slot) => slot.view !== undefined);
}
