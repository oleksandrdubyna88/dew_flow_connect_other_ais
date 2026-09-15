import assert from 'node:assert/strict';
import { test } from 'node:test';
import { answerToCopy, blockToCopy } from '../answerCopy';
import { chatCommandOf } from '../chatMessages';
import { chatMessagesHtml } from '../chatPage';
import { textCopier, type CopyPorts, type Said } from '../copyText';
import { signatureOf } from '../renderAnswer';

/**
 * THE SEAM: a control the page drew, pressed, arriving at a clipboard.
 *
 * <p>Everything between is exercised for real — the markup `chatMessagesHtml` produces, the message
 * the page's listener would post from it, the parser that decides whether to obey it, and the
 * decision that resolves the position against the host's own stored markdown. The only things stood
 * in for are the clipboard and the status bar, which is the whole reason the decision lives in a
 * module with no `vscode` in it.</p>
 *
 * <p><b>Why it is not enough to ask `answerBlocks` what block N is.</b> That proves the enumerator
 * and nothing else: the hook could fail to forward, the parser could refuse the message, the write
 * could never happen, and a test built that way would stay green while nothing ever reached the
 * clipboard. Two vendors raised that independently on the plan round, which is what moved this test
 * from the enumerator up to here.</p>
 */

const FENCE = '```';

/** A clipboard and a status bar that only remember what they were handed. */
function ports(): { ports: CopyPorts; wrote: string[]; said: string[] } {
  const wrote: string[] = [];
  const said: string[] = [];
  const nothing: Said = { dispose: () => undefined };

  return {
    wrote,
    said,
    ports: {
      writeText: (text) => {
        wrote.push(text);

        return Promise.resolve();
      },
      say: (message) => {
        said.push(message);

        return nothing;
      },
    },
  };
}

/** Every control the page drew, as the coordinates its listener would post. */
function controls(html: string): { index: number; block: number; sig: string }[] {
  return [...html.matchAll(/data-block="(\d+)" data-at="(\d+)" data-sig="([^"]+)"/g)].map((match) => ({
    block: Number(match[1]),
    index: Number(match[2]),
    sig: String(match[3]),
  }));
}

/** The whole way across: the coordinate the page would post, resolved and written. */
async function press(
  answer: string,
  control: { index: number; block: number; sig: string },
  ports_: CopyPorts,
): Promise<void> {
  // Through the PARSER, exactly as the host receives it — a message it refuses never reaches a
  // clipboard, and that refusal is part of what this asserts.
  const command = chatCommandOf({
    type: 'command', command: 'copyBlock',
    index: control.index, block: control.block, sig: control.sig,
  });
  assert.equal(command.kind, 'copyBlock', 'the parser refused a message the page itself produced');
  assert.ok(command.kind === 'copyBlock');

  // The PARSED fields, never the raw ones. Resolving with what the page sent would let a parser that
  // answered `block: 0` for every message pass this test while the shipped host copied block 0 for
  // every control. (codex, the code round — and it was doing exactly that.)
  assert.deepEqual(
    { index: command.index, block: command.block, sig: command.sig },
    control,
    'the parser changed the coordinate the page posted',
  );
  await textCopier(ports_).copy(() => blockToCopy(answer, command.block, command.sig));
}

test('the control the page rendered for a block copies that block and no other', async () => {
  const answer = [
    'Before.',
    '',
    `${FENCE}ts`,
    'const first = 1;',
    FENCE,
    '',
    '> a quoted line',
    '',
    '- an item:',
    '',
    `  ${FENCE}sh`,
    '  echo nested',
    `  ${FENCE}`,
    '',
    `${FENCE}reply`,
    'Send this onward.',
    FENCE,
  ].join('\n');

  const html = chatMessagesHtml([{ role: 'you', text: 'ask' }, { role: 'model', text: answer }]);
  const drawn = controls(html);

  // Every control belongs to message 1 — the person's message at 0 is not rendered by this path.
  assert.deepEqual(drawn.map((one) => one.index), [1, 1, 1, 1], 'a control names the wrong message');
  assert.deepEqual(drawn.map((one) => one.block), [0, 1, 2, 3], 'the controls are not numbered in order');

  // HAND-WRITTEN, never the same array indexed twice: expectations derived from the thing under test
  // pass under any consistent-but-wrong numbering, which is exactly the defect being guarded.
  const expected = ['const first = 1;', 'a quoted line', 'echo nested', 'Send this onward.'];

  for (let at = 0; at < drawn.length; at += 1) {
    const kit = ports();
    // eslint-disable-next-line no-await-in-loop
    await press(answer, drawn[at]!, kit.ports);
    assert.deepEqual(kit.wrote, [expected[at]], `the control for block ${at} copied the wrong text`);
  }
});

test('an answer the renderer stopped drawing into still resolves every control it DID draw', async () => {
  // The host resolves by ordinal, so the two sides have to agree about a block that was never drawn.
  // They cannot disagree — one walk assigns and records — and this is the seam-level proof of it on
  // the shape that makes a re-lex wrong: nesting past the depth the renderer draws to, where the rest
  // is emitted as escaped text and is not a block at all. (gemini, the plan round.)
  const answer = [FENCE, 'the first one', FENCE, '', `${'> '.repeat(11)}too deep to draw`].join('\n');
  const html = chatMessagesHtml([{ role: 'model', text: answer }]);
  const drawn = controls(html);

  // Every control the page drew resolves, and the LAST one resolves to something rather than to
  // nothing — the shape a numbering that counted undrawn blocks would break first.
  assert.ok(drawn.length > 1, 'the fixture drew too little to prove anything');
  for (const control of drawn) {
    const kit = ports();
    // eslint-disable-next-line no-await-in-loop
    await press(answer, control, kit.ports);
    assert.equal(kit.wrote.length, 1, `the control for block ${control.block} resolved to nothing`);
  }
  const first = ports();
  await press(answer, drawn[0]!, first.ports);
  assert.deepEqual(first.wrote, ['the first one'], 'the first control copied the wrong text');
});

test('the control on a reply block says a reply prompt was copied', async () => {
  const answer = [`${FENCE}reply`, 'Send this onward.', FENCE].join('\n');
  const html = chatMessagesHtml([{ role: 'model', text: answer }]);
  const kit = ports();

  await press(answer, controls(html)[0]!, kit.ports);

  assert.deepEqual(kit.wrote, ['Send this onward.'], 'the reply prompt did not reach the clipboard');
  assert.match(kit.said[0] ?? '', /reply prompt/, 'copying a reply prompt did not say so');
});

test('a control drawn for an answer that has since been rewritten refuses rather than copying', async () => {
  const drawnFrom = [FENCE, 'the text the button was drawn for', FENCE].join('\n');
  const html = chatMessagesHtml([{ role: 'model', text: drawnFrom }]);
  const control = controls(html)[0]!;

  // REWRITTEN in place to a different text with the SAME number of blocks, which is the case no
  // range check can see: the ordinal is still 0 and still resolves. Only the signature differs.
  const rewritten = [FENCE, 'something else entirely', FENCE].join('\n');
  const kit = ports();
  await textCopier(kit.ports).copy(() => blockToCopy(rewritten, control.block, control.sig));

  assert.deepEqual(kit.wrote, [], 'the stale control copied the new text');
  assert.match(kit.said[0] ?? '', /no longer part of this answer/, 'the refusal said nothing useful');
});

test('a control naming a block the answer no longer has refuses rather than copying something else', async () => {
  const answer = [FENCE, 'only one block', FENCE].join('\n');
  const kit = ports();

  await textCopier(kit.ports).copy(() => blockToCopy(answer, 4, signatureOf(answer)));

  assert.deepEqual(kit.wrote, [], 'an out-of-range position still copied something');
  assert.match(kit.said[0] ?? '', /no longer part of this answer/, 'the refusal said nothing useful');
});

test('a clipboard that refuses the write says so, for a block and for a whole answer', async () => {
  const answer = [FENCE, 'x', FENCE].join('\n');
  const said: string[] = [];
  const refusing: CopyPorts = {
    writeText: () => Promise.reject(new Error('the clipboard is held by another program')),
    say: (message) => {
      said.push(message);

      return { dispose: () => undefined };
    },
  };
  const copier = textCopier(refusing);

  const block = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));
  const whole = await copier.copy(() => answerToCopy(answer));

  assert.equal(block.copied, false, 'a refused write reported the block as copied');
  assert.equal(whole.copied, false, 'a refused write reported the answer as copied');
  // BOTH paths, because the whole-answer control wrote with a bare `void` and no catch before this
  // change: one site guarded and the other left is the defect this family keeps writing.
  assert.equal(said.length, 2, 'a refused copy was silent on one of the two paths');
  for (const one of said) {
    assert.match(one, /could not be copied/, 'a refused copy did not say so');
    assert.doesNotMatch(one, /^Copied/, 'a refused copy printed the success sentence');
  }
});

test('a change ELSEWHERE in the answer refuses a control whose own block is untouched', async () => {
  // The sharp form of the stale case: the block this control names is byte-identical, the block count
  // is unchanged, and only a paragraph above it moved. The signature covers the WHOLE stored markdown
  // precisely so that still refuses — a signature over the block alone would accept it. (codex.)
  const before = ['Before.', '', FENCE, 'unchanged', FENCE].join('\n');
  const html = chatMessagesHtml([{ role: 'model', text: before }]);
  const control = controls(html)[0]!;
  const after = ['Something else entirely.', '', FENCE, 'unchanged', FENCE].join('\n');

  const kit = ports();
  await textCopier(kit.ports).copy(() => blockToCopy(after, control.block, control.sig));

  assert.deepEqual(kit.wrote, [], 'a control was obeyed although the answer around its block had changed');
  assert.match(kit.said[0] ?? '', /no longer part of this answer/, 'the refusal said nothing useful');
});

test('a clipboard that never answers gives up rather than wedging every later press', async () => {
  // A rejection is already handled; this is the one with no natural end — a permission prompt nobody
  // answers, a host that forgets to resolve. Without a ceiling the chain behind it never runs and
  // every later press does nothing, silently and for ever. (codex, the plan round.)
  const answer = [FENCE, 'x', FENCE].join('\n');
  const said: string[] = [];
  const wrote: string[] = [];
  let pressed = 0;
  const wedged: CopyPorts = {
    writeText: (text) => {
      pressed += 1;

      return pressed === 1
        ? new Promise<void>(() => undefined)
        : Promise.resolve(wrote.push(text)).then(() => undefined);
    },
    say: (message) => {
      said.push(message);

      return { dispose: () => undefined };
    },
  };
  const copier = textCopier(wedged, 20);

  const first = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));
  const second = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));

  assert.equal(first.copied, false, 'a write that never answered was reported as copied');
  assert.match(said[0] ?? '', /could not be copied/, 'a wedged write said nothing to the person');
  assert.equal(second.copied, true, 'the press after a wedged one never reached the clipboard');
  assert.deepEqual(wrote, ['x'], 'the retry did not write');
});

test('a write we stopped waiting for cannot undo the one that came after it', async () => {
  // Giving up on a wait releases the queue; it does not cancel the write, because a clipboard write
  // cannot be cancelled. So the abandoned one can still land — AFTER the person has copied something
  // else — and quietly replace it. The copier puts the newer text back. (codex and gemini, the code
  // round, independently and from different roles.)
  const answer = [FENCE, 'first', FENCE, '', FENCE, 'second', FENCE].join('\n');
  const wrote: string[] = [];
  let release: (() => void) | undefined;
  let attempt = 0;
  const slow: CopyPorts = {
    writeText: (text) => {
      attempt += 1;
      if (attempt === 1) {
        // Hangs past the ceiling, then lands — the one shape a race alone does not cover.
        return new Promise<void>((resolve) => {
          release = () => {
            wrote.push(text);
            resolve();
          };
        });
      }
      wrote.push(text);

      return Promise.resolve();
    },
    say: () => ({ dispose: () => undefined }),
  };
  const copier = textCopier(slow, 20);

  const stalled = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));
  const after = await copier.copy(() => blockToCopy(answer, 1, signatureOf(answer)));
  assert.equal(stalled.copied, false, 'a write that outran the ceiling was reported as copied');
  assert.equal(after.copied, true, 'the press after a stalled one never landed');
  assert.deepEqual(wrote, ['second'], 'the stalled write was not the one that was abandoned');

  release?.();
  await new Promise((resolve) => { setTimeout(resolve, 10); });

  assert.deepEqual(wrote, ['second', 'first', 'second'],
    'a write that landed late was left holding the clipboard over the newer one');
});

test('a corrective write is tracked too, and cannot put back what is already stale', async () => {
  // The first fix, one level down. `reinstate` puts the newer text back after an abandoned write
  // lands — but that corrective write is itself a write, and a press can land while IT is in flight.
  // Then the correction settles last and restores text that is stale by two. (CodeRabbit, #273.)
  const answer = [FENCE, 'first', FENCE, '', FENCE, 'second', FENCE, '', FENCE, 'third', FENCE].join('\n');
  const sig = signatureOf(answer);
  const started: string[] = [];
  /** What the clipboard ended up holding, in the order it actually LANDED. */
  const landed: string[] = [];
  let releaseStalled: (() => void) | undefined;
  let releaseCorrection: (() => void) | undefined;

  const ports_: CopyPorts = {
    writeText: (text) => {
      started.push(text);
      const call = started.length;
      const land = (): void => { landed.push(text); };
      if (call === 1) {
        return new Promise<void>((resolve) => { releaseStalled = () => { land(); resolve(); }; });
      }
      if (call === 3) {
        // The CORRECTIVE write, held open so a newer press can land underneath it.
        return new Promise<void>((resolve) => { releaseCorrection = () => { land(); resolve(); }; });
      }
      land();

      return Promise.resolve();
    },
    say: () => ({ dispose: () => undefined }),
  };
  const copier = textCopier(ports_, 20);

  await copier.copy(() => blockToCopy(answer, 0, sig));   // stalls past the ceiling, abandoned
  await copier.copy(() => blockToCopy(answer, 1, sig));   // 'second' lands
  releaseStalled?.();                                      // the abandoned write lands -> correction starts
  await new Promise((resolve) => { setTimeout(resolve, 10); });
  await copier.copy(() => blockToCopy(answer, 2, sig));   // 'third' lands, while the correction is open
  releaseCorrection?.();
  await new Promise((resolve) => { setTimeout(resolve, 30); });

  assert.equal(landed.at(-1), 'third',
    'a corrective write landed last and put back text the person had already copied past');
});

test('a correction from one copier cannot land on top of what another copier just copied', async () => {
  // TWO copiers, ONE clipboard — which is the real arrangement: the chat has its own and the panel's
  // phrase list has another, and neither can see the other's presses. A correction belonging to the
  // chat could therefore put an answer back over a phrase the person had just copied. What makes it
  // answerable is that "the newest text anybody asked for" is one fact, not one per copier.
  // (codex, the plan round.)
  const answer = [FENCE, 'the answer', FENCE].join('\n');
  /** The one clipboard both copiers write to, in the order writes actually LAND. */
  const landed: string[] = [];
  let releaseStalled: (() => void) | undefined;
  let stalled = false;
  const clipboard = (text: string): Promise<void> => {
    if (!stalled) {
      stalled = true;

      return new Promise<void>((resolve) => {
        releaseStalled = () => { landed.push(text); resolve(); };
      });
    }
    landed.push(text);

    return Promise.resolve();
  };
  const ports_ = (): CopyPorts => ({ writeText: clipboard, say: () => ({ dispose: () => undefined }) });

  const chat = textCopier(ports_(), 20);
  const panel = textCopier(ports_(), 20);

  await chat.copy(() => blockToCopy(answer, 0, signatureOf(answer)));       // stalls, abandoned
  await panel.copy(() => ({ kind: 'copy', text: 'a phrase', done: 'ok', failed: 'no' }));
  releaseStalled?.();                                                       // the stalled write lands late
  await new Promise((resolve) => { setTimeout(resolve, 30); });

  assert.equal(landed.at(-1), 'a phrase',
    'a correction from the chat put its answer back over the phrase the person had just copied');
});

test('a press after a refused one still reaches the clipboard', async () => {
  // The queue is chained onto the previous press's FAILURE as well as its success; without that, a
  // retry after a rejection would never run and both copy paths would be dead. (codex.)
  const answer = [FENCE, 'x', FENCE].join('\n');
  const wrote: string[] = [];
  let attempts = 0;
  const flaky: CopyPorts = {
    writeText: (text) => {
      attempts += 1;

      return attempts === 1
        ? Promise.reject(new Error('held by another program'))
        : Promise.resolve(wrote.push(text)).then(() => undefined);
    },
    say: () => ({ dispose: () => undefined }),
  };
  const copier = textCopier(flaky);

  const refused = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));
  const retried = await copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));

  assert.equal(refused.copied, false, 'a rejected write was reported as copied');
  assert.equal(retried.copied, true, 'the retry after a rejection never reached the clipboard');
  assert.deepEqual(wrote, ['x'], 'the retry wrote the wrong thing, or nothing');
});

test('two presses leave the clipboard holding the second one', async () => {
  // Whichever write RESOLVES last is what the clipboard keeps, so a slow first press must not
  // overwrite a fast second one. The copier chains them; this is the assertion that it does.
  const answer = [FENCE, 'first', FENCE, '', FENCE, 'second', FENCE].join('\n');
  const wrote: string[] = [];
  const slow: CopyPorts = {
    writeText: (text) => new Promise((resolve) => {
      setTimeout(() => {
        wrote.push(text);
        resolve();
      }, text === 'first' ? 20 : 0);
    }),
    say: () => ({ dispose: () => undefined }),
  };
  const copier = textCopier(slow);

  const one = copier.copy(() => blockToCopy(answer, 0, signatureOf(answer)));
  const two = copier.copy(() => blockToCopy(answer, 1, signatureOf(answer)));
  await Promise.all([one, two]);

  assert.deepEqual(wrote, ['first', 'second'], 'the presses did not land in the order they were made');
});
