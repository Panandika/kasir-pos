"""
Phase 1 — Export all rows from kasir.db to csv-export-full/
Skips: FTS internal tables, sync_queue, sync_log, audit_log
"""

import sqlite3
import csv
import os

DB_PATH = os.path.join(os.path.dirname(__file__), "kasir.db")
OUT_DIR = os.path.join(os.path.dirname(__file__), "csv-export-full")

SKIP_TABLES = {
    "products_fts",
    "products_fts_config",
    "products_fts_data",
    "products_fts_docsize",
    "products_fts_idx",
    "sync_queue",
    "sync_log",
    "audit_log",
}

os.makedirs(OUT_DIR, exist_ok=True)

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row

tables = [
    row[0] for row in conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
    )
    if row[0] not in SKIP_TABLES
]

print(f"Exporting {len(tables)} tables to {OUT_DIR}/\n")

total_rows = 0
for table in tables:
    cur = conn.execute(f'SELECT * FROM "{table}"')
    columns = [d[0] for d in cur.description]
    rows = cur.fetchall()

    out_path = os.path.join(OUT_DIR, f"{table}.csv")
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(columns)
        writer.writerows(rows)

    total_rows += len(rows)
    print(f"  {table:<40} {len(rows):>7} rows")

conn.close()
print(f"\nDone. {total_rows:,} total rows across {len(tables)} files.")
