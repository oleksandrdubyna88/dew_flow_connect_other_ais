/**
 * The frame finder both remote parsers share (E1.S2, spec § A2.6): the session parser (`remoteFrame.ts`) and the accounts parser
 * (`remoteAccountsReply.ts`) each ask it for the lines of a reply and say their own words for the two misses.
 *
 * <p>Pure: a string in, a value out. It throws nothing and names no fault of either parser — a result that is a value is what lets
 * the accounts parser use it without importing the session parser, which pulls in the chat modules. Its name starts `remote`, so
 * `remoteLogScan.test.ts` covers it from its first commit.</p>
 */

/** The three lines that make a frame: the exact begin line, what every begin line of this family starts with, the exact end line. */
export interface Sentinels { readonly begin: string; readonly family: string; readonly end: string }

/** What a stdout holds of a frame. It names no parser's fault: each parser says its own words for the two misses. */
export type Framed =
  | { readonly kind: 'framed'; readonly lines: readonly string[] }
  | { readonly kind: 'no-begin'; readonly otherVersion: boolean }   // otherVersion: a line starts with `family`
  | { readonly kind: 'no-end' };

/**
 * The lines between the LAST begin line and the first end line after it. The LAST, because a banner or a transcript line
 * before the real reply may itself hold the begin line; the FIRST end after it, because nothing of the reply can end it early.
 */
export function framed(stdout: string, sentinels: Sentinels): Framed {
  const lines = stdout.split('\n');
  const begin = lines.lastIndexOf(sentinels.begin);
  if (begin < 0) {
    return { kind: 'no-begin', otherVersion: lines.some((line) => line.startsWith(sentinels.family)) };
  }
  const end = lines.indexOf(sentinels.end, begin + 1);

  return end < 0 ? { kind: 'no-end' } : { kind: 'framed', lines: lines.slice(begin + 1, end) };
}
