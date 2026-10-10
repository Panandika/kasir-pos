using System;
using System.Collections.Generic;

namespace Kasir.CloudSync.Pull
{
    // One row of Supabase pos_stock_requests (dashboard migration 0058, RALPLAN 4.1):
    // a dashboard write the hub register must apply to its kasir.db. Quantities and
    // money are INTEGER x100 like the POS ledger.
    //
    // Contract per request_kind (WP-03 RPCs write it, PosRequestApplier reads it):
    //
    //   OPNAME       product_code; qty = COUNTED qty (physical count, x100, NOT a delta);
    //                unit_cost NULL (ignored if set: the cost is the local average, OB-14);
    //                happened_at = the product's opname time (first active entry's
    //                counted_at, D4 x D11); doc_no = local adjustment journal (optional,
    //                default 'OPN-DB-yyMMdd' of happened_at).
    //                The hub computes delta = qty - on-hand AT happened_at (on-hand now
    //                minus movements after it, PR-K6) and writes one OPNAME movement.
    //   PURCHASE     product_code; qty > 0 (x100, stock unit = pcs, already converted
    //                from dus); unit_cost (x100 money per stock unit) required;
    //                vendor_code; doc_no = receipt number (required, shared by the lines
    //                of one receipt); payload.po_no or payload.po_doc_no (what dashboard
    //                0059 validate_receipt writes; optional) -> purchase_items.order_ref.
    //   RETURN_OUT   product_code; qty > 0 (x100); unit_cost (x100, NULL = local
    //                average); vendor_code; doc_no = return number (required);
    //                payload.ref_no or payload.original_doc_no (0059 validate_return;
    //                optional, the receipt returned against).
    //   VENDOR_BILL  vendor_code (required); doc_no = bill number (required);
    //                payload.amount or payload.total (0059 post_vendor_bill writes total;
    //                x100, required) total payable; payload.gross_amount,
    //                payload.disc_amount (x100, optional); payload.due_date and
    //                payload.bill_date ('YYYY-MM-DD', optional; bill_date defaults to the
    //                WIB date of happened_at); payload.vendor_invoice_no (optional).
    //                payload.bill_type 'credit_note' (+ payload.reverses_doc_no, the bill
    //                it reverses) lowers that bill's payables_register value by the total
    //                instead of adding a payable; it waits until the bill is applied.
    //   PRODUCT_STATUS product_code; payload.status 'A' | 'I' | 'D' (default 'A').
    //   NEW_PRODUCT  product_code (dashboard 'NP' code, OB-10); payload.name (required);
    //                payload.dept_code, unit, price, buying_price, cost_price (x100),
    //                vendor_code, status (optional). An existing code is left unchanged.
    //   BARCODE_LINK no-op on the POS (barcodes were dropped, Migration_005); marked applied.
    //
    // OPNAME / PURCHASE / RETURN_OUT on a non-stock code (SalesService.IsNonStockItem:
    // 1/2/44/99, AL/AT/PR/PL/MY/LL; K1/K4) are rejected: nothing is written and the row
    // gets failed_at / failed_reason / failed_by_register (dashboard 0072), which takes it
    // out of the pending fetch. The dashboard never queues them (0072); older rows may exist.
    //
    // (request_kind, idempotency_key) is UNIQUE in Supabase and in local applied_requests.
    public sealed class PosStockRequest
    {
        public Guid Id { get; set; }
        public string RequestKind { get; set; }
        public string IdempotencyKey { get; set; }
        public string ProductCode { get; set; }
        public int? Qty { get; set; }
        public long? UnitCost { get; set; }
        public string VendorCode { get; set; }
        public string DocNo { get; set; }
        public string TargetRegister { get; set; }
        public string PayloadJson { get; set; }
        public DateTimeOffset HappenedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public static class PosRequestKinds
    {
        public const string Opname = "OPNAME";
        public const string Purchase = "PURCHASE";
        public const string ReturnOut = "RETURN_OUT";
        public const string VendorBill = "VENDOR_BILL";
        public const string ProductStatus = "PRODUCT_STATUS";
        public const string NewProduct = "NEW_PRODUCT";
        public const string BarcodeLink = "BARCODE_LINK";

        // Tie-break inside one created_at (one dashboard RPC writes all its rows in one
        // transaction, so they share now()): a product must exist before stock moves on
        // it, and stock-in lands before an opname compares against on-hand.
        public static int Priority(string kind)
        {
            switch (kind)
            {
                case NewProduct: return 0;
                case ProductStatus: return 1;
                case BarcodeLink: return 2;
                case Purchase: return 3;
                case ReturnOut: return 4;
                case Opname: return 5;
                case VendorBill: return 6;
                default: return 9;
            }
        }

        // Kinds that write stock movements (their relative order per product matters).
        public static bool MovesStock(string kind) =>
            kind == Opname || kind == Purchase || kind == ReturnOut;

        // The order PullService applies a fetched batch in (and the SQL ORDER BY).
        public static readonly IComparer<PosStockRequest> ApplyOrder = Comparer<PosStockRequest>.Create((a, b) =>
        {
            int c = a.CreatedAt.CompareTo(b.CreatedAt);
            if (c != 0) return c;
            c = Priority(a.RequestKind).CompareTo(Priority(b.RequestKind));
            if (c != 0) return c;
            return a.Id.CompareTo(b.Id);
        });
    }
}
