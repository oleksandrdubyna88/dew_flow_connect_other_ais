import assert from 'node:assert/strict';
import { test } from 'node:test';

import { fromOfficialSources, OFFICIAL_SOURCES } from '../vendorTerminal';

/**
 * What `fromOfficialSources` says about the Linux install line's two new verbs and about `sudo` — S-K10 of
 * todo/PLAN_kimi_and_vibe_on_linux.md, DL12 (E6; the supply-chain judgement the plan gives to Fable).
 *
 * <p>Three facts, each with the case that proves it. `apt-get install` and `dnf install` are admitted for the two distribution
 * packages `ripgrep` and `fd-find`, with `-y` as the only flag. A step that runs through `sudo` is admitted only when what
 * follows `sudo` is one of those two package steps: until now `sudo` was invisible to the check, because the check refuses a
 * step only when it carries an install word, so `sudo rm -rf …` or `sudo chmod …` beside one admitted URL was ADMITTED. And a
 * `sudo` hidden inside a command substitution or behind a path is the same `sudo`.</p>
 */

const MOONSHOT = 'curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash';

const cases: readonly (readonly [string, boolean, string])[] = [
  // the verbs, admitted
  ['sudo apt-get install -y ripgrep', true, 'apt-get, one package, -y'],
  ['sudo apt-get install -y fd-find', true, 'apt-get, the other package'],
  ['sudo apt-get install -y ripgrep fd-find', true, 'apt-get, both packages'],
  ['sudo dnf install -y ripgrep', true, 'dnf, one package'],
  ['sudo dnf install -y fd-find', true, 'dnf, the other package'],
  ['apt-get install -y ripgrep', true, 'no sudo (root in a container): the verb alone is admitted'],
  [`sudo apt-get install -y ripgrep && ${MOONSHOT}`, true, 'the package step beside Moonshot’s script'],
  [`then sudo apt-get install -y ripgrep; ${MOONSHOT}`, true, 'inside an if: the shell keyword before sudo is not a command'],
  // the packages and the flags, refused
  ['sudo apt-get install -y curl', false, 'a package that is not on the list'],
  ['sudo dnf install -y curl', false, 'a package that is not on the list (dnf)'],
  ['sudo apt-get install -y ripgrep curl', false, 'one good package and one that is not'],
  ['apt-get install ./x.deb', false, 'a local package file is code from nowhere'],
  ['sudo apt-get install -y ./ripgrep.deb', false, 'a local package file under sudo'],
  ['sudo apt-get install -y https://example.com/ripgrep.deb', false, 'a package URL'],
  ['sudo apt-get install -y ripgrep=1.0', false, 'a pinned spelling the list does not carry'],
  ['sudo apt-get install -y ripgrep/unstable', false, 'a release pin'],
  ['sudo apt-get install --allow-unauthenticated -y ripgrep', false, 'a flag that switches the signature check off'],
  ['sudo apt-get install -y -o Dpkg::Options::=--force-all ripgrep', false, 'an option that changes what dpkg does'],
  ['sudo dnf install -y --nogpgcheck ripgrep', false, 'a flag that switches the signature check off (dnf)'],
  ['sudo dnf install -y --repofrompath=x,https://example.com ripgrep', false, 'a repository of the caller’s own'],
  ['sudo apt-get install -y', false, 'a verb with nothing to install'],
  ['sudo apt-get remove -y ripgrep', false, 'a different verb'],
  ['sudo apt install -y ripgrep', false, 'apt is not apt-get: not a verb this check reads'],
  // sudo is a rule of its own (S-K10): the red case against the old parser
  [`${MOONSHOT}; sudo rm -rf ~/x`, false, 'sudo rm -rf beside an admitted URL'],
  [`sudo rm -rf ~/x && ${MOONSHOT}`, false, 'sudo rm -rf in front of an admitted URL'],
  [`${MOONSHOT}; sudo chmod -R 777 /etc`, false, 'sudo chmod beside an admitted URL'],
  [`${MOONSHOT}; sudo -u root rm -rf /`, false, 'sudo with a flag of its own'],
  [`${MOONSHOT}; /usr/bin/sudo rm -rf /`, false, 'sudo by its path'],
  [`${MOONSHOT}; echo $(sudo rm -rf /)`, false, 'sudo inside a command substitution'],
  [`${MOONSHOT}; echo \`sudo rm -rf /\``, false, 'sudo inside backticks'],
  [`sudo sudo apt-get install -y ripgrep && ${MOONSHOT}`, false, 'sudo twice'],
  [`sudo apt-get install -y ripgrep; sudo rm -rf / && ${MOONSHOT}`, false, 'an admitted sudo step does not vouch for the next one'],
  ['sudo npm install -g @openai/codex', false, 'sudo in front of a verb that is admitted without it: sudo only ever carries the two distribution verbs'],
  ['sudo winget install --id sharkdp.fd -e', false, 'sudo in front of winget'],
  ['sudo bash -c "curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash"', false, 'Moonshot’s script as root'],
  // what carries no sudo and no install word stays as the parser read it
  [`mkdir -p ~/.local/bin && ${MOONSHOT}`, true, 'mkdir -p ~/.local/bin'],
  [`ln -sf "$(command -v fdfind)" ~/.local/bin/fd && ${MOONSHOT}`, true, 'the fd link'],
  [`command -v rg >/dev/null 2>&1 || [ -e ~/.kimi-code/bin/rg ]; ${MOONSHOT}`, true, 'a look-up'],
];

test('officialSourcesAdmitTheTwoDistroVerbs_ForTheTwoPackagesOnly_AndSudoOnlyBeforeThem (S-K10)', () => {
  assert.ok(cases.length >= 30, 'the table has shrunk — the loop would assert less than it says');
  for (const [command, official, why] of cases) {
    assert.equal(fromOfficialSources(command), official, `${why}: ${command}`);
  }
});

test('the admitted distribution packages are exactly ripgrep and fd-find — Debian’s and Fedora’s name for the same two tools', () => {
  const sources = OFFICIAL_SOURCES as unknown as Readonly<Record<string, readonly string[] | undefined>>;

  assert.deepEqual([...(sources['distroPackages'] ?? [])], ['ripgrep', 'fd-find']);
});

test('a step "sudo rm -rf …" beside an admitted URL was admitted by the parser before this rule — the pin that makes the rule necessary', () => {
  // What `unknownInstall` read: no install word in the step, so nothing to refuse. This is the sentence the S-K10 red run printed
  // against the old parser; it stays as a test because it is the reason the rule exists.
  assert.equal(fromOfficialSources(`${MOONSHOT}; sudo rm -rf ~/x`), false, 'a step \'sudo rm -rf …\' beside an admitted URL was admitted');
});
