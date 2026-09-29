/**
 * The text of a rendered fragment, with its tags dropped.
 *
 * <p>A WALK rather than a `replace` that strips angle brackets. CodeQL reads that shape as an
 * incomplete HTML sanitizer and refuses the pull request over it — `js/incomplete-multi-character-
 * sanitization`, high severity — and it is not wrong about the shape, only about the purpose: this
 * is a test shim reading a page it rendered itself, defending nothing. Writing it as a loop says
 * what it is and leaves the rule looking for sanitizers that are really sanitizers. It raised the
 * same alert twice, on two tests, which is why the walk lives here once.</p>
 */
export function textOf(fragment: string): string {
  let text = '';
  let insideATag = false;
  for (const character of fragment) {
    if (character === '<') {
      insideATag = true;
    } else if (character === '>') {
      insideATag = false;
    } else if (!insideATag) {
      text += character;
    }
  }

  return text.trim();
}
