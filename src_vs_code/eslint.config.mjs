// The linter this package could not have until today.
//
// WHY IT COULD NOT. `typescript-eslint` declares peer `typescript: ">=4.8.4 <6.1.0"` — true of
// `latest` (8.70.0) and of the alpha canary alike, checked against the registry. This package was on
// `typescript ^7.0.2`, so the parser refused to install and the largest TypeScript package in the
// family had no linter at all. TypeScript 7 arrived as an ordinary `chore(deps)` bump (7c08d6a8),
// not to satisfy anything the code needs: measured before moving it back, 5.9.3 typechecks this
// source with ZERO errors and the whole suite passes, 3583 to 0.
//
// THE TYPE-AWARE RULE IS THE POINT. `no-floating-promises` cannot be had from a syntax-only config,
// and it is the rule the hardening plan names. A plain `eslint` would have passed a gate having
// dropped the one rule the gate was for, which is why this waited for the compiler to move instead
// of shipping a weaker check.
//
// THE RULE SET IS READ OFF THE SOURCE, not chosen. This code already carried `eslint-disable`
// comments naming `no-control-regex`, `no-new-func` and `no-await-in-loop`, written against a config
// that never existed. Only the first is enabled by a preset. `no-new-func` is switched on for
// production only, where it costs nothing and means something — all twenty-four `new Function` sites
// are in tests, which is how a page script is executed at all. `no-await-in-loop` is NOT switched
// on: it reports 73 times here, and five decorative markers are not evidence that a codebase wants a
// rule it violates seventy-three times; those five directives were removed instead.
// `reportUnusedDisableDirectives` makes any marker that has stopped applying an error rather than a
// silent exemption.
//
// WHAT IS DELIBERATELY NOT HERE, with the numbers. The sibling extensions carry a boundary set —
// `complexity: 4`, `max-lines-per-function: 50`, `no-console`. Measured on this source they report
// 451, 49 and 109 violations. In `dew_flow_rag_qln` the same set found 33 across 7 files, which is a
// boundary you can name and exempt; 451 is not a boundary, it is a wholesale rejection of how this
// package is written, and burying that in a CI change would be the wrong way to raise it. `max-lines`
// stays, because at 8 violations it IS nameable and the source already expects it.
import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    // `out/` is compiled output, the `.mjs` scripts are not in the TypeScript project, and
    // `src/generated/` is written by `prepare-gate.mjs` during the build.
    ignores: ['out/**', 'node_modules/**', 'scripts/**', 'src/generated/**'],
  },
  {
    // A disable that has stopped being needed is an exemption nobody granted: once a refactor makes
    // it unnecessary, the stale marker keeps exempting the code silently. Making that an error turns
    // "remove it when it no longer applies" from prose into a check.
    linterOptions: { reportUnusedDisableDirectives: 'error' },
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['src/**/*.ts'],
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      // `node:test`'s `test`/`it`/`describe` return a promise BY DESIGN and are not meant to be
      // awaited — without this exemption the rule reports 3,544 times in `src/test` and says nothing
      // about any of them. Naming the package rather than the identifier means a local function
      // called `test` is still checked.
      '@typescript-eslint/no-floating-promises': ['error', {
        allowForKnownSafeCalls: [
          {
            from: 'package',
            package: 'node:test',
            name: ['test', 'it', 'describe', 'suite', 'before', 'after', 'beforeEach', 'afterEach'],
          },
        ],
      }],
      // A leading underscore is this codebase's existing way of saying "bound and deliberately
      // unused" — `_`, `_done`, `_gone`. Encoding that here is reading the convention off the source
      // rather than asking four call sites to change to suit a default.
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
      }],

      // Free where it counts: the extension does not build code at runtime and never should, and
      // measured across `src`, every one of the twenty-four `new Function` sites is in a TEST, where
      // it is how a page script gets executed at all. So the guard is real in production and off in
      // the tests, rather than nine decorative `eslint-disable` comments for a rule nobody enabled.
      'no-new-func': 'error',

      'max-lines': ['error', { max: 800, skipBlankLines: false, skipComments: false }],
    },
  },
  {
    // Tests narrate: one file holds one scenario end to end, and splitting it to satisfy a line count
    // hides the story the test exists to tell. And a test page script is executed by `new Function`,
    // which is the only way to run one outside a webview.
    files: ['src/test/**/*.ts'],
    rules: { 'max-lines': 'off', 'no-new-func': 'off' },
  },
  {
    // The `.test.mjs` files are node scripts, not part of the TypeScript program: they get node's
    // globals and none of the type-aware rules, which have no program to consult for them.
    files: ['**/*.mjs'],
    languageOptions: { globals: globals.node },
  },
);
