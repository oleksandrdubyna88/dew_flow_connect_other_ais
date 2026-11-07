'use strict';
// Every test file of this repository imports THIS module as its first import (E5 S4, K5, plan P1): a test file started outside its runner's run folder
// ends here with one line and exit 78, before its other imports run and before any test creates anything. See `runFolderGuard.cjs` for the checks.
// It must stay the FIRST import of each test file, and nothing but a test file imports it.
require('./runFolderGuard.cjs').enforce();
