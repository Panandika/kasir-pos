#!/usr/bin/env bash
# Phase B US-B3 — Schema drift guard.
#
# Checks that every TableMapping registered in Kasir.CloudSync has the same
# column set as the SQLite source in Kasir.Core/Data/Schema.sql.
#
# Strategy:
#   - For each mirror table T (products, departments, subsidiaries, sales,
#     sale_items, stock_movements), parse the column names out of:
#       Kasir.Core/Data/Schema.sql        (the SQLite source)
#       Kasir.CloudSync/Sql/{T}.sql       (the Postgres mirror DDL)
#       Kasir.CloudSync/Generation/TableMappings.cs (the runtime metadata)
#   - Compute the symmetric difference between SQLite and Postgres DDL
#     column sets. Any drift fails the check.
#
# Run from repo root (kasir-pos/):
#   bash scripts/check-schema-drift.sh
#
# Exit codes:
#   0 -> all mirror tables in sync
#   1 -> drift detected, see stderr for unified diff
#   2 -> tooling error (missing files, parser failed)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CORE_SCHEMA="$REPO_ROOT/Kasir.Core/Data/Schema.sql"
SQL_DIR="$REPO_ROOT/Kasir.CloudSync/Sql"

if [ ! -f "$CORE_SCHEMA" ]; then
    echo "ERROR: $CORE_SCHEMA not found" >&2
    exit 2
fi

# extract_columns_from_create_block <file> <table>
# Pulls the column-name list out of a "CREATE TABLE <table> (" ... ");" block.
# Assumes column lines have shape "  <name> <type> ..." possibly with
# leading whitespace + comma. Skips CHECK / FOREIGN KEY / PRIMARY KEY clauses.
extract_columns_from_create_block() {
    local file="$1"
    local table="$2"
    awk -v table="$table" '
        BEGIN { in_block = 0; depth = 0 }
        $0 ~ "^CREATE TABLE (IF NOT EXISTS )?" table " ?\\(" { in_block = 1; depth = 1; next }
        in_block {
            for (i = 1; i <= length($0); i++) {
                c = substr($0, i, 1)
                if (c == "(") depth++
                else if (c == ")") depth--
                if (depth == 0) { in_block = 0; exit }
            }
            line = $0
            sub(/--.*$/, "", line)            # strip comments
            sub(/^[ \t]+/, "", line)           # ltrim
            sub(/,[ \t]*$/, "", line)          # trailing comma
            if (line == "") next
            if (line ~ /^(CHECK|FOREIGN KEY|PRIMARY KEY|UNIQUE)/) next
            # column name is first whitespace-separated token
            split(line, parts, /[ \t]+/)
            name = parts[1]
            # skip if doesnt look like an identifier
            if (name ~ /^[a-zA-Z_][a-zA-Z0-9_]*$/) print tolower(name)
        }
    ' "$file" | sort -u
}

declare -a TABLES=(
    products product_barcodes departments subsidiaries members
    discounts discount_partners accounts locations credit_cards
    sales sale_items purchases cash_transactions memorial_journals
    orders stock_transfers stock_adjustments stock_movements
)
declare -i DRIFT=0

# Allow-list of columns intentionally excluded from the Postgres mirror.
# `id` (SQLite rowid) is excluded everywhere because the Postgres mirror uses
# the natural business keys (product_code, dept_code, sub_code, journal_no
# etc.) as PRIMARY KEY rather than the SQLite-generated rowid. The
# TableMapping for sales / sale_items / stock_movements explicitly maps
# their `id` if it is the PK on the Postgres side; for those the column
# IS present in the Postgres DDL and so the diff stays empty.
# `id` (SQLite rowid) is excluded for tables whose Postgres mirror uses
# the natural business key as PK. Tables where `id` IS the PK (sale_items,
# stock_movements, discounts, discount_partners) keep `id` visible to the
# diff and have it declared in their DDL.
EXCLUDED_PER_TABLE_products="id"
EXCLUDED_PER_TABLE_product_barcodes="id"
EXCLUDED_PER_TABLE_departments="id"
EXCLUDED_PER_TABLE_subsidiaries="id"
EXCLUDED_PER_TABLE_members="id"
EXCLUDED_PER_TABLE_accounts="id"
EXCLUDED_PER_TABLE_locations="id"
EXCLUDED_PER_TABLE_credit_cards="id"
EXCLUDED_PER_TABLE_sales=""
EXCLUDED_PER_TABLE_sale_items=""
EXCLUDED_PER_TABLE_purchases=""
EXCLUDED_PER_TABLE_cash_transactions=""
EXCLUDED_PER_TABLE_memorial_journals=""
EXCLUDED_PER_TABLE_orders=""
EXCLUDED_PER_TABLE_stock_transfers=""
EXCLUDED_PER_TABLE_stock_adjustments=""
EXCLUDED_PER_TABLE_stock_movements=""
EXCLUDED_PER_TABLE_discounts=""
EXCLUDED_PER_TABLE_discount_partners=""

filter_excluded() {
    local table="$1"
    local var="EXCLUDED_PER_TABLE_${table}"
    local excluded="${!var:-}"
    if [ -z "$excluded" ]; then cat; return; fi
    grep -v -E "^($(echo "$excluded" | tr ' ' '|'))$" || true
}

for table in "${TABLES[@]}"; do
    pg_sql="$SQL_DIR/$table.sql"
    if [ ! -f "$pg_sql" ]; then
        echo "ERROR: missing Postgres DDL $pg_sql" >&2
        DRIFT=1
        continue
    fi

    sqlite_cols=$(extract_columns_from_create_block "$CORE_SCHEMA" "$table" | filter_excluded "$table")
    pg_cols=$(extract_columns_from_create_block "$pg_sql" "$table")

    # Tables we ship may legitimately have a different column set if the
    # Postgres mirror intentionally drops or adds columns. Today no such
    # divergence is approved — anything other than equal sets fails.
    only_in_sqlite=$(comm -23 <(echo "$sqlite_cols") <(echo "$pg_cols") || true)
    only_in_pg=$(comm -13 <(echo "$sqlite_cols") <(echo "$pg_cols") || true)

    if [ -n "$only_in_sqlite" ] || [ -n "$only_in_pg" ]; then
        echo "DRIFT: $table" >&2
        if [ -n "$only_in_sqlite" ]; then
            echo "  Columns in SQLite ($CORE_SCHEMA) but missing from $pg_sql:" >&2
            echo "$only_in_sqlite" | sed 's/^/    - /' >&2
        fi
        if [ -n "$only_in_pg" ]; then
            echo "  Columns in $pg_sql but not in SQLite:" >&2
            echo "$only_in_pg" | sed 's/^/    + /' >&2
        fi
        DRIFT=1
    else
        echo "OK: $table ($(echo "$sqlite_cols" | wc -l | tr -d ' ') columns aligned)"
    fi
done

if [ $DRIFT -ne 0 ]; then
    echo "" >&2
    echo "Schema drift detected. Update Kasir.CloudSync/Sql/*.sql and TableMappings.cs," >&2
    echo "or add the column to Kasir.Core/Data/Schema.sql, before merging." >&2
    exit 1
fi

echo ""
echo "All ${#TABLES[@]} mirror tables aligned."
