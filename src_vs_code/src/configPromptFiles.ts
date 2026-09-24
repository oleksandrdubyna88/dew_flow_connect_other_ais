import type { Dirent } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import { isPromptName } from './configTransfer';

/**
 * The prompt files Export config reads and Import config touches — issue #467, the code round.
 *
 * <p>Split out of the host so what a real folder can hold is tested against a real folder: a directory
 * named `x.md`, a symbolic link, a file that is there but cannot be read. Three reviewers found the host
 * treating each of those as either "no prompts" or a crash.</p>
 */

/** The prompts in a folder: REGULAR `.md` files whose name passes the id guard. No folder is none. */
export async function promptNames(dir: string): Promise<string[]> {
  // `isFile()` on a Dirent is false for a symbolic link: a link could point anywhere, and an export is a
  // file meant to be shared. (codex, the code round.)
  const entries = await readdir(dir, { withFileTypes: true }).catch((error: unknown) => noneWhenAbsent<Dirent>(error));

  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
    .map((entry) => entry.name.slice(0, -'.md'.length))
    .filter(isPromptName)
    .sort();
}

/**
 * Every prompt's text, by id — read ONE AT A TIME, so a large folder cannot run out of file handles, and
 * a file that fails says which one it was.
 */
export async function promptTexts(dir: string): Promise<Record<string, string>> {
  const texts: Record<string, string> = {};
  for (const id of await promptNames(dir)) {
    texts[id] = await readFile(`${dir}/${id}.md`, 'utf8').catch((error: unknown) => {
      throw new Error(`${id}.md could not be read: ${messageOf(error)}`);
    });
  }

  return texts;
}

/**
 * A prompt's text, or undefined when there is NO FILE — and only then.
 *
 * <p>Undefined is what a rollback DELETES. A file that is there but failed to read, taken as absent,
 * would be removed by the restore that was meant to protect it; so any other failure is thrown, which
 * stops the import before its first write. (our own reviewer, the code round.)</p>
 */
export async function readPromptOrAbsent(file: string): Promise<string | undefined> {
  return readFile(file, 'utf8').catch((error: unknown) => {
    if (codeOf(error) === 'ENOENT') {
      return undefined;
    }
    throw error;
  });
}

function noneWhenAbsent<T>(error: unknown): T[] {
  if (codeOf(error) === 'ENOENT') {
    return [];
  }
  throw error;
}

function codeOf(error: unknown): string {
  const code = typeof error === 'object' && error !== null ? (error as { code?: unknown }).code : undefined;

  return typeof code === 'string' ? code : '';
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
