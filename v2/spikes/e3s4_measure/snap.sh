#!/bin/bash
# usage: snap.sh <row> <out>  : every file/symlink/dir name under the row (not .git objects), with sha256-12 for files; plus real-home canary check
F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}; R=$F/$1
( cd $R && find . -path ./main/.git/objects -prune -o -path ./c/home/.codex -prune -o -path ./c/home/.gemini -prune -o \( -type f -o -type l -o -type d \) -print | sort | while read -r p; do
   if [ -f "$p" ] && [ ! -L "$p" ]; then echo "$p $(sha256sum "$p" | cut -c1-12)"; else echo "$p -"; fi; done ) > $2
ls $HOME/coai_e3s4_b5_canary.txt >/dev/null 2>&1 && echo "REAL-HOME-CANARY-PRESENT" >> $2
true
