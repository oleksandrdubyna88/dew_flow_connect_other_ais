import { familyOf, installedFileProbe, systemDriveOf, type ExecutableSide, type PathFamily } from './pathFamily';
import { isWsl, namesWsl2, readProcVersion, runningUnderWsl } from './wslNetwork';

/**
 * Which side this extension host is on, and what it can reach from there.
 *
 * <p><b>Why this file exists.</b> `process.platform` describes the EXTENSION HOST, never the machine
 * — in a VS Code window attached to WSL it is `linux` whatever the badge on the taskbar says. That
 * sentence was written twice in prose, in `panelProvider.ts` and in `vendorTerminal.ts`, and the
 * narrowing it implies was written three times in code: twice identically, and once — in
 * `selectionCapture.ts` — as `platform !== 'win32'`, which is the same fact asked wrongly and is the
 * defect this module was extracted for. A WSL host reports `linux` and still has a live Windows
 * session one interop hop away; a native Linux box reports `linux` and has nothing of the kind, and
 * `process.platform` cannot tell the two apart.</p>
 *
 * <p><b>What is deliberately NOT here.</b> Nothing in this file opens a socket, spawns a process or
 * looks for an executable. `classifyWindowsReach` is pure and `windowsReach` composes it with the one
 * cheap fact `wslNetwork.ts` already answers. Whether interop then actually WORKS — it can be
 * switched off in `/etc/wsl.conf`, and a docker-desktop distro has no `/mnt/c` at all — is answered
 * by attempting the thing, not by asking beforehand: a probe describes a moment that is not the
 * moment of use. That is the same discipline `wslNetwork.ts` records three reviewers imposing on it
 * when they removed its gateway probe, and it was raised again, and refused again, on this module's
 * own plan round.</p>
 *
 * <p>The identity question — WHICH side's settings and storage are mine — is a different one and is
 * already answered elsewhere, by `Side`/`sideKey` in `coaiInstall.ts` and `thisSide` in
 * `installer.ts`. Folding the two together would put "what OS am I" and "which bucket is mine" behind
 * one type because both are called "side" in English, which is a worse abstraction than the
 * scattering this file removes.</p>
 */

/**
 * The operating systems this extension answers for — what `process.platform` reports, narrowed.
 *
 * <p>It lived in `vendorTerminal.ts`, next to the install commands that were its first consumer, and
 * the code round asked why selection capture and orphan cleanup should depend on the vendor-install
 * module for a type neither of them has anything to do with. They should not: the union is a fact
 * about the HOST, so it belongs beside the two functions that answer host questions.</p>
 */
export type Platform = 'win32' | 'linux' | 'darwin';

/**
 * This extension host's own operating system.
 *
 * <p>Anything unrecognised narrows to `linux`, which is the SAFE default rather than an arbitrary
 * one: the Windows branch is the one that synthesises keystrokes and runs `taskkill`, and a platform
 * nobody planned for must not be handed either.</p>
 */
export function hostPlatform(raw: string = process.platform): Platform {
  // The nested ternary this arrived as — carried identically in both of the files it replaced — is
  // written out, because a reader has to hold two conditions at once to see that the DEFAULT is the
  // POSIX branch, and that is the load-bearing half. (SonarCloud, S3358.)
  if (raw === 'win32' || raw === 'darwin') {
    return raw;
  }

  return 'linux';
}

/**
 * Which family this host spells paths in — what decides whether a stored path is the OTHER side's
 * (`pathFamily.ts`). A WSL window is `posix` whatever machine it is on, exactly as `hostPlatform` says.
 */
export function hostFamily(raw: string = process.platform): PathFamily {
  return familyOf(raw);
}

/**
 * The side a CLI path is judged on, as THIS host is: its family (or the one named — a self-update shown for a platform,
 * a page drawn for a test), its system drive and the file probe activation installed (`useFileProbe`).
 */
export function hostExecutableSide(raw: string = process.platform): ExecutableSide {
  return { family: familyOf(raw), systemDrive: systemDriveOf(process.env['SystemDrive']), fileAt: installedFileProbe };
}

/** How this host can reach a live Windows session to run something only Windows can run. */
export type WindowsReachKind =
  /** This host IS Windows. */
  | 'direct'
  /** A WSL host: a Windows session exists on this machine, one interop hop away. */
  | 'interop'
  /** There is no Windows side here at all — darwin, or a Linux box that is not WSL. */
  | 'none';

export interface WindowsReach {
  readonly kind: WindowsReachKind;
}

/**
 * The reach implied by the platform and by WSL-ness alone. Pure — it probes nothing.
 *
 * <p>Three values rather than a boolean, because the PERSON sees the difference. "There is no
 * Windows side here", "the Windows side could not be reached" and "PowerShell itself would not run"
 * send somebody to three different places, and a boolean can carry two of them at most.</p>
 */
export function classifyWindowsReach(platform: Platform, isWslHost: boolean): WindowsReach {
  if (platform === 'win32') {
    return { kind: 'direct' };
  }

  return { kind: platform === 'linux' && isWslHost ? 'interop' : 'none' };
}

/**
 * {@link classifyWindowsReach}, asking the two cheap facts itself. Both injected, for the tests.
 *
 * <p>The WSL question is asked only where it can CHANGE the answer, which is `linux` and nowhere
 * else. A Windows host is `direct` whatever `/proc/version` would have said, and a mac has no
 * Windows side either way — both used to pay a file read that could not alter the outcome, on the
 * keypress path. (codex, the code round.)</p>
 *
 * <p>And it never rejects. `runningUnderWsl` already answers `false` for every failure, but this
 * function takes an injected predicate and is awaited by a command handler with no catch of its own —
 * an unhandled rejection there would take down the keybinding with nothing on screen saying why.
 * Not knowing whether this is WSL is `none`, which is the safe answer: it refuses in words and names
 * the menu. (gemini, the code round.)</p>
 */
export async function windowsReach(
  platform: string = process.platform,
  underWsl: () => Promise<boolean> = runningUnderWsl,
): Promise<WindowsReach> {
  const here = hostPlatform(platform);

  return here === 'linux'
    ? classifyWindowsReach(here, await answered(underWsl))
    : classifyWindowsReach(here, false);
}

/** The WSL question, with every way of not answering it collapsed into `false`. */
async function answered(underWsl: () => Promise<boolean>): Promise<boolean> {
  try {
    return await underWsl();
  } catch {
    return false;
  }
}

/**
 * Which host this extension runs on, as far as a Vibe row cares (DL1 of todo/PLAN_kimi_and_vibe_on_linux.md): WSL2 runs it,
 * a native Linux host and WSL1 are refused, macOS is not measured, Windows is as it always was. One union, one source —
 * `process.platform` alone cannot tell WSL from a desktop Linux, and a POSIX path family cannot tell either from a Mac.
 */
export type HostKind = 'windows' | 'wsl2' | 'wsl1' | 'linux' | 'darwin';

/**
 * The host kind from what is known without asking the disk: Windows and macOS are what they say, and a Linux host is taken
 * to be a native one — the safe answer, since it only ever shows a refusal. {@link hostKindOf} narrows it by the banner.
 */
export function hostKindOfPlatform(platform: Platform): HostKind {
  if (platform === 'win32') {
    return 'windows';
  }

  return platform === 'darwin' ? 'darwin' : 'linux';
}

/**
 * The host kind from the platform and this kernel's banner — pure. Only a Linux host reads the banner: WSL2 is the banner
 * naming `WSL2` or `microsoft-standard`, WSL1 any other that still names Microsoft or WSL, and the rest is native Linux.
 */
export function hostKindOf(platform: Platform, procVersion: string): HostKind {
  const kind = hostKindOfPlatform(platform);
  if (kind !== 'linux' || !isWsl(procVersion)) {
    return kind;
  }

  return namesWsl2(procVersion) ? 'wsl2' : 'wsl1';
}

/**
 * {@link hostKindOf} for THIS host, reading the banner itself — injected for the tests, and it never rejects: a banner that
 * cannot be read is an empty one, which is a native Linux host (a refusal, never a promise) — and it is SAID, once, through
 * `warn` (default: the extension's `console.warn`, which lands in the extension-host log).
 */
export async function hostKind(
  platform: string = process.platform,
  readBanner: () => Promise<string> = readProcVersion,
  warn?: (line: string) => void,
): Promise<HostKind> {
  const here = hostPlatform(platform);

  return here === 'linux' ? hostKindOf(here, await bannerOf(readBanner, warn)) : hostKindOfPlatform(here);
}

/** The banner, with every way of not reading it collapsed into the empty one — and one line saying it happened. */
async function bannerOf(readBanner: () => Promise<string>, warn?: (line: string) => void): Promise<string> {
  try {
    return await readBanner();
  } catch (reason) {
    (warn ?? console.warn)(
      `ConnectOtherAIs: /proc/version could not be read (${reason instanceof Error ? reason.message : String(reason)}); `
      + 'this host is treated as a native Linux host, so a Vibe row is shown as refused here.',
    );

    return '';
  }
}
