import { writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { makeDataDirectory, refusalError } from './dataDirMaker';
import { refuseUnderMissingRootOnceSettled } from './findingsBoard';

/**
 * The empty override file a security prompt is edited in, made if it is not there — `vscode`-free, so the storage-write
 * policy is proven on it by a run rather than by reading it (research/PLAN_paths_per_side.md E5.7, the gate's item B; it was
 * the body of `securityPromptEditor.ts`, which opens the editor and so cannot run outside one).
 *
 * <p>Never the missing NAMED data root (E5.0): the guard is asked first, and the folder is made through the one maker,
 * which never makes a named root — an override cannot be edited into a folder that is not there.</p>
 */
export async function ensureOverride(path: string): Promise<void> {
  await refuseUnderMissingRootOnceSettled(dirname(path));
  const made = await makeDataDirectory(dirname(path));
  if (!made.made) {
    throw refusalError(made);
  }
  try {
    await writeFile(path, '', { flag: 'wx' });
  } catch (error: unknown) {
    if (!alreadyExists(error)) {
      throw error;
    }
  }
}

function alreadyExists(error: unknown): boolean {
  return error instanceof Error && 'code' in error && error.code === 'EEXIST';
}
