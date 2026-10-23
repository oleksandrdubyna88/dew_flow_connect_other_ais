import { KIMI_ON_LINUX_SINCE, KIMI_ON_NATIVE_LINUX_SINCE } from './runtimeSince';

/**
 * The ⤓ / ⟳ line for Kimi on a Linux host — DL12 of research/PLAN_kimi_and_vibe_on_linux.md (E6).
 *
 * <p>`rg` and `fd` are provisioned by the install button, because the owner's two rules (set it up once; every action is a
 * button) exclude "install them by hand", and coai-mcp refuses a Kimi launch when either does not resolve. One press, each step
 * guarded and idempotent, in this order:</p>
 *
 * <ol>
 *   <li><b>`rg`</b>: skipped when `rg` resolves or `~/.kimi-code/bin/rg` exists; else `sudo apt-get install -y ripgrep` where
 *       `apt-get` exists, `sudo dnf install -y ripgrep` where `dnf` does — the projects' own README instructions, chosen by
 *       `command -v` inside the line.</li>
 *   <li><b>`fd`</b>: skipped when `fd` resolves, `~/.kimi-code/bin/fd` exists, <i>Debian's `fdfind` already resolves</i> or the
 *       line's own link is already there (those last two so that a second press asks for no password); else the `fd-find`
 *       package.</li>
 *   <li><b>The `fd` link</b> (the plan gate for epics 4–6, 2026-10-16, F7 + F17; reshaped by the review of 2026-10-18, G): Debian and Ubuntu name fd's
 *       program `fdfind` and Kimi looks for `fd`; Fedora's `fd-find` installs `fd` itself. The link is judged by what is at `~/.local/bin/fd`, and
 *       made only when it is needed: when `fdfind` is a program (a regular, executable file — an alias or a function is not) and `fd` does not
 *       resolve already, and the path holds NOTHING of the person's — not a REAL file (the plan's own addition: the link replaces a link, never
 *       somebody's file), and not a link of theirs that still resolves to a file (left alone; only a link that points at nothing, or at a folder,
 *       is replaced). Only then is `~/.local/bin` made — a host that needs no link does not get an empty `~/.local` — and the link made with `ln -sfn` (never `-sf` alone: it follows a planted
 *       symlink to a DIRECTORY and writes inside it) on that exact path alone. <b>The target is ABSOLUTE</b>: Debian's own `/usr/bin/fdfind` when it is
 *       there (the distribution's file, not whichever `fdfind` an earlier PATH entry holds), else what `command -v` answers, made absolute from
 *       the current folder when a relative PATH entry produced it.</li>
 *   <li><b>The one sentence for any other distribution</b>: when a tool was missing and neither `apt-get` nor `dnf` resolves
 *       the line installs nothing for it, sets a flag, and says — once, whichever tools were missing — {@link KIMI_NO_PACKAGE_MANAGER};
 *       then it goes on to Moonshot's script. coai does not guess a third package manager's spelling.</li>
 *   <li><b>Moonshot's own script</b>, as before.</li>
 *   <li><b>The check</b> (F10; H of the review of 2026-10-18): the line ENDS by resolving `rg` and `fd` the way the SERVER will look
 *       (`KimiTools.Directories`) — the person's Kimi `bin`, then PATH, then `~/.local/bin` — and printing where each resolved, or
 *       `not found`, so a press that installed nothing usable says so in the terminal rather than at the next launch. The order is the
 *       server's, so the terminal never names a program the launch would not pick.</li>
 * </ol>
 *
 * <p>Steps are joined by `&&`, and <b>each step that contains a `||` is wrapped in `{ …; }`</b>: without the braces `a || b && c || d`
 * reads as `((a || b) && c) || d`, so a failed step would hand control to the NEXT step's own `||` and the line would walk on —
 * the defect the stand-in run found. A package manager that fails (a password declined, no network) now STOPS the line, as a
 * failed winget step stops the Windows one. The line is POSIX-shell syntax — bash, zsh and dash read it — and uses `coai_`-prefixed variables
 * that it unsets when it succeeds, so a terminal is not left holding them. <b>Every word of it is read by `fromOfficialSources`</b>
 * (`officialSources.ts`): the four `sudo` steps are admitted because each is `sudo apt-get install -y <package>` or `sudo dnf
 * install -y <package>` for the two packages and nothing else; and the sentence below carries a capital "I" because that check
 * refuses a step whose words include the lower-case verb `install` without reading it as one.</p>
 *
 * <p>The line is RUN against stand-ins by `kimiLinuxInstall.test.ts` (every arm of S-K17), and parsed by `bash -n`.</p>
 */

/** DL12's generic instruction, as the line prints it. The server's `KimiTools.Refusal` says the same sentence (lower-case first letter). */
export const KIMI_NO_PACKAGE_MANAGER = 'Install ripgrep and fd with your distribution\'s package manager, or place them on PATH or in ~/.local/bin';

/** The flag the line sets when a tool was missing and no package manager it knows exists. */
const NO_MANAGER = 'coai_no_pm';

const QUIET = '>/dev/null 2>&1';
const KIMI_BIN = '~/.kimi-code/bin';
const LOCAL_BIN = '~/.local/bin';

/**
 * A look-up that must end in a FILE (and that sets aliases aside — inside the substitution only, the person's terminal keeps its own): `command -v` answers the TEXT of an alias in an interactive terminal (`alias fd='fdfind'`), the name
 * of a function or a builtin, and a path that may be a directory — none of which `KimiTools` will find on disk. So the answer goes into
 * a variable and must be a regular file that is executable (`-x` alone accepts a directory); an alias-only `fd` is a MISSING `fd`.
 */
function found(name: string, variable: string): string {
  return `{ ${variable}=$(unalias -a 2>/dev/null; command -v ${name} 2>/dev/null) && [ -f "$${variable}" ] && [ -x "$${variable}" ]; }`;
}

/**
 * One tool's step: skipped when it is there, else the distribution's package for it, else the flag.
 *
 * @param tool the program Kimi looks for
 * @param distroPackage the package of that name in `OFFICIAL_SOURCES.distroPackages`
 * @param alsoThere further ways the tool can already be there, each a complete `||` operand
 */
function toolStep(tool: string, distroPackage: string, alsoThere: readonly string[]): string {
  const there = [found(tool, 'coai_f'), `[ -f ${KIMI_BIN}/${tool} ]`, ...alsoThere];

  return `{ ${there.join(' || ')} || if command -v apt-get ${QUIET}; then sudo apt-get install -y ${distroPackage}; `
    + `elif command -v dnf ${QUIET}; then sudo dnf install -y ${distroPackage}; else ${NO_MANAGER}=1; fi; }`;
}

/** Debian's own file for fd's program — preferred as the link's target over whichever `fdfind` an earlier PATH entry holds. */
const DEBIAN_FDFIND = '/usr/bin/fdfind';

/**
 * The link `~/.local/bin/fd` → `fdfind`, in one group: the target first (Debian's own file, else what `command -v` answers, made absolute), then
 * every reason to leave things alone — no program called `fdfind`, an `fd` that resolves already, a REAL file or a resolving link of the person's
 * own at the path — and only then the folder and the link. `ln -sfn` replaces a dangling link, or one that points at a folder, by name.
 */
const FD_LINK = `{ coai_d=${DEBIAN_FDFIND}; { [ -f "$coai_d" ] && [ -x "$coai_d" ]; } || coai_d=$(unalias -a 2>/dev/null; command -v fdfind 2>/dev/null); `
  + 'case "$coai_d" in /*) ;; ?*) coai_d="$PWD/$coai_d";; esac; '
  + `! { [ -f "$coai_d" ] && [ -x "$coai_d" ]; } || ${found('fd', 'coai_g')} `
  + `|| { [ -L ${LOCAL_BIN}/fd ] && [ -f ${LOCAL_BIN}/fd ]; } || { [ -e ${LOCAL_BIN}/fd ] && [ ! -L ${LOCAL_BIN}/fd ]; } `
  + `|| { mkdir -p ${LOCAL_BIN} && ln -sfn "$coai_d" ${LOCAL_BIN}/fd; }; }`;

/** The one sentence for a host with neither manager — printed once, only when a tool was missing and nothing could provide it. */
const NO_MANAGER_NOTE = `{ [ -z "\${${NO_MANAGER}-}" ] || echo "${KIMI_NO_PACKAGE_MANAGER}"; }`;

/** Where `rg` and `fd` resolved, in the order the SERVER looks (`KimiTools.Directories`): Kimi's own bin, PATH, `~/.local/bin`. */
const CHECK = '{ for coai_t in rg fd; do coai_p=; '
  + `for coai_c in ${KIMI_BIN}/$coai_t "$(unalias -a 2>/dev/null; command -v $coai_t 2>/dev/null)" ${LOCAL_BIN}/$coai_t; do [ -f "$coai_c" ] && [ -x "$coai_c" ] && { coai_p=$coai_c; break; }; done; `
  + 'echo "$coai_t: ${coai_p:-not found}"; done; unset coai_no_pm coai_t coai_p coai_c coai_f coai_g coai_d; }';

/** The whole line: tools, link, the sentence, Moonshot's script, the check. */
export const KIMI_LINUX_LINE = [
  toolStep('rg', 'ripgrep', []),
  toolStep('fd', 'fd-find', [found('fdfind', 'coai_f'), `[ -f ${LOCAL_BIN}/fd ]`]),
  FD_LINK,
  NO_MANAGER_NOTE,
  'curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash',
  CHECK,
].join(' && ');

/** What the card says about the install button on a Linux host — the tools, the link, the check; the sign-in sentence is `vendorTerminal.ts`'s own. */
export const KIMI_TOOLS_NOTE =
  'One press: where ripgrep (`rg`) or fd is missing — coai-mcp refuses a Kimi launch without them — it installs them from your '
  + 'distribution’s own packages with `sudo apt-get` or `sudo dnf` (you type your password in this terminal). A step is skipped '
  + 'when the tool is already there, and a failed tool step stops the line. Debian and Ubuntu name fd’s program `fdfind`, so the '
  + 'line links `~/.local/bin/fd` to it. On any other distribution it installs nothing for the tools, says so — install ripgrep '
  + 'and fd with your package manager, or place them on PATH or in `~/.local/bin` — and goes on. Then Moonshot’s own installer, '
  + 'from code.kimi.com, puts Kimi in `~/.kimi-code/bin` and edits one shell rc file so that folder is on your PATH (open a new '
  + 'shell, or restart VS Code); it is a piped script — read it first if that matters to you. The line ends by printing where '
  + '`rg` and `fd` resolved. If apt says it cannot find ripgrep or fd-find, run `sudo apt-get update` once and press the button again. '
  + 'The tools must be on the PATH the VS Code server sees (system folders or `~/.local/bin`), not only on a PATH that `.bashrc` adds.';

/** The sentence about the person's own `kimi` (Q3 of the plan): the wait is deliberate, and the Busy answer names its reason. */
export const KIMI_OWN_OPEN =
  'While your own `kimi` is open on this side, coai’s Kimi turns wait for it to close and then answer busy: two Kimi processes '
  + 'could refresh one sign-in at the same time and sign you out.';

/** What runs where, for a WSL2 side (the plan's DL1) — the release is the one `ON_LINUX_SINCE` names. */
export const KIMI_RUNS_ON_WSL = `Kimi runs on this side, WSL2, from coai-mcp ${KIMI_ON_LINUX_SINCE}.`;

/**
 * What a native Linux side is told about Kimi: it RUNS from `KIMI_ON_NATIVE_LINUX_SINCE`, although it is NOT MEASURED there — the owner's decision of
 * 2026-10-23 (DN1 of todo/PLAN_kimi_and_vibe_on_native_linux_E2.md: "unmeasured" is a note, never a refusal). Vibe's sentence for the same host is
 * `VIBE_NATIVE_LINUX`; Vibe is measured there, so it says what closes its keyring, and this one says what is NOT known.
 */
export const KIMI_NATIVE_LINUX =
  `On a native Linux host coai-mcp runs Kimi from coai-mcp ${KIMI_ON_NATIVE_LINUX_SINCE}, although Kimi is not measured there: `
  + 'it is admitted by the owner’s decision of 2026-10-23, and the row carries a note that says so.';

/**
 * A Linux host whose kernel banner could not be read (`linux-unread`, `hostSide.ts`): the server cannot tell it from WSL1 and refuses a Kimi row on it
 * — an AMBIGUITY refusal, not a measurement one, so it stays. Vibe's twin is `VIBE_NOTE_LINUX_UNREAD`.
 */
export const KIMI_LINUX_UNREAD =
  'coai-mcp could not read this host’s kernel banner, so it cannot tell a native Linux host from WSL1 and refuses a Kimi row on this side; '
  + 'the extension-host log names the cause.';

/** What a WSL1 side is told — Vibe's sentence for the same host (`VIBE_NOTE_WSL1`): WSL2 only (the plan's F0 / S-K20), and what helps is moving the distro. */
export const KIMI_ON_WSL1 = 'This is WSL1, and coai-mcp runs Kimi on WSL2 only, so a Kimi row is refused on this side. Move this distro to WSL2 with `wsl --set-version <distro> 2`, run in Windows.';
