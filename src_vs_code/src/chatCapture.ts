import * as os from 'node:os';
import * as vscode from 'vscode';
import { ChatPanels } from './chatPanels';
import { EditorText, confirmWholeFile, passageFromEditor } from './editorPassage';
import { windowsReach } from './hostSide';
import { oneAtATime } from './oneAtATime';
import { launch } from './processLauncher';
import { COPY_SCRIPT, RunOutcome, argvFor, captureSelection, ran } from './selectionCapture';
import { TabSnapshot, isOrdinaryEditorTab, sourceSession } from './sessionKey';
import { notifyAndAsk } from './notify';

/**
 * What this side reads out of the editor, the tabs and the clipboard.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. One module because it is one question asked twice:
 * both chat doors need the passage a person meant and the tab it came from, and they must read them
 * off ONE snapshot or they can see the tabs in two states — a defect this code already carries the
 * comments for.</p>
 *
 * <p>The decisions are not here. `captureSelection`, `passageFromEditor`, `confirmWholeFile` and
 * `sourceSession` are pure and tested as values; this is the half that goes to the host for them.</p>
 */

/** The tabs, narrowed to what `sessionKey` judges on. */
export function snapshots(): { active: TabSnapshot | undefined; all: TabSnapshot[] } {
  const all: TabSnapshot[] = [];
  for (const group of vscode.window.tabGroups.all) {
    for (const tab of group.tabs) {
      const input = tab.input as { viewType?: unknown; uri?: vscode.Uri } | undefined;
      all.push({
        key: tab,
        label: tab.label,
        viewType: typeof input?.viewType === 'string' ? input.viewType : '',
        // A `TabInputText` carries the document's uri and no viewType; a webview carries the
        // reverse. Reading both is what lets one snapshot answer for both doors.
        scheme: typeof input?.uri?.scheme === 'string' ? input.uri.scheme : '',
        // And the WHOLE uri, which is what a conversation opened from this tab is filed under and
        // later found by. `toString()` rather than `fsPath`, because that is the spelling a record
        // keeps and the one `sameSource` compares.
        uri: input?.uri === undefined ? '' : input.uri.toString(),
      });
    }
  }
  const activeTab = vscode.window.tabGroups.activeTabGroup.activeTab;

  return { active: all.find((tab) => tab.key === activeTab), all };
}

/**
 * Run the copy helper, and say how it finished.
 *
 * <p><b>The same launch on both sides of the machine, and that is measured rather than hoped.</b> In
 * a Remote-WSL window this is a Linux process starting a Windows one: `spawn` resolves a bare name
 * through the PATH, WSL's interop puts the Windows directories on it, and binfmt hands the PE over.
 * The extension host's own environment was inspected on the operator's machine — 33 Windows entries
 * including `WindowsPowerShell/v1.0`, `WSL_INTEROP` set — and the whole helper ran in 1.07 s against
 * the 6 s cap.</p>
 *
 * <p>`os.tmpdir()` is `/tmp` there, and it is the FASTER of the two candidates: 1.07 s against 1.4–1.9 s
 * from `/mnt/c`. So this line is unchanged, deliberately — a reviewer reading it should not take it
 * for an oversight. It matters not at all to the script, which is handed no path: `COPY_SCRIPT`
 * travels base64-encoded, opens nothing and takes no argument.</p>
 */
function pressCopy(): Promise<RunOutcome> {
  const child = launch('powershell.exe', argvFor(COPY_SCRIPT), { cwd: os.tmpdir() });

  return ran(child, (ms, run) => {
    const handle = setTimeout(run, ms);

    return () => clearTimeout(handle);
  });
}

/** The clipboard, as `selectionCapture` wants it. VS Code answers a Thenable, not a Promise. */
const hostClipboard = {
  read: async (): Promise<string> => vscode.env.clipboard.readText(),
  write: async (value: string): Promise<void> => {
    await vscode.env.clipboard.writeText(value);
  },
};

/**
 * Where the passage comes from, and a status line while it is being fetched.
 *
 * <p>The keybinding path takes about 1.7 seconds — PowerShell's own startup, mostly — and until the
 * tab appears there is nothing at all to see. A person who presses a shortcut and watches nothing
 * happen presses it again, which is how one question becomes two. The menu path is instant and says
 * nothing.</p>
 *
 * <p>The reach is asked once, here, and handed down: `captureSelection` is pure enough to be tested
 * against every side of the machine precisely because it does not go looking for one. Through interop
 * the round trip is ~1 s rather than the ~1.7 s this label was written for, so the label and the cap
 * both stand as they are.</p>
 */
/**
 * The keyboard door, and it opens once at a time.
 *
 * <p>Two presses arriving while the first is still probing the host both passed the entry point and
 * both started a capture — which is how one question becomes two. Showing the progress earlier (below)
 * makes the wait legible; it does NOT make the path single-entry, and an earlier draft of this fix
 * stopped at the progress and would have shipped as decoration.</p>
 */
const oneCapture = oneAtATime<{ text: string; failure: string }>({
  text: '',
  failure: 'That selection is still being copied — the second press was ignored.',
});

export async function passageFor(path: 'menu' | 'keyboard'): Promise<{ text: string; failure: string }> {
  if (path === 'menu') {
    return hostClipboard.read().then((text) => ({ text, failure: '' }));
  }

  return oneCapture(() => vscode.window.withProgress(
    { location: vscode.ProgressLocation.Window, title: 'Copying the selection…' },
    // THE PROBE IS INSIDE THE PROGRESS, which it was not. `windowsReach()` is about a second in a
    // remote window, and it ran before this notification existed — so the slowest part of the whole
    // gesture was the part with nothing on screen, which is precisely when a person presses again.
    async () => captureSelection(pressCopy, hostClipboard, await windowsReach()),
  ));
}


/** A refusal nobody needs to read: the person just cancelled the question that caused it. */
export const CANCELLED = '\u0000';

/**
 * The passage from the active editor — and the one question this door asks before it sends.
 *
 * <p>A SELECTION goes without a word, however large: choosing it was the choice. An empty selection
 * sends the whole document, which the operator chose over a refusal — but over a bound it asks
 * first, because the accident this guards against is the chord pressed to focus a window, in a
 * minified bundle, becoming a paid turn nobody meant. Cancelling is not an error and says nothing
 * more: the person has just been asked and has just answered.</p>
 */
export async function fromTheEditor(): Promise<{ text: string; failure: string }> {
  const editor = editorText();
  const passage = passageFromEditor(editor);
  if (!passage.ok) {
    return { text: '', failure: passage.refusal };
  }
  if (!passage.whole || editor === undefined) {
    return { text: passage.text, failure: '' };
  }
  const ask = confirmWholeFile(editor);
  if (ask.length === 0) {
    return { text: passage.text, failure: '' };
  }
  const said = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'chat',
    code: 'send-the-whole-passage',
    modal: true,
    title: ask,
    action: 'Send all of it',
  });
  if (said === 'Send all of it') {
    return { text: passage.text, failure: '' };
  }

  // CANCELLED, which is not a failure and needs no sentence: the person was asked a question one
  // second ago and answered it. A refusal here showed an empty warning box. (codex, the code round.)
  return { text: '', failure: CANCELLED };
}

/**
 * Which conversation this belongs to, over ONE snapshot of the tabs.
 *
 * <p>Both doors judged from the same list, rather than two lookups that could see the tabs in two
 * states — and it is one function because two commands ask the same question now. `claude` is
 * `undefined` when the source is a file, which is what tells the caller where the passage comes
 * from. (gemini, the code round.)</p>
 */
export function matchedSource(panels: ChatPanels): {
  claude: ReturnType<typeof sourceSession>;
  source: ReturnType<typeof sourceSession>;
  /** The active document's uri, from the SAME snapshot — what a file conversation is filed under. */
  uri: string;
} {
  const { active, all } = snapshots();
  const known = panels.known();
  const claude = sourceSession(active, all, known);

  const matched = claude ?? sourceSession(active, all, known, isOrdinaryEditorTab);

  return {
    claude,
    source: matched,
    // THE MATCHED TAB'S uri, not the active one's. They are the same tab only when a person presses
    // from the editor; from the chat panel itself — which is how *add the question* is used — the
    // active tab is a webview with no document, while the match falls back through `all` to the
    // editor. Reading `active` there filed the conversation with no source at all, leaving it
    // unmatchable by *go to* and unfollowable by a rename. Still from THIS snapshot, so the two
    // cannot see the tabs in two states. (gemini, the code round.)
    uri: all.find((tab) => tab.key === matched?.key)?.uri ?? '',
  };
}

/**
 * The active editor, narrowed to the two strings the decision needs — and only when it is the tab
 * the conversation is being keyed to.
 *
 * <p>`matchedSession` reads the active TAB and this reads the active EDITOR, and in a split with the
 * focus somewhere else those are two different documents. Then the conversation would be named after
 * one file and carry the text of another. They are checked against each other rather than assumed
 * equal. (gemini, the code round.)</p>
 */
function editorText(): EditorText | undefined {
  const editor = vscode.window.activeTextEditor;
  if (editor === undefined) {
    return undefined;
  }
  const tab = vscode.window.tabGroups.activeTabGroup.activeTab?.input as { uri?: { toString?: () => string } } | undefined;
  const named = typeof tab?.uri?.toString === 'function' ? tab.uri.toString() : '';
  if (named.length > 0 && named !== editor.document.uri.toString()) {
    return undefined;
  }

  return {
    whole: editor.document.getText(),
    selected: editor.document.getText(editor.selection),
    // The file name rather than the path: it is for a sentence a person reads, and the path is
    // already in the tab they are looking at.
    name: editor.document.uri.path.split('/').pop() ?? 'this file',
  };
}
