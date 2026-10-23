// lib/code-text.mjs - a script's source with its comments dropped, for the source-text guards of the probe tests.
//
// The probe scripts run on import (and two of them can start a paid CLI), so no test imports them; a guard reads their TEXT. Matching raw text lets a comment that quotes the
// guarded line keep the guard green after the line is deleted, so a guard matches this instead. Line-oriented on purpose (no tokenizer): a line that starts with `//`, `/*`
// or `*` is a comment line and goes; a trailing ` // …` goes; a URL inside a string (`https://…`) stays because its `//` is not preceded by whitespace.
export const codeOf = (text) => text.replaceAll('\r\n', '\n').split('\n')
  .filter((line) => !/^\s*(?:\/\/|\/\*|\*)/u.test(line))
  .join('\n')
  .replaceAll(/(^|\s)\/\/.*$/gmu, '$1');
