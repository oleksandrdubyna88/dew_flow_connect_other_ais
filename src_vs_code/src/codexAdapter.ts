import { ChatAdapter, NOTHING, parsed, text, count, inside } from './chatAdapter';

/**
 * `codex` — no pipe at all: a process per turn, resuming a session the vendor stores itself.
 *
 * <p>The master plan recorded "codex exec has no multi-turn stdin" as a fact and built around it.
 * Measured, half of that was wrong: there is no multi-turn stdin, and there IS a multi-turn
 * conversation — through `resume`. Planted 4271 in one process, asked for it back in the next, and
 * got 4271.</p>
 *
 * <p><b>Four things were measured here, and three of them changed this file.</b></p>
 *
 * <ol>
 *   <li><b>The prompt goes on STDIN, not in argv.</b> `codex exec -` reads its instructions from
 *       stdin — its own help says so — and a 76 059-byte prompt was answered in five seconds
 *       outside a git repository. argv would not have carried it: Windows caps a command line at
 *       32 767 characters and a carried conversation is bounded at 60 000. The stream must then be
 *       CLOSED, or the process waits for more input and the turn never starts.</li>
 *   <li><b>`--json` gives events instead of prose.</b> `thread.started` carries the id,
 *       `item.completed` carries the answer as an `agent_message`, `turn.completed` ends it. Taking
 *       "the last line of stdout" would have worked in the measurement and broken the first time a
 *       usage block was printed after the answer — which it is.</li>
 *   <li><b>Resume by ID, never by `--last`.</b> `--last` is the most recent session ON THE MACHINE,
 *       so two chat tabs — or one chat and one review round — would answer each other's questions.
 *       Measured: `codex exec resume &lt;id&gt;` returned the number planted in that thread.</li>
 *   <li><b>`--skip-git-repo-check` does NOT disable the session store</b>, whatever its own help
 *       says about not persisting session files. Both halves were run in a directory that is not a
 *       repository at all, and the resume found its thread. So this feature's empty-temp-directory
 *       rule needs no exception for codex.</li>
 * </ol>
 */

/**
 * What a thread id may look like before it is put in a command line.
 *
 * <p>Not paranoia about a vendor: it is untrusted input by POSITION. The id arrives on another
 * process's stdout and leaves in an argv that, on Windows, goes through `cmd.exe` — `codex` is
 * `codex.cmd` there and node will not spawn a `.cmd` without a shell. `&`, `|` and a quote are the
 * whole attack. A UUID is what the vendor sends; anything else is not a thread this adapter will
 * resume. (codex and gemini, the code round, independently.)</p>
 */
const THREAD_ID = /^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$/;

/** The flags every codex turn carries. `exec` and any resume come first — see `argv`. */
export const CODEX_ARGS: readonly string[] = ['--json', '-', '--skip-git-repo-check'];

export const codexAdapter: ChatAdapter = {
  shape: 'per-turn',
  announces: false,
  // The `turn.completed` block counts UP across the thread — see the branch that reads it below.
  cumulative: true,
  // Checked HERE too, not only where the id was read. `classify` is the only place one is minted
  // today, so this line is safe by construction — and "safe by construction" is a proof a reader
  // has to reconstruct. A command line that refuses on its own terms cannot be handed anything a
  // future caller invented. (local, the second code round.)
  // `-m`, read from `codex exec --help` (`-m, --model <MODEL>`) and already the flag the reviewer
  // runtime passes on this same CLI. It goes BEFORE `CODEX_ARGS`, because the bare `-` in there is
  // the positional that makes codex read its instructions from stdin: an option after a positional
  // is still parsed as an option here, but writing it that way asks a reader to know that, and the
  // one place this product cannot afford a reader's benefit of the doubt is a command line.
  argv: ({ resume, model }) => [
    'exec',
    ...(THREAD_ID.test(resume) ? ['resume', resume] : []),
    ...(model.length > 0 ? ['-m', model] : []),
    ...CODEX_ARGS,
  ],
  // The prompt itself: there is no envelope, and the session writes it followed by EOF.
  encode: (turn) => turn,
  classify: (line) => {
    const event = parsed(line);
    if (event === undefined) {
      return NOTHING;
    }
    const kind = text(event['type']);
    if (kind === 'thread.started') {
      const id = text(event['thread_id']);

      return THREAD_ID.test(id) ? { kind: 'session', id } : NOTHING;
    }
    if (kind === 'item.completed') {
      const item = (event['item'] ?? {}) as Record<string, unknown>;

      return text(item['type']) === 'agent_message'
        ? { kind: 'answer', text: text(item['text']).trim() }
        : NOTHING;
    }
    // The answer is an `item.completed` and the numbers are on `turn.completed`, a separate line
    // arriving after it — which is why the seam has a `usage` event at all. The file's own header
    // comment has said since it was written that a usage block is printed after the answer and that
    // this branch did not exist; it does now.
    //
    // `input_tokens` and `output_tokens` are ATTESTED — `chatAdapters.test.ts` has carried a real
    // captured block since this adapter was written, which is where those two names come from.
    // `cached_input_tokens` is not: it is the plausible third field and has not been seen. Adding it
    // to the input count is safe either way, because `count` reads a field that is not there as
    // zero — the risk is under-counting a cached turn, never inventing tokens.
    //
    // The re-probe that would settle it is `npm run measure:stream -- codex`, which prints the usage
    // keys of whatever really arrives. It could not be run today: the account hit its usage limit
    // during the session that measured the other two vendors.
    if (kind === 'turn.completed') {
      const usage = inside(event, 'usage');

      return usage === undefined
        ? NOTHING
        : {
          kind: 'usage',
          usage: {
            // CUMULATIVE for the thread, not the cost of this turn — `chatUsage.turnTokens`
            // differences it. An adapter reports what its vendor said.
            tokensIn: count(usage['input_tokens']) + count(usage['cached_input_tokens']),
            tokensOut: count(usage['output_tokens']),
            costUsd: null,
          },
        };
    }
    if (kind === 'turn.failed' || kind === 'error') {
      const said = text(event['message']) || text(event['error']);

      return { kind: 'failure', failure: said.length > 0 ? said : 'the model did not finish the turn' };
    }

    return NOTHING;
  },
};
