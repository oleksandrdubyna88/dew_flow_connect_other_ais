import { CoaiSettings } from './settingsShape';
import { serverSettingsJson } from './serverSettingsFile';
import { Vendor } from './vendors';

/** Everything the sync needs from the editor, so the sync itself needs nothing from it. */
export type ReadConfiguration = () => { settings: CoaiSettings; vendors: readonly Vendor[] };
export type WriteFile = (json: string) => Promise<void>;

/**
 * Mirrors the workspace's `coai.*` settings into the file the server reads.
 *
 * <p><b>Why this is not the panel's job.</b> It used to be, and that was the defect: the write sat
 * in `PanelProvider.render()` behind the view guard, and the `onDidChangeConfiguration`
 * subscription was registered inside `resolveWebviewView`. VS Code resolves a webview view lazily
 * — only when somebody first opens it — so in a window where nobody had opened the ConnectOtherAIs
 * panel, nothing watched the settings and nothing mirrored them. A person could set
 * `coai.onExhausted` to `good_enough`, restart everything, and the server would go on answering
 * `call_human` from whatever `env` block had been pasted into a client config months before.</p>
 *
 * <p>The server needs its settings whether or not a person is looking at a webview, so this takes
 * no view, holds no VS Code type, and is created at activation.</p>
 */
export class ServerSettingsSync {
  /** The last content actually written, so an unchanged configuration touches nothing. */
  private lastWritten = '';

  constructor(private readonly read: ReadConfiguration, private readonly write: WriteFile) {}

  /**
   * Writes the settings file when — and only when — its content would change.
   *
   * <p>The server reloads on this file's mtime and length (`PanelServiceHost`), and the panel
   * repaints on every live poll, so rewriting identical content would ask the server to re-read
   * its settings several times a minute for nothing.</p>
   *
   * <p>A failed write is not remembered as done: the disk being unwritable is not worth
   * interrupting anyone over, but the next change must still try.</p>
   */
  async sync(): Promise<void> {
    const { settings, vendors } = this.read();
    const json = serverSettingsJson(settings, vendors);
    if (json === this.lastWritten) {
      return;
    }

    try {
      await this.write(json);
      this.lastWritten = json;
    } catch {
      // Not writable. The pasted env block remains a way in, and this runs from a configuration
      // listener — throwing here would put an extension error in front of somebody for every
      // keystroke in their settings file, over a disk problem a settings panel cannot fix.
    }
  }
}
