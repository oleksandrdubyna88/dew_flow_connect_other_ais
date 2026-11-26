#!/bin/bash
# a model-free codex call with a scratch HOME and the scratch COPY of the sign-in; never the real home
S=${E3S4_SCRATCH:?}; PORT=${CX0_OVERRIDE_PORT:-9}
exec /usr/bin/env -i PATH="/usr/local/bin:/usr/bin:/bin:$(dirname $(readlink -f $HOME/.local/bin/codex))" HOME=$S/h0 CODEX_HOME=${CX0_HOME:-$S/copies/codex/home} CODEX_REFRESH_TOKEN_URL_OVERRIDE=http://127.0.0.1:$PORT/oauth/token LANG=C.UTF-8 TERM=dumb NO_COLOR=1 GIT_TERMINAL_PROMPT=0 codex "$@"
