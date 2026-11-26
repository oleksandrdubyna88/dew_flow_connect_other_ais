#!/bin/bash
# usage: pxrun.sh <label> <agy args...>   agy in a unit with PrivateNetwork=yes and the proxy variables at the relay (stdin passes through)
S=$E3S4_SCRATCH; F=$E3S4_FIXTURES; l=$1; shift
UNIT_NET=yes UNIT_PROGDIR= UNIT_AGYHOME=$S/copies/agy/c_px/.gemini UNIT_CWD=${PX_CWD:-$F/g7/c} UNIT_HIDE=1 UNIT_TMPFS=1 UNIT_ENVS="HTTPS_PROXY=http://127.0.0.1:18080 HTTP_PROXY=http://127.0.0.1:18080 https_proxy=http://127.0.0.1:18080 http_proxy=http://127.0.0.1:18080 NO_PROXY= no_proxy=" UNIT_EXTRA="-p BindReadOnlyPaths=/home/<user>/.nvm/versions/node/v26.12.1/bin" $S/tools/unit.sh g7 agy coai-e3s6-px$l ${PX_TMO:-80} -- /bin/bash -c 'node=/home/<user>/.nvm/versions/node/v26.12.1/bin/node; $node $0/relay.mjs 18080 $0/proxy.sock & sleep 0.7; exec "$@"' $F/g7/c "$@"
