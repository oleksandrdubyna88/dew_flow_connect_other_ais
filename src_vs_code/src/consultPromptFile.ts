import * as vscode from 'vscode';
import { CONSULT_PROMPT_PATH, consultPromptWrite } from './consultPrompt';
import { notify } from './notify';

/**
 * The consultant's prompt override, as a file on disk — read at paint, written on a keystroke pause.
 *
 * <p><b>The third extraction from `panelProvider.ts`</b>, per
 * `research/PLAN_the_panel_provider_is_too_big.md`. One field, three methods, and one thing reached
 * outside the cluster: `dataDir`, which is taken in under its own name so every line that uses it
 * stays a proven move.</p>
 *
 * <p>Its shape is the FIRST extraction's, not the second's: all three methods are private and the
 * panel calls two of them from three places, so they leave the class outright and those three call
 * sites point here instead. Nothing delegates.</p>
 */
export class ConsultPromptFile {
  /** The last reason a prompt write failed, so one unwritable disk is one message. */
  private promptWriteFailed = '';

  private readonly dataDir: vscode.Uri;

  constructor(dataDir: vscode.Uri) {
    this.dataDir = dataDir;
  }

  async readConsultPrompt(): Promise<string> {
    try {
      return new TextDecoder().decode(
        await vscode.workspace.fs.readFile(vscode.Uri.joinPath(this.dataDir, ...CONSULT_PROMPT_PATH)),
      );
    } catch {
      return ''; // no override, which is the ordinary state and means the shipped prompt
    }
  }

  /**
   * Writes what is in the box to the server's prompt override, or takes the override away.
   *
   * <p>Not `config.update`: the server reads its prompts from its own data directory, override-first,
   * so the file IS the setting. Writing a `coai.*` key beside it would have given one prompt two
   * homes, and the hand-edit the server has always supported would have been reverted by whichever
   * window mirrored next.</p>
   *
   * <p><b>Written beside it and renamed over it</b>, the way the settings file and an answered
   * escalation already are. `writeFile` truncates before it fills, so a host killed between the two
   * leaves the SERVER reading a half-written prompt — and the server reads its prompts override-first
   * without a second opinion, so a truncated one is simply what the consultant is asked. Raised by two
   * reviewers on this story's plan round.</p>
   *
   * <p>A failure is swallowed the way the settings write's is, and for the same reason: this runs
   * from a keystroke pause, and a disk that will not take a file is not something a panel can fix by
   * interrupting somebody about it. Nothing claims the prompt was saved — the box is repainted from
   * the FILE, so a write that did not land shows as the words coming back on the next paint.</p>
   */
  async saveConsultPrompt(value: unknown): Promise<void> {
    const write = consultPromptWrite(value);
    const target = vscode.Uri.joinPath(this.dataDir, ...CONSULT_PROMPT_PATH);
    try {
      if (write.kind === 'remove') {
        // Removing an override that was never written is the ORDINARY case, not an error — and it is
        // the ONLY one this swallows. It used to swallow every rejection, so a permission failure or
        // a provider error left the old prompt in force while the panel cleared its warning and the
        // restore looked as though it had worked. (CodeRabbit, on the pull request.)
        try {
          await vscode.workspace.fs.delete(target);
        } catch (error) {
          if (!(error instanceof vscode.FileSystemError) || error.code !== 'FileNotFound') {
            throw error;
          }
        }
        this.promptWriteFailed = '';
        return;
      }
      const directory = vscode.Uri.joinPath(this.dataDir, CONSULT_PROMPT_PATH[0]!);
      await vscode.workspace.fs.createDirectory(directory);
      const temp = vscode.Uri.joinPath(directory, `${CONSULT_PROMPT_PATH[1]}.${process.pid}.tmp`);
      await vscode.workspace.fs.writeFile(temp, new TextEncoder().encode(write.text));
      try {
        await vscode.workspace.fs.rename(temp, target, { overwrite: true });
      } catch (error) {
        // The temp name carries the pid, so a rename that keeps failing leaves one more file beside
        // the one the SERVER reads out of this directory. The settings writer already cleans up on
        // its own failure path for the same reason. (CodeRabbit, on the pull request.)
        await vscode.workspace.fs.delete(temp).then(undefined, () => undefined);
        throw error;
      }
      // The situation is over. A failure after this is news rather than a repeat.
      this.promptWriteFailed = '';
    } catch (e) {
      this.reportPromptFailure(e instanceof Error ? e.message : String(e));
    }
  }

  /**
   * Says, ONCE per distinct reason, that the consultant's prompt is not on disk.
   *
   * <p>The durable-status rule pointed at a text box: an action that failed must not look like one
   * that succeeded, and this one used to be swallowed entirely — a read-only data directory left a
   * person typing into a box whose words no consultation would ever read. Once per reason, and
   * cleared by the next successful write, because this runs from a keystroke PAUSE: a message per
   * pause over one unwritable disk is the other way to make it unusable. Raised twice on this
   * story's code round, in two roles.</p>
   */
  private reportPromptFailure(why: string): void {
    if (this.promptWriteFailed === why) {
      return;
    }
    this.promptWriteFailed = why;
    void notify({
      as: 'warning',
      class: 'failure',
      source: 'consultant',
      code: 'consultant-prompt-not-saved',
      title: `The consultant's prompt could not be saved, so consultations still use the previous one: ${why}`,
      detail: why,
    });
  }
}
