import { spawn } from 'node:child_process';
import { readFile, rename, unlink, writeFile } from 'node:fs/promises';

/**
 * The Windows side of a machine whose VS Code is attached to WSL.
 *
 * <p><b>Why this file exists.</b> Measured 2026-09-03: the same coai on both sides of one machine,
 * the same settings byte for byte, and every local round from WSL dies in zero seconds —
 * <c>Connection refused (127.0.0.1:11434)</c>, ten times. Two barriers, and fixing one is not
 * enough: Windows Ollama binds loopback only, and a WSL distro's own <c>127.0.0.1</c> is not the
 * Windows host's. The panel had the sentence for that already; what it did not have was any way to
 * tell "you have no engine" apart from "your engine is one hop away", or any way to act on it.</p>
 *
 * <p><b>What is deliberately NOT here: probing the network.</b> The first draft looked for the
 * engine at WSL's default gateway. Three gate reviewers refused it for three separate reasons, all
 * of them right — a panel-side discovery never reaches the server that runs the round; and
 * "the gateway is inside 172.16.0.0/12" is not a test for "this is the Windows host", it is a test
 * that names the office router on a corporate network in that range. So nothing here opens a socket
 * to any address. The only question asked of the outside is asked of THIS machine's Windows side,
 * through interop, and the answer is used to say a sentence rather than to send a review anywhere.</p>
 *
 * <p>Everything is pure except the four functions at the bottom, which are the only ones that touch
 * a process or a file — so the tests are shapes, not a machine with Ollama on it.</p>
 */

/** What `.wslconfig` currently asks for, as far as this product is concerned. */
export type NetworkingMode = 'mirrored' | 'nat';

/** The mode a file names, or `none` when it names nothing this build manages. */
export type ConfiguredMode = NetworkingMode | 'none';

/** The result of merging a mode into an existing file — including the case of refusing to. */
export interface WslconfigMerge {
  /** The merged text, or the original verbatim when nothing changed or the merge was refused. */
  readonly text: string;
  readonly changed: boolean;
  /** Empty when the merge is safe; otherwise why this file must not be rewritten. */
  readonly refused: string;
}

const SECTION = '[wsl2]';
const KEY = 'networkingMode';

/**
 * Whether this kernel is WSL, from the banner Microsoft builds into it.
 *
 * <p>The distinction is the whole point and it is not "am I on Linux": a native Linux box told to
 * edit <c>%USERPROFILE%\.wslconfig</c> and run <c>wsl --shutdown</c> has been given instructions for
 * a machine it is not. Raised by Gemini 3.7 Flash against the first draft of this plan, which had
 * exactly that bug written into it.</p>
 */
export function isWsl(procVersion: string): boolean {
  return /microsoft|wsl/i.test(procVersion);
}

/** The `networkingMode` set in the `[wsl2]` section, or `none`. */
export function networkingModeOf(text: string): ConfiguredMode {
  const value = valueInSection(text.split(/\r?\n/)).toLowerCase();

  return value === 'mirrored' || value === 'nat' ? value : 'none';
}

/**
 * The file with `networkingMode` set to `mode`, or a refusal.
 *
 * <p>Three rules, each of which is a way a naive rewrite damages a file that is global to every
 * distro on the machine:</p>
 * <ul>
 *   <li>The key goes INSIDE `[wsl2]`, not at the end of the file — appended after a following
 *       `[experimental]` it would be ignored, and the person would restart WSL for nothing.</li>
 *   <li>The file's own line endings are used. A `\n` inserted into a CRLF file is a file Notepad
 *       shows as a single line.</li>
 *   <li>A file that did not arrive as UTF-8 text is refused rather than merged. PowerShell's
 *       redirection still writes UTF-16, which reaches this function as NULs between the
 *       characters; "merging" that writes back rubbish.</li>
 * </ul>
 */
export function wslconfigWith(existing: string, mode: NetworkingMode): WslconfigMerge {
  const refused = unreadable(existing);
  if (refused.length > 0) {
    return { text: existing, changed: false, refused };
  }
  if (networkingModeOf(existing) === mode) {
    return { text: existing, changed: false, refused: '' };
  }

  const eol = existing.includes('\r\n') ? '\r\n' : '\n';
  const lines = existing.length > 0 ? existing.split(/\r?\n/) : [];
  const merged = withKey(lines, mode);
  const text = merged.join(eol);

  return { text: text.endsWith(eol) ? text : `${text}${eol}`, changed: true, refused: '' };
}

/**
 * The path WSL reads a Windows path through, or nothing when it is not one.
 *
 * <p>Nothing rather than a guess: a UNC path has no mount point, and inventing one would send a
 * write somewhere nobody asked for.</p>
 */
export function mountPathOf(windowsPath: string): string {
  const drive = /^([A-Za-z]):[\\/](.*)$/.exec(windowsPath);
  if (drive === null) {
    return '';
  }
  const rest = drive[2].replace(/\\/g, '/').replace(/\/+$/, '');

  return `/mnt/${drive[1].toLowerCase()}${rest.length > 0 ? `/${rest}` : ''}`;
}

/** The one line this product adds, for the message that shows it before anything is written. */
export function mirroredLines(mode: NetworkingMode): string {
  return `${SECTION}\n${KEY}=${mode}`;
}

/**
 * A file as a confirmation dialog can show it: whole when it is small, honestly cut when it is not.
 *
 * <p>The dialog used to show only the two lines being added, which cannot answer the question
 * somebody actually has before approving a change to a file every distro reads — do my other
 * settings survive this. So it shows the merged file. The bound is there because a modal is not a
 * text editor, and a cut that says it was cut is better than one that does not.</p>
 */
export function previewOf(text: string, maxLines = 40): string {
  const lines = text.replace(/\r\n/g, '\n').replace(/\n+$/, '').split('\n');

  return lines.length <= maxLines
    ? lines.join('\n')
    : `${lines.slice(0, maxLines).join('\n')}\n… ${lines.length - maxLines} more lines, unchanged`;
}

// ---------- the four that touch the world ----------

/** Whether the process is running under WSL. False on anything that cannot answer. */
export async function runningUnderWsl(): Promise<boolean> {
  try {
    return isWsl(await readFile('/proc/version', 'utf8'));
  } catch {
    return false;
  }
}

/**
 * What answers on the WINDOWS side of this machine, as a sentence, or nothing.
 *
 * <p>Asked through interop, so the request is made by a Windows process and its `127.0.0.1` is the
 * Windows loopback — which is exactly the address this side cannot reach. Nothing is sent anywhere:
 * the engine is asked its version, and the answer becomes a sentence in the panel.</p>
 *
 * <p>Empty for every failure, deliberately and identically: interop disabled in `/etc/wsl.conf`,
 * `/mnt/c` unmounted, no `curl.exe`, a timeout, a non-zero exit. "Not asked" and "nothing there"
 * lead to the same panel sentence, and a hang leads to neither — the child is killed on the
 * deadline.</p>
 */
export async function windowsSideEngine(timeoutMs = 2000): Promise<string> {
  // Both at once, and on a short deadline. Asked one after the other on a 4s budget this was up to
  // eight seconds of a frozen panel on a machine where nothing answers — which is exactly the
  // machine that reaches this code. Two loopback requests on the same box need none of it.
  //
  // Measured through real interop on 2026-09-03: launching `curl.exe` from a distro costs ~80ms,
  // the answering engine replies in ~100ms, and the DEAD candidate is what sets the pace — under
  // mirrored networking a port with nothing on it is dropped rather than refused, so that leg runs
  // to curl's own `-m 1` and the pair completes in ~1.05s. The deadline is 2s so that a loaded
  // machine has room: expiring here would report "nothing on the Windows side" and hide the very
  // button this exists to show.
  const asked = [
    { url: 'http://127.0.0.1:11434/api/version', engine: 'ollama' },
    { url: 'http://127.0.0.1:8000/v1/models', engine: 'vllm' },
  ];
  const answers = await Promise.all(asked.map(async (candidate) => {
    const version = versionIn(await interop('curl.exe', ['-s', '-m', '1', candidate.url], timeoutMs));

    return version.length > 0 ? `${candidate.engine} ${version}`.trim() : '';
  }));

  return answers.find((answer) => answer.length > 0) ?? '';
}

/** Where `.wslconfig` lives, as a path this side can open, or nothing when interop cannot say. */
export async function windowsWslconfigPath(timeoutMs = 4000): Promise<string> {
  // `cmd.exe` started from a Linux directory warns about UNC paths and lands in C:\Windows; run it
  // from a Windows-visible directory so the warning is not part of the answer.
  const profile = (await interop('cmd.exe', ['/c', 'echo', '%USERPROFILE%'], timeoutMs, '/mnt/c')).trim();
  const mounted = mountPathOf(profile);

  return mounted.length > 0 ? `${mounted}/.wslconfig` : '';
}

/** What a write did — which is not the same question as whether it went perfectly. */
export interface WriteOutcome {
  /** Whether the file on disk was REPLACED. The rename is the moment this becomes true. */
  readonly written: boolean;
  /** Empty when there is nothing to tell the person. */
  readonly message: string;
}

/**
 * Writes the file, or says why it did not. Never leaves a half-written one behind.
 *
 * <p>A temporary file beside the target, then a rename, then the file is read BACK and compared:
 * `.wslconfig` is global to every distro, and a truncated one is a machine that may not start its
 * containers. Telling somebody to restart WSL on the strength of a write that failed is the
 * specific outcome this guards — raised by two of the three plan reviewers independently.</p>
 *
 * <p><b>`written` and "went perfectly" are separate answers, and conflating them was a real defect.</b>
 * The rename is atomic; the read-back that follows it is not part of it. A read-back that fails, or
 * that sees somebody else's concurrent edit, used to be reported as a failed write — so a person
 * whose file HAD been replaced was told it had not, and pressing the button again would then read
 * `mirrored` from disk and offer to undo it. Both code reviewers found this independently. After the
 * rename the answer is "written, and here is what could not be confirmed".</p>
 */
export async function writeWslconfig(path: string, text: string): Promise<WriteOutcome> {
  const temporary = `${path}.coai-${process.pid}.tmp`;
  try {
    await writeFile(temporary, text, 'utf8');
  } catch (error) {
    return { written: false, message: `${path} could not be written: ${reason(error)}${await sweep(temporary)}` };
  }

  try {
    await rename(temporary, path);
  } catch (error) {
    return {
      written: false,
      message: `${path} could not be replaced, so nothing was changed: ${reason(error)}${await sweep(temporary)}`,
    };
  }

  try {
    return (await readFile(path, 'utf8')) === text
      ? { written: true, message: '' }
      : {
        written: true,
        message: `${path} was replaced, but reading it back shows something else — check it before `
          + 'restarting WSL; another editor may have written it in the same moment.',
      };
  } catch (error) {
    return {
      written: true,
      message: `${path} was replaced, but could not be read back to confirm it: ${reason(error)}`,
    };
  }
}

// ---------- pure helpers ----------

function reason(error: unknown): string {
  return (error as Error).message;
}

/**
 * Removes the temporary file, and says so when it could not be.
 *
 * <p>Swallowed, this hides the one thing worth knowing about a failed write on Windows: a file left
 * behind because an antivirus or a permission held it. Named rather than logged, because the panel
 * is where the person already is.</p>
 */
async function sweep(temporary: string): Promise<string> {
  try {
    await unlink(temporary);

    return '';
  } catch (error) {
    return ` (and ${temporary} could not be cleaned up: ${reason(error)})`;
  }
}

function unreadable(existing: string): string {
  return existing.includes('\u0000') || existing.includes('\uFFFD')
    ? 'this .wslconfig is not UTF-8 text (PowerShell writes UTF-16), so it is left untouched'
    : '';
}

/** The value of the key inside `[wsl2]`, or empty. Sections other than `[wsl2]` are not it. */
function valueInSection(lines: readonly string[]): string {
  const start = headerIndex(lines);
  if (start < 0) {
    return '';
  }
  for (let i = start + 1; i < lines.length && !isHeader(lines[i]); i += 1) {
    const found = /^\s*networkingMode\s*=\s*(.*?)\s*$/i.exec(lines[i]);
    if (found !== null) {
      return found[1];
    }
  }

  return '';
}

function withKey(lines: readonly string[], mode: NetworkingMode): string[] {
  const start = headerIndex(lines);
  if (start < 0) {
    const separator = lines.length > 0 && lines[lines.length - 1].trim().length > 0 ? [''] : [];

    return [...lines, ...separator, SECTION, `${KEY}=${mode}`];
  }

  const copy = [...lines];
  for (let i = start + 1; i < copy.length && !isHeader(copy[i]); i += 1) {
    if (/^\s*networkingMode\s*=/i.test(copy[i])) {
      copy[i] = `${/^\s*/.exec(copy[i])?.[0] ?? ''}${KEY}=${mode}`;

      return copy;
    }
  }
  copy.splice(start + 1, 0, `${KEY}=${mode}`);

  return copy;
}

function headerIndex(lines: readonly string[]): number {
  return lines.findIndex((line) => line.trim().toLowerCase() === SECTION);
}

function isHeader(line: string): boolean {
  return line.trim().startsWith('[');
}

/** The version out of an engine's answer, or empty when it did not answer like one. */
function versionIn(body: string): string {
  if (body.trim().length === 0) {
    return '';
  }
  try {
    const parsed = JSON.parse(body) as { version?: unknown; data?: unknown };
    if (typeof parsed.version === 'string') {
      return parsed.version;
    }

    // A vLLM answers `/v1/models` with a list and no version. It is still an engine.
    return Array.isArray(parsed.data) ? 'reachable' : '';
  } catch {
    return '';
  }
}

/**
 * One Windows process, bounded. Empty for every failure — see {@link windowsSideEngine}.
 */
async function interop(command: string, args: readonly string[], timeoutMs: number, cwd = '/mnt/c'): Promise<string> {
  return new Promise((resolve) => {
    let child: ReturnType<typeof spawn>;
    try {
      child = spawn(command, [...args], { cwd, stdio: ['ignore', 'pipe', 'ignore'] });
    } catch {
      return resolve('');
    }
    let output = '';
    let settled = false;
    const finish = (value: string): void => {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        resolve(value);
      }
    };
    const timer = setTimeout(() => {
      child.kill('SIGKILL');
      finish('');
    }, timeoutMs);
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString('utf8');
    });
    child.on('error', () => finish(''));
    child.on('close', (code) => finish(code === 0 ? output : ''));
  });
}
