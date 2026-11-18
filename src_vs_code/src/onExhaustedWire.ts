import { FEATURES } from './binaryFeatures';
import type { OnExhausted } from './onExhaustedPanel';

/**
 * The parent § 4.6 table: what the settings file says about When the rounds run out, for the binary it meets.
 *
 * <p>Auto is what a server with no policy named runs, so it is written as nothing — except for a binary that has SAID it
 * does not list Auto (an older one, or one older than `--features`), which would not know the word and is told Good
 * enough instead. A binary that has not said anything yet is not an older one: every settled answer rewrites the file, so
 * nothing is written for it and the settled answer corrects the file a moment later. Any other policy crosses as it is.</p>
 */
export function onExhaustedEnv(
  value: OnExhausted,
  features: readonly string[],
  featuresSettled: boolean,
): Record<string, string> {
  if (value !== 'auto') {
    return { COAI_ON_EXHAUSTED: value };
  }

  return featuresSettled && !features.includes(FEATURES.onExhaustedAuto) ? { COAI_ON_EXHAUSTED: 'good_enough' } : {};
}
