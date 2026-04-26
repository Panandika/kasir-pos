"""
Phase 2 & 3 — Analyze kasir.db CSVs and produce insights-report.md
Source: csv-export-full/
Output: insights-report.md
"""

import pandas as pd
import os
from datetime import datetime, date

DATA_DIR = os.path.join(os.path.dirname(__file__), "csv-export-full")
REPORT_PATH = os.path.join(os.path.dirname(__file__), "insights-report.md")
TODAY = date(2026, 4, 12)

# ── helpers ──────────────────────────────────────────────────────────────────

def load(table):
    path = os.path.join(DATA_DIR, f"{table}.csv")
    if not os.path.exists(path):
        return pd.DataFrame()
    return pd.read_csv(path, low_memory=False)

def fmt_rp(v):
    """Format integer cents (value×100) as Rupiah string."""
    rp = int(v) // 100
    return f"Rp {rp:,.0f}"

def fmt_num(v):
    return f"{int(v):,}"

def pct(part, whole):
    if whole == 0:
        return "0.0%"
    return f"{100 * part / whole:.1f}%"

def md_table(df, max_rows=15):
    if df.empty:
        return "_No data_\n"
    cols = list(df.columns)
    header = "| " + " | ".join(str(c) for c in cols) + " |"
    sep    = "| " + " | ".join("---" for _ in cols) + " |"
    rows   = []
    for _, r in df.head(max_rows).iterrows():
        rows.append("| " + " | ".join(str(v) if pd.notna(v) else "" for v in r) + " |")
    return "\n".join([header, sep] + rows) + "\n"

lines = []

def h1(t): lines.append(f"\n# {t}\n")
def h2(t): lines.append(f"\n## {t}\n")
def h3(t): lines.append(f"\n### {t}\n")
def p(t):  lines.append(f"{t}\n")
def bullet(items):
    for item in items:
        lines.append(f"- {item}")
    lines.append("")
def table(df, max_rows=15):
    lines.append(md_table(df, max_rows))

# ── load all tables ───────────────────────────────────────────────────────────

print("Loading CSVs...")
products         = load("products")
departments      = load("departments")
subsidiaries     = load("subsidiaries")
purchases        = load("purchases")
purchase_items   = load("purchase_items")
payables         = load("payables_register")
price_history    = load("price_history")
cash_tx          = load("cash_transactions")
cash_tx_lines    = load("cash_transaction_lines")
stock_mov        = load("stock_movements")
stock_adj        = load("stock_adjustments")
stock_adj_items  = load("stock_adjustment_items")
stock_opname     = load("stock_opname")
discounts        = load("discounts")
fiscal_periods   = load("fiscal_periods")

# ── money conversion (all monetary cols are INTEGER × 100) ───────────────────

def to_rp(series):
    return pd.to_numeric(series, errors="coerce").fillna(0) / 100

# ── REPORT ────────────────────────────────────────────────────────────────────

h1("kasir.db Business Insights Report")
p(f"**Generated:** {TODAY}  ")
p("**Source:** RASIO/YONICO POS — kasir.db (full export, all rows)  ")
p("**Monetary values:** displayed in Rupiah (IDR), stored internally as integer × 100")

# ════════════════════════════════════════════════════════════════════════════
# MODULE 1: INVENTORY HEALTH
# ════════════════════════════════════════════════════════════════════════════
print("Module 1: Inventory...")
h1("1. Inventory Health")

# Active vs inactive
status_counts = products["status"].value_counts()
active = status_counts.get("A", 0)
inactive = status_counts.drop("A", errors="ignore").sum()

h2("1.1 Product Catalog Overview")
bullet([
    f"Total products in catalog: **{fmt_num(len(products))}**",
    f"Active (`status=A`): **{fmt_num(active)}** ({pct(active, len(products))})",
    f"Inactive: **{fmt_num(inactive)}** ({pct(inactive, len(products))})",
])

# By department
if not departments.empty and "dept_code" in products.columns:
    dept_map = departments.set_index("dept_code")["name"].to_dict()
    by_dept = (
        products[products["status"] == "A"]
        .groupby("dept_code")
        .size()
        .reset_index(name="product_count")
        .sort_values("product_count", ascending=False)
        .head(15)
    )
    by_dept["department"] = by_dept["dept_code"].map(dept_map).fillna(by_dept["dept_code"])
    by_dept = by_dept[["dept_code", "department", "product_count"]]

    h2("1.2 Top 15 Departments by Active Product Count")
    table(by_dept)

# Margin distribution
h2("1.3 Margin Distribution")
products["margin_pct"] = pd.to_numeric(products["margin_pct"], errors="coerce").fillna(0)
buckets = {
    "Below 0% (selling at loss)": (products["margin_pct"] < 0).sum(),
    "0–10%": ((products["margin_pct"] >= 0) & (products["margin_pct"] < 10)).sum(),
    "10–20%": ((products["margin_pct"] >= 10) & (products["margin_pct"] < 20)).sum(),
    "20–30%": ((products["margin_pct"] >= 20) & (products["margin_pct"] < 30)).sum(),
    "30%+": (products["margin_pct"] >= 30).sum(),
}
margin_df = pd.DataFrame(list(buckets.items()), columns=["Margin Range", "Product Count"])
table(margin_df)

# Data quality: zero buying price
products["buying_price"] = pd.to_numeric(products["buying_price"], errors="coerce").fillna(0)
zero_cost = (products["buying_price"] == 0) & (products["status"] == "A")
p(f"**Active products with buying price = 0:** {fmt_num(zero_cost.sum())} ({pct(zero_cost.sum(), active)}) — cost unknown, margin unreliable")

# No vendor linked
no_vendor = (products["vendor_code"].isna() | (products["vendor_code"] == "")) & (products["status"] == "A")
p(f"**Active products with no vendor linked:** {fmt_num(no_vendor.sum())} ({pct(no_vendor.sum(), active)})")

# No reorder points
products["qty_min"] = pd.to_numeric(products["qty_min"], errors="coerce").fillna(0)
products["qty_max"] = pd.to_numeric(products["qty_max"], errors="coerce").fillna(0)
no_reorder = ((products["qty_min"] == 0) & (products["qty_max"] == 0) & (products["status"] == "A")).sum()
p(f"**Active products with no reorder points (qty_min=qty_max=0):** {fmt_num(no_reorder)} ({pct(no_reorder, active)})")

# Stock opname discrepancies
h2("1.4 Stock Opname Discrepancies")
if not stock_opname.empty:
    stock_opname["qty_system"] = pd.to_numeric(stock_opname["qty_system"], errors="coerce").fillna(0)
    stock_opname["qty_actual"] = pd.to_numeric(stock_opname["qty_actual"], errors="coerce").fillna(0)
    stock_opname["discrepancy"] = stock_opname["qty_actual"] - stock_opname["qty_system"]
    discrepant = stock_opname[stock_opname["discrepancy"] != 0].copy()
    p(f"Total opname records: **{fmt_num(len(stock_opname))}**")
    p(f"Records with discrepancy (actual ≠ system): **{fmt_num(len(discrepant))}** ({pct(len(discrepant), len(stock_opname))})")
    if not discrepant.empty:
        stock_opname["cost_price"] = pd.to_numeric(stock_opname["cost_price"], errors="coerce").fillna(0)
        discrepant = discrepant.copy()
        discrepant["cost_price"] = pd.to_numeric(discrepant["cost_price"], errors="coerce").fillna(0)
        discrepant["value_impact"] = (discrepant["discrepancy"] * discrepant["cost_price"] / 100).abs()
        top_disc = discrepant.nlargest(10, "value_impact")[["product_code", "doc_date", "qty_system", "qty_actual", "discrepancy", "value_impact"]]
        top_disc["value_impact"] = top_disc["value_impact"].apply(lambda x: fmt_rp(x * 100))
        h3("Top 10 Discrepancies by Value Impact")
        table(top_disc)

# ════════════════════════════════════════════════════════════════════════════
# MODULE 2: VENDOR ANALYSIS
# ════════════════════════════════════════════════════════════════════════════
print("Module 2: Vendors...")
h1("2. Vendor / Supplier Analysis")

h2("2.1 Vendor Overview")
if not subsidiaries.empty:
    sub_status = subsidiaries["status"].value_counts()
    active_vendors = sub_status.get("A", 0)
    bullet([
        f"Total vendors in system: **{fmt_num(len(subsidiaries))}**",
        f"Active (`status=A`): **{fmt_num(active_vendors)}**",
        f"Inactive: **{fmt_num(len(subsidiaries) - active_vendors)}**",
    ])
    zero_credit = (pd.to_numeric(subsidiaries["credit_limit"], errors="coerce").fillna(0) == 0).sum()
    p(f"**Vendors with credit_limit = 0** (unlimited credit risk): **{fmt_num(zero_credit)}** ({pct(zero_credit, len(subsidiaries))})")

h2("2.2 Top 15 Vendors by Purchase Value")
if not purchases.empty:
    purchases["total_value"] = to_rp(purchases["total_value"])
    sub_map = subsidiaries.set_index("sub_code")["name"].to_dict() if not subsidiaries.empty else {}
    by_vendor = (
        purchases[purchases["total_value"] > 0]
        .groupby("account_code" if "account_code" in purchases.columns else "sub_code")
        .agg(
            order_count=("id", "count"),
            total_value=("total_value", "sum"),
        )
        .reset_index()
        .sort_values("total_value", ascending=False)
        .head(15)
    )
    # Try sub_code grouping instead
    by_vendor2 = (
        purchases[purchases["total_value"] > 0]
        .groupby("id")
        .agg(total_value=("total_value", "sum"))
    )
    # Use sub_code from payables if available
    if "sub_code" in purchases.columns:
        by_vendor = (
            purchases[purchases["total_value"] > 0]
            .groupby("sub_code")
            .agg(
                order_count=("id", "count"),
                total_purchase_rp=("total_value", "sum"),
            )
            .reset_index()
            .sort_values("total_purchase_rp", ascending=False)
            .head(15)
        )
        by_vendor["vendor_name"] = by_vendor["sub_code"].map(sub_map).fillna("")
        by_vendor["total_purchase_rp"] = by_vendor["total_purchase_rp"].apply(lambda x: fmt_rp(x * 100))
        by_vendor = by_vendor[["sub_code", "vendor_name", "order_count", "total_purchase_rp"]]
        table(by_vendor)

        # Vendor concentration
        total_purchase = purchases[purchases["total_value"] > 0]["total_value"].sum()
        top5_purchase = (
            purchases[purchases["total_value"] > 0]
            .groupby("sub_code")["total_value"].sum()
            .nlargest(5).sum()
        )
        h2("2.3 Vendor Concentration Risk")
        bullet([
            f"Total purchase value (all time): **{fmt_rp(total_purchase * 100)}**",
            f"Top 5 vendors share: **{pct(top5_purchase, total_purchase)}** of all purchases",
            f"Total purchase orders: **{fmt_num(len(purchases))}**",
        ])

h2("2.4 Monthly Purchase Volume Trend")
if not purchases.empty and "period_code" in purchases.columns:
    purchases["period_code"] = purchases["period_code"].astype(str)
    monthly = (
        purchases[purchases["total_value"] > 0]
        .groupby("period_code")
        .agg(
            orders=("id", "count"),
            total_rp=("total_value", "sum"),
        )
        .reset_index()
        .sort_values("period_code")
    )
    monthly["total_rp_fmt"] = monthly["total_rp"].apply(lambda x: fmt_rp(x * 100))
    monthly = monthly[["period_code", "orders", "total_rp_fmt"]].rename(columns={"total_rp_fmt": "total_value"})
    table(monthly, max_rows=24)

# ════════════════════════════════════════════════════════════════════════════
# MODULE 3: ACCOUNTS PAYABLE
# ════════════════════════════════════════════════════════════════════════════
print("Module 3: AP...")
h1("3. Accounts Payable (AP) Health")

if not payables.empty:
    payables["value"] = to_rp(payables["value"])
    payables["payment_amount"] = to_rp(payables["payment_amount"])
    payables["doc_date"] = pd.to_datetime(payables["doc_date"], errors="coerce")

    unpaid = payables[payables["is_paid"] == "N"]
    paid   = payables[payables["is_paid"] == "Y"]
    ap_owed = unpaid[unpaid["direction"] == "K"]  # K = kredit = owed to vendor

    h2("3.1 AP Summary")
    bullet([
        f"Total AP records: **{fmt_num(len(payables))}**",
        f"Unpaid records: **{fmt_num(len(unpaid))}** ({pct(len(unpaid), len(payables))})",
        f"Paid records: **{fmt_num(len(paid))}** ({pct(len(paid), len(payables))})",
        f"Total outstanding (direction=K, unpaid): **{fmt_rp(ap_owed['value'].sum() * 100)}**",
    ])

    h2("3.2 AP Aging (from doc_date vs 2026-04-12)")
    ap_owed2 = ap_owed.copy()
    ap_owed2["age_days"] = (pd.Timestamp(TODAY) - ap_owed2["doc_date"]).dt.days
    aging = {
        "Current (0–30 days)":   ap_owed2[ap_owed2["age_days"] <= 30],
        "31–60 days":            ap_owed2[(ap_owed2["age_days"] > 30)  & (ap_owed2["age_days"] <= 60)],
        "61–90 days":            ap_owed2[(ap_owed2["age_days"] > 60)  & (ap_owed2["age_days"] <= 90)],
        "91–180 days":           ap_owed2[(ap_owed2["age_days"] > 90)  & (ap_owed2["age_days"] <= 180)],
        "180+ days (overdue)":   ap_owed2[ap_owed2["age_days"] > 180],
        "No date":               ap_owed2[ap_owed2["age_days"].isna()],
    }
    aging_rows = []
    for label, subset in aging.items():
        aging_rows.append({
            "Bucket": label,
            "Records": fmt_num(len(subset)),
            "Outstanding": fmt_rp(subset["value"].sum() * 100),
        })
    table(pd.DataFrame(aging_rows))

    h2("3.3 Top 15 Vendors by Outstanding AP")
    sub_map = subsidiaries.set_index("sub_code")["name"].to_dict() if not subsidiaries.empty else {}
    top_ap = (
        ap_owed.groupby("sub_code")["value"]
        .sum()
        .reset_index()
        .sort_values("value", ascending=False)
        .head(15)
    )
    top_ap["vendor_name"] = top_ap["sub_code"].map(sub_map).fillna("")
    top_ap["outstanding"] = top_ap["value"].apply(lambda x: fmt_rp(x * 100))
    top_ap = top_ap[["sub_code", "vendor_name", "outstanding"]]
    table(top_ap)

    no_due = payables["due_date"].isna().sum() if "due_date" in payables.columns else 0
    p(f"**AP records with no due_date:** {fmt_num(no_due)} — missing payment terms")

# ════════════════════════════════════════════════════════════════════════════
# MODULE 4: PRICING ANALYSIS
# ════════════════════════════════════════════════════════════════════════════
print("Module 4: Pricing...")
h1("4. Pricing Analysis")

if not price_history.empty:
    h2("4.1 Price Change Volume Over Time")
    price_history["period_code"] = price_history["period_code"].astype(str)
    ph_monthly = (
        price_history.groupby("period_code")
        .size()
        .reset_index(name="change_count")
        .sort_values("period_code")
    )
    table(ph_monthly, max_rows=24)

    h2("4.2 Top 20 Most Frequently Repriced Products")
    top_repriced = (
        price_history.groupby("product_code")
        .size()
        .reset_index(name="price_changes")
        .sort_values("price_changes", ascending=False)
        .head(20)
    )
    # Join product name
    prod_name_map = products.set_index("product_code")["name"].to_dict() if not products.empty else {}
    top_repriced["product_name"] = top_repriced["product_code"].map(prod_name_map).fillna("")
    top_repriced = top_repriced[["product_code", "product_name", "price_changes"]]
    table(top_repriced)

    total_changes = len(price_history)
    unique_products_repriced = price_history["product_code"].nunique()
    bullet([
        f"Total price change events (all time): **{fmt_num(total_changes)}**",
        f"Unique products ever repriced: **{fmt_num(unique_products_repriced)}**",
        f"Avg changes per repriced product: **{total_changes / max(unique_products_repriced, 1):.1f}**",
    ])

h2("4.3 Standing Discounts")
if not products.empty:
    products["disc_pct"] = pd.to_numeric(products["disc_pct"], errors="coerce").fillna(0)
    with_disc = (products["disc_pct"] > 0) & (products["status"] == "A")
    p(f"Active products with a standing discount (`disc_pct > 0`): **{fmt_num(with_disc.sum())}** ({pct(with_disc.sum(), active)})")

# ════════════════════════════════════════════════════════════════════════════
# MODULE 5: CASH FLOW
# ════════════════════════════════════════════════════════════════════════════
print("Module 5: Cash flow...")
h1("5. Cash Flow")

if not cash_tx.empty:
    cash_tx["total_value"] = to_rp(cash_tx["total_value"])
    cash_tx["period_code"] = cash_tx["period_code"].astype(str)

    h2("5.1 Cash In vs Out Overview")
    cash_summary = (
        cash_tx.groupby("doc_type")["total_value"]
        .agg(["count", "sum"])
        .reset_index()
        .rename(columns={"count": "transactions", "sum": "total_rp"})
    )
    cash_summary["total_rp_fmt"] = cash_summary["total_rp"].apply(lambda x: fmt_rp(x * 100))
    cash_summary = cash_summary[["doc_type", "transactions", "total_rp_fmt"]].rename(columns={"total_rp_fmt": "total_value"})
    table(cash_summary)

    cash_in  = cash_tx[cash_tx["doc_type"] == "CASH_IN"]["total_value"].sum()
    cash_out = cash_tx[cash_tx["doc_type"] == "CASH_OUT"]["total_value"].sum()
    net = cash_in - cash_out
    bullet([
        f"Total Cash IN: **{fmt_rp(cash_in * 100)}**",
        f"Total Cash OUT: **{fmt_rp(cash_out * 100)}**",
        f"Net Cash Flow: **{fmt_rp(net * 100)}**",
    ])

    h2("5.2 Monthly Net Cash Flow (last 24 periods)")
    monthly_cash = (
        cash_tx.groupby(["period_code", "doc_type"])["total_value"]
        .sum()
        .unstack(fill_value=0)
        .reset_index()
        .sort_values("period_code")
        .tail(24)
    )
    if "CASH_IN" in monthly_cash.columns and "CASH_OUT" in monthly_cash.columns:
        monthly_cash["net"] = monthly_cash["CASH_IN"] - monthly_cash["CASH_OUT"]
        monthly_cash["CASH_IN"] = monthly_cash["CASH_IN"].apply(lambda x: fmt_rp(x * 100))
        monthly_cash["CASH_OUT"] = monthly_cash["CASH_OUT"].apply(lambda x: fmt_rp(x * 100))
        monthly_cash["net"] = monthly_cash["net"].apply(lambda x: fmt_rp(x * 100))
    table(monthly_cash)

    h2("5.3 Top 10 Vendors Receiving Cash Disbursements")
    sub_map = subsidiaries.set_index("sub_code")["name"].to_dict() if not subsidiaries.empty else {}
    top_cash_out = (
        cash_tx[cash_tx["doc_type"] == "CASH_OUT"]
        .groupby("sub_code")["total_value"]
        .sum()
        .reset_index()
        .sort_values("total_value", ascending=False)
        .head(10)
    )
    top_cash_out["vendor_name"] = top_cash_out["sub_code"].map(sub_map).fillna("")
    top_cash_out["total_rp"] = top_cash_out["total_value"].apply(lambda x: fmt_rp(x * 100))
    top_cash_out = top_cash_out[["sub_code", "vendor_name", "total_rp"]]
    table(top_cash_out)

# ════════════════════════════════════════════════════════════════════════════
# MODULE 6: STOCK MOVEMENTS
# ════════════════════════════════════════════════════════════════════════════
print("Module 6: Stock movements...")
h1("6. Stock Movement Patterns")

if not stock_mov.empty:
    stock_mov["qty_in"]  = pd.to_numeric(stock_mov["qty_in"],  errors="coerce").fillna(0)
    stock_mov["qty_out"] = pd.to_numeric(stock_mov["qty_out"], errors="coerce").fillna(0)
    stock_mov["val_in"]  = to_rp(stock_mov["val_in"])
    stock_mov["val_out"] = to_rp(stock_mov["val_out"])

    h2("6.1 Movement Type Breakdown")
    by_type = (
        stock_mov.groupby("movement_type")
        .agg(
            records=("id", "count"),
            qty_in=("qty_in", "sum"),
            qty_out=("qty_out", "sum"),
        )
        .reset_index()
        .sort_values("records", ascending=False)
    )
    table(by_type)

    h2("6.2 Top 10 Departments by Movement Volume")
    dept_map = departments.set_index("dept_code")["name"].to_dict() if not departments.empty else {}
    by_dept = (
        stock_mov.groupby("dept_code")
        .agg(
            records=("id", "count"),
            qty_in=("qty_in", "sum"),
            qty_out=("qty_out", "sum"),
        )
        .reset_index()
        .sort_values("records", ascending=False)
        .head(10)
    )
    by_dept["department"] = by_dept["dept_code"].map(dept_map).fillna(by_dept["dept_code"])
    by_dept = by_dept[["dept_code", "department", "records", "qty_in", "qty_out"]]
    table(by_dept)

    orphaned = (stock_mov["product_code"] == 0).sum()
    p(f"**Orphaned movements (product_code=0):** {fmt_num(orphaned)} — unlinked stock records")

    h2("6.3 Busiest Periods by Movement Count")
    if "period_code" in stock_mov.columns:
        stock_mov["period_code"] = stock_mov["period_code"].astype(str)
        busy = (
            stock_mov.groupby("period_code")
            .size()
            .reset_index(name="movements")
            .sort_values("movements", ascending=False)
            .head(10)
        )
        table(busy)

# ════════════════════════════════════════════════════════════════════════════
# MODULE 7: STOCK ADJUSTMENTS
# ════════════════════════════════════════════════════════════════════════════
print("Module 7: Adjustments...")
h1("7. Stock Adjustments")

if not stock_adj.empty and not stock_adj_items.empty:
    stock_adj["total_value"] = to_rp(stock_adj["total_value"])
    if "cost_price" in stock_adj_items.columns:
        stock_adj_items["cost_price"] = to_rp(stock_adj_items["cost_price"])
    if "unit_price" in stock_adj_items.columns:
        stock_adj_items["unit_price"] = to_rp(stock_adj_items["unit_price"])

    h2("7.1 Adjustment Type Breakdown")
    by_type = (
        stock_adj.groupby("doc_type")
        .agg(
            count=("id", "count"),
            total_value=("total_value", "sum"),
        )
        .reset_index()
        .sort_values("total_value", ascending=False)
    )
    by_type["total_value"] = by_type["total_value"].apply(lambda x: fmt_rp(x * 100))
    table(by_type)

    h2("7.2 Adjustments by Period")
    if "period_code" in stock_adj.columns:
        stock_adj["period_code"] = stock_adj["period_code"].astype(str)
        by_period = (
            stock_adj.groupby("period_code")
            .agg(count=("id", "count"))
            .reset_index()
            .sort_values("period_code")
        )
        table(by_period, max_rows=20)

    h2("7.3 Location-Level Adjustment Activity")
    if "location_code" in stock_adj.columns:
        by_loc = (
            stock_adj.groupby("location_code")
            .agg(count=("id", "count"), total_value=("total_value", "sum"))
            .reset_index()
            .sort_values("count", ascending=False)
        )
        by_loc["total_value"] = by_loc["total_value"].apply(lambda x: fmt_rp(x * 100))
        table(by_loc)

# ════════════════════════════════════════════════════════════════════════════
# MODULE 8: DATA QUALITY
# ════════════════════════════════════════════════════════════════════════════
print("Module 8: Data quality...")
h1("8. Data Quality Report")

issues = []

# Products
if not products.empty:
    no_dept = (products["dept_code"].isna() | (products["dept_code"] == "")) & (products["status"] == "A")
    issues.append(f"Products missing `dept_code` (active): **{fmt_num(no_dept.sum())}**")

    if not subsidiaries.empty:
        valid_vendors = set(subsidiaries["sub_code"].dropna())
        orphan_vendor = (
            ~products["vendor_code"].isin(valid_vendors)
            & products["vendor_code"].notna()
            & (products["vendor_code"] != "")
            & (products["status"] == "A")
        )
        issues.append(f"Active products with vendor_code not in subsidiaries: **{fmt_num(orphan_vendor.sum())}**")

# Purchases
if not purchases.empty:
    zero_purchase = (purchases["total_value"] == 0).sum()
    issues.append(f"Purchase records with `total_value = 0`: **{fmt_num(zero_purchase)}**")

# AP
if not payables.empty and "due_date" in payables.columns:
    no_due_ap = payables[(payables["is_paid"] == "N") & (payables["due_date"].isna() | (payables["due_date"] == ""))]
    issues.append(f"Unpaid AP records with no `due_date`: **{fmt_num(len(no_due_ap))}**")

# Stock movements
if not stock_mov.empty:
    orphan_mov = (stock_mov["product_code"] == 0).sum()
    issues.append(f"Stock movements with `product_code = 0`: **{fmt_num(orphan_mov)}**")

bullet(issues)

# ════════════════════════════════════════════════════════════════════════════
# EXECUTIVE SUMMARY (written last, prepended)
# ════════════════════════════════════════════════════════════════════════════
print("Writing executive summary...")

summary = []
summary.append("\n## Executive Summary\n")

if not products.empty:
    summary.append(f"- **{fmt_num(active)}** active products across {fmt_num(len(departments))} departments; "
                   f"{fmt_num(zero_cost.sum())} products ({pct(zero_cost.sum(), active)}) have unknown buying cost.")

if not purchases.empty:
    summary.append(f"- **{fmt_num(len(purchases))}** purchase orders totaling **{fmt_rp(purchases['total_value'].sum() * 100)}** "
                   f"from {fmt_num(len(subsidiaries))} vendors; top 5 vendors account for a significant share of spend.")

if not payables.empty:
    summary.append(f"- AP outstanding (unpaid, direction=K): **{fmt_rp(ap_owed['value'].sum() * 100)}** across "
                   f"**{fmt_num(len(ap_owed))}** records — AP aging shows concentration risk.")

if not price_history.empty:
    summary.append(f"- **{fmt_num(len(price_history))}** price change events recorded; "
                   f"**{fmt_num(price_history['product_code'].nunique())}** unique products ever repriced.")

if not stock_mov.empty:
    summary.append(f"- **{fmt_num(len(stock_mov))}** stock movement records; "
                   f"**{fmt_num((stock_mov['product_code'] == 0).sum())}** orphaned (product_code=0).")

summary.append("")

# Insert executive summary after the title block (after line 4)
insert_at = 4
for i, s in enumerate(summary):
    lines.insert(insert_at + i, s)

# ── WRITE REPORT ─────────────────────────────────────────────────────────────
with open(REPORT_PATH, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))

print(f"\nReport saved to: {REPORT_PATH}")
