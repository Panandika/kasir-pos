using System;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class InventoryService
    {
        private readonly SqliteConnection _db;
        private readonly StockMovementRepository _movementRepo;
        private readonly ConfigRepository _configRepo;
        private readonly ProductRepository _productRepo;

        // Single-store default location ('T' = Toko). Schema default is '' which made
        // per-location on-hand queries miss POS movements.
        public const string DefaultLocationCode = "T";

        // Config key: "true" lets the POS cost engine write products.cost_price (perpetual
        // average). Default OFF until Avalonia cutover: FoxPro AVGCOST is authoritative and a
        // POS-side write would be pushed to Supabase via trg_products_sync_u / CloudSync.
        public const string CostEngineOwnsCostPriceKey = "cost_engine_owns_cost_price";

        public InventoryService(SqliteConnection db)
        {
            _db = db;
            _movementRepo = new StockMovementRepository(db);
            _configRepo = new ConfigRepository(db);
            _productRepo = new ProductRepository(db);
        }

        public int GetStockOnHand(string productCode)
        {
            return _movementRepo.GetStockOnHand(productCode);
        }

        public int GetStockOnHandByLocation(string productCode, string locationCode)
        {
            return _movementRepo.GetStockOnHandByLocation(productCode, locationCode);
        }

        // qty is a ledger qty (x100, see StockQty); the result is money x100.
        public long GetCostPrice(string productCode, int qty)
        {
            string method = _configRepo.Get("costing_method") ?? "AVG";
            if (method == "FIFO")
            {
                return CalculateFifoCost(productCode, qty);
            }
            return StockQty.Value(CalculateAverageCost(productCode), qty);
        }

        public long CalculateFifoCost(string productCode, int qtyNeeded)
        {
            var purchases = _movementRepo.GetPurchaseMovements(productCode);

            int totalIn = 0;
            foreach (var p in purchases)
            {
                totalIn += p.QtyIn;
            }

            int currentOnHand = _movementRepo.GetStockOnHand(productCode);
            int alreadyConsumed = Math.Max(0, totalIn - currentOnHand);

            long totalCost = 0;
            int remaining = qtyNeeded;
            int skipped = 0;

            foreach (var lot in purchases)
            {
                int lotQty = lot.QtyIn;

                // Skip already consumed lots
                if (skipped < alreadyConsumed)
                {
                    int toSkip = Math.Min(lotQty, alreadyConsumed - skipped);
                    skipped += toSkip;
                    lotQty -= toSkip;
                }

                if (lotQty <= 0) continue;

                int take = Math.Min(lotQty, remaining);
                // val_in is money x100 for the whole lot and qty_in is x100, so the
                // per-unit cost (money x100) is val_in * 100 / qty_in.
                long unitCost = lot.QtyIn > 0 ? lot.ValIn * StockQty.Scale / lot.QtyIn : 0;
                totalCost += StockQty.Value(unitCost, take);
                remaining -= take;

                if (remaining <= 0) break;
            }

            return totalCost;
        }

        // Unit cost (x100 money) for COGS and stock valuation: the perpetual moving average
        // kept in products.cost_price (FoxPro AVGCOST via the snapshot; maintained by
        // RecordStockIn once CostEngineOwnsCostPriceKey is on). Fallback when it is 0: last PURCHASE unit_price, then 0.
        public long CalculateAverageCost(string productCode)
        {
            var product = _productRepo.GetByCode(productCode);
            if (product != null && product.CostPrice > 0) return product.CostPrice;

            // purchase_items has no doc_date; the JOIN to purchases supplies it.
            return SqlHelper.ExecuteScalar<long>(_db,
                @"SELECT pi.unit_price
                  FROM purchase_items pi
                  JOIN purchases p ON p.journal_no = pi.journal_no
                  WHERE pi.product_code = @code
                    AND p.doc_type = 'PURCHASE'
                    AND p.control != 3
                    AND pi.unit_price > 0
                  ORDER BY p.doc_date DESC, p.id DESC, pi.id DESC
                  LIMIT 1",
                SqlHelper.Param("@code", productCode));
        }

        // Perpetual moving average: every stock-in at a known cost re-weights
        // products.cost_price; stock-outs (sales, purchase returns) never change it.
        // on_hand and qty must be in the same unit (the local ledger's) - the average is a
        // pure ratio, so it is unaffected by the x100 qty scale as long as it is not mixed.
        // qty is a LEDGER qty (x100, StockQty): legacy GHIST/GSMRY rows and dashboard requests
        // are x100, so callers holding a plain unit count pass StockQty.ToLedger(units)
        // (WP-02). The qty itself is stored as given; only the money value divides the
        // scale back out (val_in = unit cost x whole units).
        // cost_price is only written when CostEngineOwnsCostPriceKey is "true".
        // placement: null = a normal POS row (next id below the dashboard range, stamped
        // now); Kasir.CloudSync PullService passes the reserved id and the time the
        // dashboard event happened (WP-04).
        public void RecordStockIn(string productCode, int qty, long unitCost,
            string movementType, string journalNo, string docDate, int changedBy,
            MovementPlacement placement = null)
        {
            int onHandBefore = _movementRepo.GetStockOnHand(productCode);

            var movement = new StockMovement
            {
                ProductCode = productCode,
                JournalNo = journalNo,
                MovementType = movementType,
                DocDate = docDate,
                PeriodCode = docDate.Length >= 7 ? docDate.Substring(0, 4) + docDate.Substring(5, 2) : "",
                LocationCode = DefaultLocationCode,
                QtyIn = qty,
                QtyOut = 0,
                ValIn = StockQty.Value(unitCost, qty),
                ValOut = 0,
                CostPrice = unitCost,
                ChangedBy = changedBy
            };

            InsertMovement(movement, placement);

            UpdatePerpetualAverage(productCode, onHandBefore, qty, unitCost, movementType);
        }

        private bool CostEngineOwnsCostPrice()
        {
            return string.Equals(_configRepo.Get(CostEngineOwnsCostPriceKey), "true",
                StringComparison.OrdinalIgnoreCase);
        }

        private void UpdatePerpetualAverage(string productCode, int onHandBefore, int qty, long unitCost,
            string movementType)
        {
            // Pre-cutover: FoxPro AVGCOST owns cost_price; the movement is still recorded.
            if (!CostEngineOwnsCostPrice()) return;

            // A zero-cost stock-in (e.g. voiding a sale whose COGS was 0) carries no price
            // information; don't let it wipe or dilute the average.
            if (qty <= 0 || unitCost <= 0) return;

            var product = _productRepo.GetByCode(productCode);
            if (product == null) return;

            long newAvg;
            if (product.CostPrice <= 0)
            {
                // No known cost: any cost basis beats 0.
                newAvg = unitCost;
            }
            else if (onHandBefore <= 0)
            {
                // Nothing on hand: only a purchase sets a fresh cost. A RETURN_IN (sale void)
                // or OPNAME surplus carries an older cost and must not overwrite a newer one.
                if (movementType != "PURCHASE") return;
                newAvg = unitCost;
            }
            else
            {
                decimal total = (decimal)onHandBefore * product.CostPrice + (decimal)qty * unitCost;
                newAvg = (long)Math.Round(total / (onHandBefore + qty), MidpointRounding.AwayFromZero);
            }

            if (newAvg != product.CostPrice)
            {
                _productRepo.UpdateCostPrice(productCode, newAvg);
            }
        }

        // qty is a LEDGER qty (x100, StockQty) - see RecordStockIn (also for placement).
        public void RecordStockOut(string productCode, int qty, long costPrice,
            string movementType, string journalNo, string docDate, int changedBy,
            MovementPlacement placement = null)
        {
            var movement = new StockMovement
            {
                ProductCode = productCode,
                JournalNo = journalNo,
                MovementType = movementType,
                DocDate = docDate,
                PeriodCode = docDate.Length >= 7 ? docDate.Substring(0, 4) + docDate.Substring(5, 2) : "",
                LocationCode = DefaultLocationCode,
                QtyIn = 0,
                QtyOut = qty,
                ValIn = 0,
                ValOut = StockQty.Value(costPrice, qty),
                CostPrice = costPrice,
                ChangedBy = changedBy
            };

            InsertMovement(movement, placement);
        }

        private void InsertMovement(StockMovement movement, MovementPlacement placement)
        {
            if (placement == null)
                _movementRepo.Insert(movement);
            else
                _movementRepo.InsertWithId(movement, placement.Id, placement.MovedAt);
        }

        // physicalQty and the result quantities are ledger qty (x100).
        public StockVariance CalculateVariance(string productCode, int physicalQty)
        {
            int systemQty = GetStockOnHand(productCode);
            long avgCost = CalculateAverageCost(productCode);
            int variance = physicalQty - systemQty;

            return new StockVariance
            {
                ProductCode = productCode,
                SystemQty = systemQty,
                PhysicalQty = physicalQty,
                Variance = variance,
                VarianceCost = StockQty.Value(avgCost, Math.Abs(variance))
            };
        }
    }

    // Where a movement goes in the ledger when the caller (not SQLite) picks it:
    // Id in the dashboard range (StockMovementRepository.DashboardIdFloor and up) and
    // MovedAt = local 'yyyy-MM-dd HH:mm:ss' written to created_at and changed_at.
    public sealed class MovementPlacement
    {
        public long Id { get; set; }
        public string MovedAt { get; set; }
    }

    public class StockVariance
    {
        public string ProductCode { get; set; }
        public int SystemQty { get; set; }
        public int PhysicalQty { get; set; }
        public int Variance { get; set; }
        public long VarianceCost { get; set; }
    }
}
