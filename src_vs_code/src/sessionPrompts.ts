import { humanSaid } from './claudeQuestion';

/**
 * What the person wrote in a Claude session, and the sentences about it that the local road and the remote road both say
 * (E2): pure, so the file reader of `claudeSessions.ts` and the lines that came over ssh (`remoteRead.ts`) make a prompt by ONE
 * function. It lives in a module of its own because `claudeSessions.ts` is far over the 800-line limit and must not grow.
 */

/**
 * The most turns that cross to a webview at once, and the most of each.
 *
 * <p>A day-long session holds hundreds of turns and some of them are whole files pasted in. All of
 * it in one `postMessage` is megabytes over a bridge that has to stay responsive, rendered into a
 * region 40vh tall. The EARLIEST are kept, because the question this button exists to answer is
 * *what was this window for* — and a turn that is cut says so where it is cut. (gemini, the plan
 * round, on a payload nobody had bounded.)</p>
 */
export const MOST_PROMPTS = 200;
export const MOST_PER_PROMPT = 8_000;

/** Said when a session that can be read holds nothing the person wrote. */
export const NOTHING_OF_YOURS = 'Nothing of yours is in that session yet.';

/** What is said when a question was found that this build cannot read, `where` being the file or the session it was in. One sentence for the local road and the remote one. */
export const unrecognisedSaid = (where: string): string =>
  `Claude Code asked something this build does not recognise, in ${where}. The session format has changed — this needs a new release.`;

/**
 * What the person said in ONE line, as it is shown: empty when nobody did, and cut at {@link MOST_PER_PROMPT} with a note.
 *
 * @param say what a line says; `humanSaid`, unless a test counts the calls
 */
export function promptIn(line: string, say: (line: string) => string = humanSaid): string {
  const spoken = say(line);

  return spoken.length <= MOST_PER_PROMPT
    ? spoken
    : `${spoken.slice(0, MOST_PER_PROMPT)}\n\n… (cut here — the rest is in the session file)`;
}

/**
 * What the person wrote in lines already read (the tail of a remote session), EARLIEST first, at most {@link MOST_PROMPTS},
 * each cut as the file reader cuts it. It STOPS reading once it holds {@link MOST_PROMPTS}: a tail of a hundred thousand lines
 * is not parsed past the prompts that will be shown.
 *
 * @param say what a line says: `humanSaid`, or a function that counts the calls
 */
export function promptsIn(lines: readonly string[], say: (line: string) => string): readonly string[] {
  const said: string[] = [];
  for (const line of lines) {
    if (said.length >= MOST_PROMPTS) {
      break;
    }
    const prompt = promptIn(line, say);
    if (prompt.length > 0) {
      said.push(prompt);
    }
  }

  return said;
}
