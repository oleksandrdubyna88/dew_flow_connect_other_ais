#!/usr/bin/env python3
r"""Exports the code rounds of one coai store, read-only, as JSON (E1.S2 of PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md).

    COAI_EXPORT_DIR=<folder> py -3     scripts/export-code-rounds.py <dataDir>\coai.db <name.json> <since yyyy-mm-dd>     (Windows)
    COAI_EXPORT_DIR=<folder> python3   scripts/export-code-rounds.py <dataDir>/coai.db <name.json> <since yyyy-mm-dd>     (WSL)

One query, no logic beyond it: every CodeReview round that started on or after <since>, joined to its gate session's repo. It
exists because WSL's Node 20 has no `node:sqlite`, and ONE exporter for both sides beats two. The database is opened with
`mode=ro` (a URI), so it cannot be written; it is opened in place, never across a \\wsl.localhost share (WAL). The output
(UTF-8, LF, a JSON array) holds session ids and repository paths: keep it OUTSIDE the repository.

On Windows `python3` is the Microsoft Store alias stub ("Python was not found"); use `py -3`.
The output argument is a bare FILE NAME, never a path: its folder comes from the environment variable COAI_EXPORT_DIR (resolved with
realpath; it must be an existing directory) or, when that is not set, from the current directory. The store must exist, be a regular file and
be named *.db.
Exit: 0 written - 65 bad arguments, a store that is not an existing *.db file, an output argument that is not a bare name, an export folder
that is not an existing directory, or an output that is the store itself / named like a store file (nothing is touched) - 74 the store could
not be read or the output not written. The output is written to a temp file in its folder and renamed over the target only after the dump
succeeded, so a failed export leaves an existing output as it was.
"""
import json
import os
import re
import sqlite3
import sys
import tempfile
from pathlib import Path

# An output named like a store file is refused outright: the arguments are positional, and a swapped pair would otherwise let
# open(out, "w") truncate the live store after the read-only query succeeded.
STORE_SUFFIXES = (".db", ".sqlite", ".sqlite3", "-wal", "-shm", "-journal")
EXPORT_DIR_ENV = "COAI_EXPORT_DIR"

QUERY = (
    "SELECT r.session_id, r.number, r.started_utc, r.completed_utc, r.status, r.caller, r.caller_vendor, s.repo_path "
    "FROM rounds r JOIN sessions s ON s.id = r.session_id "
    "WHERE r.stage = 'CodeReview' AND r.started_utc >= ? ORDER BY r.started_utc, r.id"
)


def output_name_refusal(arg: str) -> str:
    """Why `arg` is not a bare file name, or an empty string. The folder is never part of the argument."""
    if arg in ("", ".", "..") or re.search(r"[\\/:]", arg) or os.path.basename(arg) != arg:
        return "the output must be a bare file name; its folder comes from " + EXPORT_DIR_ENV + " (or the current directory)"
    return ""


def export_directory() -> "Path | None":
    """The folder outputs go to, from trusted sources only: COAI_EXPORT_DIR (realpath) or the current directory; None if it is not a directory."""
    named = os.environ.get(EXPORT_DIR_ENV, "")
    directory = os.path.realpath(named) if named else os.getcwd()
    return Path(directory) if os.path.isdir(directory) else None


def store_refusal(db: Path) -> str:
    """Why `db` is not a store we will open, or an empty string."""
    if db.suffix.lower() != ".db" or not db.is_file():
        return "the store must be an existing file named *.db"
    return ""


def refusal_for(db: Path, out: Path) -> str:
    """Why `out` may not be written, or an empty string. Never touches either file."""
    if os.path.normcase(os.path.realpath(db)) == os.path.normcase(os.path.realpath(out)) or (
            out.exists() and db.exists() and os.path.samefile(db, out)):
        return "the output is the same file as the store"
    if out.name.lower().endswith(STORE_SUFFIXES):
        return "the output is named like a store file (.db, .sqlite, -wal, -shm, -journal)"
    return ""


def export(db: Path, out: Path, since: str) -> int:
    """Writes the rounds started on or after `since` to `out` (through a temp file beside it); returns how many."""
    uri = f"{db.resolve().as_uri()}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    try:
        connection.row_factory = sqlite3.Row
        rows = [dict(row) for row in connection.execute(QUERY, (since,))]
    finally:
        connection.close()
    handle = tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n", dir=out.parent, prefix=f"{out.name}.", suffix=".tmp", delete=False)
    try:
        with handle:
            json.dump(rows, handle, ensure_ascii=False)
            handle.write("\n")
        os.replace(handle.name, out)
    finally:
        if os.path.exists(handle.name):
            os.unlink(handle.name)  # our own temp file, by the exact name it was created under
    return len(rows)


def main(argv: list[str]) -> int:
    if len(argv) != 3 or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", argv[2]):
        print("usage: [COAI_EXPORT_DIR=<folder>] export-code-rounds.py <coai.db> <name.json> <since yyyy-mm-dd>", file=sys.stderr)
        return 65
    db = Path(argv[0])
    reason = store_refusal(db) or output_name_refusal(argv[1])
    directory = None if reason else export_directory()
    if not reason and directory is None:
        reason = EXPORT_DIR_ENV + " must name an existing directory"
    out = directory / os.path.basename(argv[1]) if directory is not None else None  # basename at the join: the folder is never taken from the argument
    reason = reason or refusal_for(db, out)
    if reason:
        print(f"refused: {reason}", file=sys.stderr)
        return 65
    try:
        count = export(db, out, argv[2])
    except (sqlite3.Error, OSError) as error:
        print(f"export failed: {type(error).__name__}", file=sys.stderr)
        return 74
    print(f"{count} code rounds written", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
