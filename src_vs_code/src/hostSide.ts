import { runningUnderWsl } from './wslNetwork';

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
