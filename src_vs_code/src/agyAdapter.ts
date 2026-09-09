import { ChatAdapter, NOTHING, parsed, text } from './chatAdapter';

/**
 * `agy` — a persistent pipe, and the first protocol this feature learned.
 *
 * <p>The schema was MEASURED rather than read out of a manual: the binary's own error strings gave
 * it up after it refused `type:` with `stream input message is missing the "event" field`. Three
 * turns down one pipe were answered by one process with the context preserved — turn 3 resolved
 * "now simpler" against turn 2 without the passage being repeated.</p>
 *
 * <p>The four flags each answer a measured failure: stdin because `-p` does not read it (the model
 * answered "you did not attach the fragment"), NDJSON out because that is what makes a turn
 * distinguishable from a log line, `--mode plan` because explaining a paragraph should write
 * nothing, and `--disable-slash-commands` because the passage is another AI's text and can begin
 * with a slash.</p>
 */
export const AGY_ARGS: readonly string[] = [
  '--mode',
  'plan',
  '--disable-slash-commands',
  '--input-format',
  'stream-json',
  '--output-format',
  'stream-json',
];

export const agyAdapter: ChatAdapter = {
  shape: 'persistent',
  announces: true,
  argv: () => AGY_ARGS,
  encode: (turn) => JSON.stringify({ event: 'user', message: { role: 'user', content: turn } }),
  classify: (line) => {
    const event = parsed(line);
    if (event === undefined) {
      return NOTHING;
    }
    if (event['event'] === 'init') {
      return { kind: 'ready' };
    }
    if (event['event'] !== 'result') {
      return NOTHING;
    }

    const result = (event['result'] ?? {}) as Record<string, unknown>;
    const status = text(result['status']);
    if (status === 'SUCCESS') {
      return { kind: 'answer', text: text(result['response']).trim() };
    }

    // An ERROR result is an error, not an empty answer: the CLI reports a refused input this way —
    // it is how the schema was discovered — and a page showing nothing would look like a model with
    // nothing to say.
    const failure = text(result['error']);

    return { kind: 'failure', failure: failure.length > 0 ? failure : `the model answered with ${status || 'no status'}` };
  },
};
