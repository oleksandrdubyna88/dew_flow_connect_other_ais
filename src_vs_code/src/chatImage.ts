/**
 * A picture in the question.
 *
 * <p>Phase 0 of this plan MEASURED the mechanism rather than assuming one: `claude` and `agy` both
 * read a number out of a real PNG when the file's PATH was named in the prompt, and that is the one
 * vector both answered to. It means the vendor process opens the file itself — so the file's name,
 * its location and its lifetime are part of the contract with the model, not an implementation
 * detail somebody may change later.</p>
 *
 * <p>Pure, so every rule below is a test rather than a claim, and because the two that matter most
 * are refusals: what is not an image, and which providers cannot be handed one.</p>
 */

/** The four a screenshot actually is. */
export const IMAGE_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp', 'image/gif'];

/**
 * SVG is deliberately absent.
 *
 * <p>It is an image the way an HTML file is an image: markup, with script in it. The one thing this
 * feature does is hand a file to a process that will open it, and handing it a document that can
 * carry code is the one shape of this feature that would be a mistake.</p>
 */
const EXTENSIONS: Record<string, string> = {
  'image/png': 'png',
  'image/jpeg': 'jpg',
  'image/webp': 'webp',
  'image/gif': 'gif',
};

/** A screenshot is measured in hundreds of kilobytes; twenty megabytes of base64 is not one. */
const MAX_BASE64 = 20_000_000;

export interface PastedImage {
  readonly type: string;
  readonly base64: string;
}

/**
 * What a paste actually was.
 *
 * <p>A `data:` URL and nothing else, of a type on the list, with a body that is really base64. The
 * page reads a clipboard — which anything on the machine can write into — so this is a boundary and
 * behaves like one: it answers nothing for whatever it does not recognise, rather than passing a
 * string along for somebody further in to make sense of.</p>
 */
export function pastedImage(dataUrl: string): PastedImage | undefined {
  const match = /^data:([a-z]+\/[a-z+-]+);base64,([A-Za-z0-9+/=]+)$/.exec(dataUrl);
  if (match === null) {
    return undefined;
  }
  const [, type = '', base64 = ''] = match;
  if (!IMAGE_TYPES.includes(type) || base64.length === 0 || base64.length > MAX_BASE64) {
    return undefined;
  }

  return { type, base64 };
}

/**
 * What the file is called.
 *
 * <p>Made HERE, from what the image is, and never from anything the page said: a name that came
 * from the page would be a path fragment chosen by whatever wrote into the clipboard. The number is
 * the turn it belongs to, which makes a directory of them readable when something goes wrong.</p>
 */
export function imageFileName(type: string, turn: number): string {
  return `coai-${turn}.${EXTENSIONS[type] ?? 'png'}`;
}

/**
 * Why this provider cannot be handed a picture, or empty when it can.
 *
 * <p>Named, because the worst outcome of this whole feature is a picture that silently does not
 * arrive: somebody pastes a screenshot, asks about it, and is answered about the text alone with
 * nothing anywhere saying the image was dropped. The picker already follows this rule for models
 * that cannot answer at all.</p>
 *
 * <p>`codex` is refused as UNTESTED rather than as incapable — its account hit a usage limit during
 * phase 0, and that is a different sentence from a measured no.</p>
 */
export function imageRefusal(providerId: string): string {
  if (providerId.includes('-')) {
    return `A Team server cannot take a picture yet — ${providerId} answers over a wire that has no`
      + ' place for one. Send it to a model that runs on this machine.';
  }
  if (providerId === 'codex') {
    return 'Whether codex can read a picture has not been measured — its account hit a usage limit'
      + ' during the measurement, which is not the same as a no. Until it is re-run, send pictures'
      + ' to claude or antigravity.';
  }

  return '';
}

/**
 * The turn a picture travels in.
 *
 * <p>The PATH goes last, for the same reason the captured passage does in `chatPrompt.ts`: whatever
 * must survive a long turn goes at the end, and here that is the instruction to look at the file.
 * A question with no words is still a question — pasting a screenshot and pressing Enter asks
 * something perfectly clear — so the turn says what to do with it either way.</p>
 */
export function imageTurn(question: string, absolutePath: string): string {
  const asked = question.trim();
  const said = asked.length > 0 ? asked : 'What is in this image?';

  return `${said}\n\nThe image is a file on this machine. Read it at:\n${absolutePath}`;
}
