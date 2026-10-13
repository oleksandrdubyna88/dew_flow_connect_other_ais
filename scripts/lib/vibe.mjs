// lib/vibe.mjs — what every Vibe measurement script here launches with, and how it starts a child.
//
// Shared by scripts/probe-vibe-confinement.mjs (the confinement cells, research/RESULTS_vibe_confinement.md §3–§21) and
// scripts/vibe-home-manifest.mjs (the owned home's hash manifest, D17 of todo/PLAN_vibe_and_kimi_runtimes.md). It was
// private to the probe until the manifest step needed the same flag set, the same minimal environment and the same
// tree kill: two copies of the flag set would be two measurements of two different launches the day one of them moved.
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';

/**
 * ProcessEnvironment.MinimalWindows (src_mcp/runners/Processes/ProcessEnvironment.cs) — the question row's child
 * environment, and (D17) the base of every Vibe launch's BUILT environment. A copy of a C# list, so it is named here as
 * one: change both or neither.
 */
export const MINIMAL_WINDOWS = ['PATH', 'PATHEXT', 'SystemRoot', 'windir', 'ComSpec', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH',
  'APPDATA', 'LOCALAPPDATA', 'TEMP', 'TMP', 'USERNAME', 'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
  'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'PROCESSOR_LEVEL', 'PROCESSOR_REVISION', 'OS', 'NUMBER_OF_PROCESSORS'];

/** This process's values for MINIMAL_WINDOWS, matched case-insensitively as Windows does — absent names stay absent. */
export function minimalWindowsEnv() {
  const env = {};
  for (const name of MINIMAL_WINDOWS) {
    const key = Object.keys(process.env).find((k) => k.toLowerCase() === name.toLowerCase());
    if (key !== undefined) env[name] = process.env[key];
  }
  return env;
}

export const fwd = (p) => p.replaceAll('\\', '/');

/** A Windows system tool by its absolute path, never looked up through PATH (SonarCloud S4036, the code round 2026-10-12). */
export const SYSTEM32 = (exe) => join(process.env.SystemRoot ?? String.raw`C:\Windows`, 'System32', exe);

/**
 * Every child these scripts start goes through here: exe + argv (never a shell string, except mklink, which is a cmd
 * builtin), always a timeout, and a timeout kills the child's WHOLE tree by its PID before this returns — a stuck vibe
 * leaves python children behind otherwise (the code round, codex, 2026-10-12). Never by image name.
 */
export function run(file, args, options) {
  const r = spawnSync(file, args, { ...options, timeout: options.timeout ?? 60_000 });
  const timedOut = r.error?.code === 'ETIMEDOUT' || Boolean(r.signal);
  if (timedOut && r.pid) {
    if (process.platform === 'win32') {
      spawnSync(SYSTEM32('taskkill.exe'), ['/PID', String(r.pid), '/T', '/F'], { encoding: 'utf8', timeout: 15_000 });
    } else {
      try { process.kill(-r.pid, 'SIGKILL'); } catch { /* the group is already gone */ }
    }
  }
  return r;
}

/** Every file under `dir` (relative, forward slashes, sorted), not following links. */
export function files(dir) {
  if (!existsSync(dir)) return [];
  const out = [];
  const walk = (d) => {
    for (const e of readdirSync(d, { withFileTypes: true })) {
      const p = join(d, e.name);
      if (e.isDirectory()) walk(p);
      else out.push(fwd(relative(dir, p)));
    }
  };
  walk(dir);
  return out.sort((a, b) => a.localeCompare(b));
}

/**
 * The SHIPPED flag set (D2, D3, D7, D8 of the plan; src_mcp/runners/Reviewers/VibeRuntime.cs builds the same argv), with
 * a probe arm's deviations — `--trust` only for a positive control, `--resume` for a second turn.
 */
export function vibeArgs({ workdir, exportDir, trust = false, timeLimit = 120, resume = '' }) {
  return ['-p', '--output', 'json', '--agent', 'plan', '--enabled-tools', 'read_file', '--workdir', workdir,
    '--time-limit', String(timeLimit), '--output-dir', exportDir, ...(trust ? ['--trust'] : []), ...(resume ? ['--resume', resume] : [])];
}
