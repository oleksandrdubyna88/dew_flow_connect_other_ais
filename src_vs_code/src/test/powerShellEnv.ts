import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * The ONE environment a test hands a PowerShell it starts (C28, S12 of todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md).
 *
 * <p>Two properties, both for a PowerShell that must not depend on who started the test:</p>
 * <ul>
 * <li><b>Minimal.</b> Only what a PowerShell needs to start and to resolve `Get-Command` by extension — never the caller's
 * profile, `PSModulePath` or tokens. A test that wants more says so through `extra`.</li>
 * <li><b>Its module-analysis cache is inside the run's temp.</b> PowerShell 5.1 and 7 both honour `PSModuleAnalysisCachePath`, and
 * write the cache (deferred about ten seconds, only when it is dirty) where it names. Without it the cache goes where the
 * process's profile resolves — and for a PowerShell whose known-folder lookup fails that is a path RELATIVE to its working
 * folder, which for a test run from `npm test` is `src_vs_code/Microsoft/Windows/PowerShell/ModuleAnalysisCache`, untracked.
 * Six worktrees held one on 2026-10-22. Every spawn also passes `cwd: tmpdir()`, so the two together keep it out of the checkout.</li>
 * </ul>
 *
 * <p>The spawn sites are held to this by `powerShellEnv.test.ts`'s scan.</p>
 */

/** What a PowerShell needs from the machine to start and to find programs, copied when the machine has it. */
const FROM_THE_MACHINE = ['PATH', 'SystemRoot', 'PATHEXT', 'LOCALAPPDATA', 'ProgramFiles', 'ProgramData', 'ALLUSERSPROFILE'] as const;

/** The module-analysis cache's file name inside the temp folder; the run removes that folder (D2), so the file goes with it. */
export const MODULE_CACHE_NAME = 'testsToDelete-powershell-module-analysis-cache';

/**
 * @param extra entries that win over the defaults — the Runs tests' own `PATH`, a scenario's variables
 * @param temp the run's temp folder: `TEMP`/`TMP` and the module cache live in it (default: this process's `tmpdir()`)
 */
export function powerShellEnv(extra: Readonly<Record<string, string>> = {}, temp: string = tmpdir()): Record<string, string> {
  const machine = FROM_THE_MACHINE.flatMap((name) => {
    const value = process.env[name];

    return value === undefined || value === '' ? [] : [[name, value] as const];
  });

  return {
    ...Object.fromEntries(machine),
    TEMP: temp,
    TMP: temp,
    PSModuleAnalysisCachePath: join(temp, MODULE_CACHE_NAME),
    ...extra,
  };
}
