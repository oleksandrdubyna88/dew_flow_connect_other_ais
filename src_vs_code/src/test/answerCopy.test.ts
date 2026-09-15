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
  await textCopier(ports_).copy(() => blockToCopy(answer, control.block, control.sig));
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
