#!/usr/bin/env python3
r"""Exports the code rounds of one coai store, read-only, as JSON (E1.S2 of PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md).

    py -3     scripts/export-code-rounds.py <dataDir>\coai.db <out.json> <since yyyy-mm-dd>     (Windows)
    python3   scripts/export-code-rounds.py <dataDir>/coai.db <out.json> <since yyyy-mm-dd>     (WSL)

One query, no logic beyond it: every CodeReview round that started on or after <since>, joined to its gate session's repo. It
exists because WSL's Node 20 has no `node:sqlite`, and ONE exporter for both sides beats two. The database is opened with
`mode=ro` (a URI), so it cannot be written; it is opened in place, never across a \\wsl.localhost share (WAL). The output
(UTF-8, LF, a JSON array) holds session ids and repository paths: keep it OUTSIDE the repository.

On Windows `python3` is the Microsoft Store alias stub ("Python was not found"); use `py -3`.
Exit: 0 written - 65 bad arguments - 74 the store could not be read or the output not written.
"""
import json
import re
import sqlite3
import sys
from pathlib import Path

QUERY = (
    "SELECT r.session_id, r.number, r.started_utc, r.completed_utc, r.status, r.caller, r.caller_vendor, s.repo_path "
    "FROM rounds r JOIN sessions s ON s.id = r.session_id "
    "WHERE r.stage = 'CodeReview' AND r.started_utc >= ? ORDER BY r.started_utc, r.id"
)


def export(db: Path, out: Path, since: str) -> int:
    """Writes the rounds started on or after `since` to `out`; returns how many."""
    uri = f"{db.resolve().as_uri()}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    try:
        connection.row_factory = sqlite3.Row
        rows = [dict(row) for row in connection.execute(QUERY, (since,))]
    finally:
        connection.close()
    with open(out, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(rows, handle, ensure_ascii=False)
        handle.write("\n")
    return len(rows)


def main(argv: list[str]) -> int:
    if len(argv) != 3 or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", argv[2]):
        print("usage: export-code-rounds.py <coai.db> <out.json> <since yyyy-mm-dd>", file=sys.stderr)
        return 65
    try:
        count = export(Path(argv[0]), Path(argv[1]), argv[2])
    except (sqlite3.Error, OSError) as error:
        print(f"export failed: {type(error).__name__}", file=sys.stderr)
        return 74
    print(f"{count} code rounds written", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
