/**
 * The mark a section wears while it is waiting on something that takes real seconds.
 *
 * <p>Two surfaces need the same one — a reviewer card's caption and a consultant row — and they live
 * in different modules, so it is here rather than copied into both. A second copy of a spinner is a
 * spinner that will stop matching the other one the day either is touched.</p>
 *
 * <p><b>It is a mark beside a sentence, never a sentence replaced by a mark.</b> A bare spinner says
 * only that something is happening; the caption beside it says what, which is the difference between
 * a person waiting and a person wondering whether the panel has hung. The animation is decoration:
 * the sentence is the whole message, and a viewer with reduced motion loses nothing.</p>
 */

/** Hidden from assistive technology: the sentence beside it already says what is happening. */
export const LOOKING = '<span class="looking" aria-hidden="true"></span>';

/**
 * The rule for it. Appended to the page stylesheet.
 *
 * <p>No backticks and no hex in here: this string is interpolated into a template literal, and a
 * backtick in a comment inside one ends the literal — which has broken this build before.</p>
 */
export const LOOKING_CSS = `
  /* A ring that turns. Sized from the caption it sits in rather than in pixels, so it stays
     proportionate when the panel is zoomed. */
  .looking {
    display: inline-block;
    width: 0.85em;
    height: 0.85em;
    margin-right: 5px;
    vertical-align: -0.1em;
    border: 1.5px solid var(--vscode-panel-border);
    border-top-color: var(--vscode-textLink-foreground);
    border-radius: 50%;
    animation: looking-turn 0.9s linear infinite;
  }
  @keyframes looking-turn {
    to { transform: rotate(360deg); }
  }
  /* Decoration only, so a viewer who asked for less motion gets a ring that simply stands still.
     The PREFERENCE redefines the animation rather than the element: this page forbids a class from
     being defined twice, and the rule that forbids it is right - a second .looking rule anywhere
     in this stylesheet is one somebody will lose to in silence. */
  @media (prefers-reduced-motion: reduce) {
    @keyframes looking-turn {
      to { transform: none; }
    }
  }`;
