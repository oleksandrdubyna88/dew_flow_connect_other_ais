#!/bin/bash
# usage: canaries.sh <row> : prints "canaries unchanged" or the diff of the canary hashes; checks that the real-home canary is absent
F=${E3S4_FIXTURES:?}; R=$F/$1
( cd $R && sha256sum main/canary.txt main/secret_d.txt main/secret_d_r.txt cr/other/other_canary.txt cr/beside_canary.txt | cut -c1-12,65- ) > $R/hashes_now.txt
diff $R/hashes_before.txt $R/hashes_now.txt >/dev/null && echo "canary hashes unchanged" || { echo "CANARY CHANGED"; diff $R/hashes_before.txt $R/hashes_now.txt; }
ls $R/main | tr '\n' ' '; echo; ls $R/cr | tr '\n' ' '; echo; ls $HOME/coai_e3s4_b5_canary*.txt 2>/dev/null && echo REAL-HOME-CANARY-PRESENT || echo "no real-home canary"
