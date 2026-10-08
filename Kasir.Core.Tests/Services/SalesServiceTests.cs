using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class SalesServiceTests
    {
        private SqliteConnection _db;
        private SalesService _service;
        private FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new System.DateTime(2026, 4, 4, 14, 30, 0));
            _service = new SalesService(_db, _clock);
            _service.SetCashier("ADM", 1);

            SeedTestData();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void SeedTestData()
        {
            // Seed config
            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");

            // Seed products
            var productRepo = new ProductRepository(_db);
            productRepo.Insert(new Product
            {
                ProductCode = "P001",
                Name = "MINYAK GORENG 2L",
                Price = 3200000,
                Price2 = 2800000,
                QtyBreak2 = 12,
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });

            productRepo.Insert(new Product
            {
                ProductCode = "P002",
                Name = "SABUN CUCI 800G",
                Price = 1550000,
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });

            productRepo.Insert(new Product
            {
                ProductCode = "P003",
                Name = "GULA PASIR 1KG",
                Price = 1800000,
                DiscPct = 500, // 5% product discount
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });
        }

        [Test]
        public void AddItem_ValidProduct_ReturnsItem()
        {
            var item = _service.AddItem("P001", 1);

            item.Should().NotBeNull();
            item.ProductCode.Should().Be("P001");
            item.ProductName.Should().Be("MINYAK GORENG 2L");
            item.UnitPrice.Should().Be(3200000);
            item.Quantity.Should().Be(1);
            item.Value.Should().Be(3200000);
        }

        [Test]
        public void AddItem_NonExistentProduct_ReturnsNull()
        {
            var item = _service.AddItem("XXXX", 1);
            item.Should().BeNull();
        }

        [Test]
        public void AddItem_MultipleItems_TracksAll()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 2);

            _service.CurrentItems.Count.Should().Be(2);
        }

        [Test]
        public void AddItem_QtyBreakTier_UsesPrice2()
        {
            var item = _service.AddItem("P001", 12); // qty >= 12 → price2

            item.UnitPrice.Should().Be(2800000);
            item.Quantity.Should().Be(12);
        }

        [Test]
        public void AddItem_WithProductDiscount_AppliesDiscount()
        {
            var item = _service.AddItem("P003", 1); // 5% discount

            item.DiscPct.Should().Be(500);
            // 1800000 * 5% = 90000
            item.DiscValue.Should().Be(90000);
            item.Value.Should().Be(1710000); // 1800000 - 90000
        }

        [Test]
        public void AddItem_WithDiscountsTableRule_AppliesTableDiscount()
        {
            // Insert a 10% discount rule for P001 valid for 2026
            Kasir.Data.SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO discounts (product_code, dept_code, disc_pct, date_start, date_end,
                  is_active, priority) VALUES (@code, '', @pct, @start, @end, 1, 10)",
                Kasir.Data.SqlHelper.Param("@code", "P001"),
                Kasir.Data.SqlHelper.Param("@pct", 1000),
                Kasir.Data.SqlHelper.Param("@start", "2026-01-01"),
                Kasir.Data.SqlHelper.Param("@end", "2026-12-31"));

            var item = _service.AddItem("P001", 1);

            item.DiscPct.Should().Be(1000); // 10%
            // 3200000 * 10% = 320000
            item.DiscValue.Should().Be(320000);
            item.Value.Should().Be(2880000); // 3200000 - 320000
        }

        [Test]
        public void AddItem_WithExpiredDiscount_NoDiscount()
        {
            // Insert an expired discount rule for P001
            Kasir.Data.SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO discounts (product_code, dept_code, disc_pct, date_start, date_end,
                  is_active, priority) VALUES (@code, '', @pct, @start, @end, 1, 10)",
                Kasir.Data.SqlHelper.Param("@code", "P001"),
                Kasir.Data.SqlHelper.Param("@pct", 2000),
                Kasir.Data.SqlHelper.Param("@start", "2025-01-01"),
                Kasir.Data.SqlHelper.Param("@end", "2025-12-31"));

            var item = _service.AddItem("P001", 1);

            item.DiscPct.Should().Be(0); // No discount — expired
            item.DiscValue.Should().Be(0);
            item.Value.Should().Be(3200000);
        }

        [Test]
        public void GetTotals_SingleItem_CorrectTotals()
        {
            _service.AddItem("P001", 1);

            var totals = _service.GetTotals();

            totals.GrossAmount.Should().Be(3200000);
            totals.TotalDiscount.Should().Be(0);
            totals.NetAmount.Should().Be(3200000);
            totals.ItemCount.Should().Be(1);
            totals.LineCount.Should().Be(1);
        }

        [Test]
        public void GetTotals_MultipleItems_SumsCorrectly()
        {
            _service.AddItem("P001", 2); // 2 × 3200000 = 6400000
            _service.AddItem("P002", 1); // 1 × 1550000 = 1550000

            var totals = _service.GetTotals();

            totals.GrossAmount.Should().Be(6400000 + 1550000);
            totals.NetAmount.Should().Be(7950000);
            totals.LineCount.Should().Be(2);
        }

        [Test]
        public void RemoveItem_RemovesFromList()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            _service.RemoveItem(0);

            _service.CurrentItems.Count.Should().Be(1);
            _service.CurrentItems[0].ProductCode.Should().Be("P002");
        }

        [Test]
        public void UpdateItemQty_RecalculatesValue()
        {
            _service.AddItem("P001", 1);

            _service.UpdateItemQty(0, 3);

            _service.CurrentItems[0].Quantity.Should().Be(3);
            _service.CurrentItems[0].Value.Should().Be(3200000L * 3);
        }

        [Test]
        public void CompleteSale_PersistsAndReturnsSale()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            var sale = _service.CompleteSale(
                cashAmount: 5000000,
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "");

            sale.Should().NotBeNull();
            sale.JournalNo.Should().NotBeNullOrEmpty();
            sale.TotalValue.Should().Be(4750000); // 3200000 + 1550000
            sale.CashAmount.Should().Be(5000000);
            sale.ChangeAmount.Should().Be(250000); // 5000000 - 4750000
            sale.DocDate.Should().Be("2026-04-04");
        }

        // F41: CompleteSale must bind the sale to the actual open shift, not the "1"
        // no-shift fallback that every other test exercised.
        [Test]
        public void CompleteSale_BindsToOpenShift_NotFallback()
        {
            new ShiftRepository(_db).OpenShift(new Shift
            {
                RegisterId = "01",
                ShiftNumber = "3",
                CashierId = 1,
                OpenedAt = "2026-04-04 08:00:00",
                OpeningCash = 0,
                Status = "O"
            });

            _service.AddItem("P001", 1);
            var sale = _service.CompleteSale(5000000, 0, 0, "", "", "");

            sale.Shift.Should().Be("3", "the sale must carry the open shift's number, not the fallback '1'");
        }

        // F36: an in-progress cart must survive a crash — a new SalesService on the same DB
        // recovers the persisted draft instead of losing it.
        [Test]
        public void PendingCart_IsRecovered_ByNewSalesService()
        {
            _service.AddItem("P001", 2);
            _service.AddItem("P002", 1);

            // Simulate a crash + restart: brand-new service on the same database.
            var recovered = new SalesService(_db, _clock);
            recovered.SetCashier("ADM", 1);
            int count = recovered.RecoverPendingSale();

            count.Should().Be(2, "the two draft lines must be recovered");
            recovered.CurrentItems.Should().HaveCount(2);
            recovered.CurrentItems[0].ProductCode.Should().Be("P001");
            recovered.CurrentItems[0].ProductName.Should().Be("MINYAK GORENG 2L", "product name is re-looked-up");
        }

        // F36: completing the sale clears the persisted draft so it is not recovered later.
        [Test]
        public void CompleteSale_ClearsPendingDraft()
        {
            _service.AddItem("P001", 1);
            _service.CompleteSale(5000000, 0, 0, "", "", "");

            var after = new SalesService(_db, _clock);
            after.SetCashier("ADM", 1);
            after.RecoverPendingSale().Should().Be(0, "a completed sale leaves no draft to recover");
        }

        // F35: voiding a sale must return the sold stock to inventory — a plain control=3
        // flip left inventory permanently understated.
        [Test]
        public void VoidSale_ReturnsSoldStockToInventory()
        {
            var movementRepo = new StockMovementRepository(_db);
            _service.AddItem("P001", 2);
            var sale = _service.CompleteSale(10000000, 0, 0, "", "", "");

            int afterSale = movementRepo.GetStockOnHand("P001"); // reduced by 2
            _service.VoidSale(sale.JournalNo);
            int afterVoid = movementRepo.GetStockOnHand("P001");

            afterVoid.Should().Be(afterSale + 2, "voiding returns the 2 sold units to stock");

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT control FROM sales WHERE journal_no = @j";
            cmd.Parameters.AddWithValue("@j", sale.JournalNo);
            System.Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(3, "sale is marked void");
        }

        // The void's stock-in must be valued at the unit cost, not the line COGS total,
        // or stock value is inflated by (qty - 1) x the line cost.
        [Test]
        public void VoidSale_RestoresStockValue_AtUnitCost()
        {
            new InventoryService(_db).RecordStockIn("P001", 10, 1000, "PURCHASE", "BPB-X", "2026-04-01", 1);
            _service.AddItem("P001", 3);
            var sale = _service.CompleteSale(10000000, 0, 0, "", "", "");

            _service.VoidSale(sale.JournalNo);

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT SUM(val_in) - SUM(val_out) FROM stock_movements WHERE product_code = 'P001'";
            System.Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(10000, "sale + void net to zero value");
        }

        // F13: a sale whose GL journal is already posted must not be silently voided.
        [Test]
        public void VoidSale_PostedSale_Throws()
        {
            _service.AddItem("P001", 1);
            var sale = _service.CompleteSale(5000000, 0, 0, "", "", "");

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE sales SET is_posted = 'Y' WHERE journal_no = @j";
                cmd.Parameters.AddWithValue("@j", sale.JournalNo);
                cmd.ExecuteNonQuery();
            }

            System.Action act = () => _service.VoidSale(sale.JournalNo);
            act.Should().Throw<System.InvalidOperationException>().WithMessage("*diposting*");
        }

        // F20/F40: the stored line COGS must use the weighted-average cost from the stock
        // ledger, not the master CostPrice, so GL COGS matches the inventory ledger.
        [Test]
        public void CompleteSale_UsesWeightedAverageCostForCogs_NotMasterCostPrice()
        {
            // P001 master CostPrice is 0 in the seed; establish a weighted-average of 1000.
            new InventoryService(_db).RecordStockIn("P001", 10, 1000, "PURCHASE", "BPB-X", "2026-04-01", 1);

            _service.AddItem("P001", 2);
            var sale = _service.CompleteSale(10000000, 0, 0, "", "", "");

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT cogs FROM sale_items WHERE journal_no = @j AND product_code = 'P001'";
            cmd.Parameters.AddWithValue("@j", sale.JournalNo);
            System.Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(2000,
                "COGS = weighted-average (1000) × qty (2), not master CostPrice (0)");
        }

        [Test]
        public void CompleteSale_InsufficientPayment_Throws()
        {
            _service.AddItem("P001", 1); // 3200000

            System.Action act = () => _service.CompleteSale(
                cashAmount: 1000000, // not enough
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "");

            act.Should().Throw<System.InvalidOperationException>();
        }

        [Test]
        public void CompleteSale_SplitPayment_Works()
        {
            _service.AddItem("P001", 1); // 3200000

            var sale = _service.CompleteSale(
                cashAmount: 2000000,
                cardAmount: 1200000,
                voucherAmount: 0,
                cardCode: "VISA",
                cardType: "C",
                memberCode: "");

            sale.CashAmount.Should().Be(2000000);
            sale.NonCash.Should().Be(1200000);
            sale.ChangeAmount.Should().Be(0);
        }

        // #19: a card tender must name the card (legacy JUAL1: "Jenis card harus di-isi").
        // Without it the sale has no card code and a credit-card amount is posted to the
        // debit/QRIS clearing account.
        [Test]
        public void CompleteSale_CardAmountWithoutCard_Throws()
        {
            _service.AddItem("P001", 1); // 3200000

            System.Action act = () => _service.CompleteSale(
                cashAmount: 2000000,
                cardAmount: 1200000,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "");

            act.Should().Throw<System.InvalidOperationException>().WithMessage("*kartu*");
        }

        // #19: a card picked in the dropdown but with no card amount is a cash sale; it
        // must not be stored with a card code/type.
        [Test]
        public void CompleteSale_CardSelectedButZeroCardAmount_DoesNotStoreCard()
        {
            _service.AddItem("P001", 1); // 3200000

            var sale = _service.CompleteSale(
                cashAmount: 3200000,
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "VISA",
                cardType: "C",
                memberCode: "");

            sale.CardCode.Should().BeEmpty();
            sale.CardType.Should().BeEmpty();
            var saved = new SaleRepository(_db).GetByJournalNo(sale.JournalNo);
            saved.CardCode.Should().BeEmpty();
            saved.CardType.Should().BeEmpty();
        }

        [Test]
        public void CompleteSale_NegativeCardTender_Throws()
        {
            _service.AddItem("P001", 1); // 3200000

            System.Action act = () => _service.CompleteSale(
                cashAmount: 4200000,
                cardAmount: -1000000,
                voucherAmount: 0,
                cardCode: "VISA",
                cardType: "C",
                memberCode: "");

            act.Should().Throw<System.InvalidOperationException>();
        }

        // #19 owner decision: stickers are for members only (legacy JUAL1 a_total gives 0
        // when no member card is entered). New-POS sales without a member were getting points.
        [Test]
        public void CompleteSale_WithoutMember_GivesNoLoyaltyPoints()
        {
            _service.AddItem("P001", 1); // 3200000 = Rp 32,000

            var sale = _service.CompleteSale(3200000, 0, 0, "", "", "");

            sale.PointValue.Should().Be(0, "stickers are only for members");
            new SaleRepository(_db).GetByJournalNo(sale.JournalNo).PointValue.Should().Be(0);
        }

        // #19 owner decision: no card surcharge (PBI 23/6/PBI/2021). A card with a fee in
        // the master must not change what the customer pays or what is recorded.
        [Test]
        public void CompleteSale_CardWithFee_DoesNotSurchargeCustomer()
        {
            new CreditCardRepository(_db).Insert(new CreditCard
            {
                CardCode = "BCA", Name = "BCA KREDIT", FeePct = 250, CardType = "C"
            });
            _service.AddItem("P001", 1); // 3200000

            var sale = _service.CompleteSale(0, 3200000, 0, "BCA", "C", "");

            sale.TotalValue.Should().Be(3200000);
            sale.NonCash.Should().Be(3200000);
            sale.PaymentAmount.Should().Be(3200000);
            sale.ChangeAmount.Should().Be(0);
        }

        [Test]
        public void CompleteSale_WithMember_CalculatesLoyaltyPoints()
        {
            _service.AddItem("P001", 1); // 3200000 = Rp 32,000 → 3 stickers

            var sale = _service.CompleteSale(
                cashAmount: 3200000,
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "MBR001");

            sale.PointValue.Should().Be(3); // 3200000 / 1000000 = 3
            sale.MemberCode.Should().Be("MBR001");
        }

        [Test]
        public void VoidSale_SetsControl3()
        {
            _service.AddItem("P001", 1);
            var sale = _service.CompleteSale(3200000, 0, 0, "", "", "");

            _service.VoidSale(sale.JournalNo);

            var voided = new SaleRepository(_db).GetByJournalNo(sale.JournalNo);
            voided.Control.Should().Be(3);
        }

        [Test]
        public void ClearCurrentSale_EmptiesItemList()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            _service.ClearCurrentSale();

            _service.CurrentItems.Count.Should().Be(0);
        }

        [Test]
        public void CompleteSale_GeneratesUniqueJournalNos()
        {
            _service.AddItem("P001", 1);
            var sale1 = _service.CompleteSale(3200000, 0, 0, "", "", "");
            _service.ClearCurrentSale();

            _service.AddItem("P002", 1);
            var sale2 = _service.CompleteSale(1550000, 0, 0, "", "", "");

            sale1.JournalNo.Should().NotBe(sale2.JournalNo);
        }

        // A scan that fails to save the draft cart (e.g. SqliteException: database is locked)
        // must not leave a line in memory that the screen never showed but CompleteSale charges.
        private void BreakCartPersistence()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE pending_sales";
                cmd.ExecuteNonQuery();
            }
        }

        [Test]
        public void AddItem_WhenCartPersistFails_ThrowsAndLeavesCartUnchanged()
        {
            _service.AddItem("P001", 1);
            BreakCartPersistence();

            System.Action act = () => _service.AddItem("P002", 1);

            act.Should().Throw<SqliteException>();
            _service.CurrentItems.Select(i => i.ProductCode).Should().Equal("P001");
        }

        [Test]
        public void AddMiscItem_WhenCartPersistFails_ThrowsAndLeavesCartUnchanged()
        {
            _service.AddItem("P001", 1);
            BreakCartPersistence();

            System.Action act = () => _service.AddMiscItem(1, 500000);

            act.Should().Throw<SqliteException>();
            _service.CurrentItems.Select(i => i.ProductCode).Should().Equal("P001");
        }

        // ---- Stock integrity PR-K1: non-stock guard + location default ----

        private void SeedProduct(string code, string name, string status = "A", long price = 1000000)
        {
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = code,
                Name = name,
                Price = price,
                Status = status,
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });
        }

        private long CountMovements(string productCode)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE product_code = @p";
            cmd.Parameters.AddWithValue("@p", productCode);
            return System.Convert.ToInt64(cmd.ExecuteScalar());
        }

        private long SaleItemCogs(string journalNo, string productCode)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT cogs FROM sale_items WHERE journal_no = @j AND product_code = @p";
            cmd.Parameters.AddWithValue("@j", journalNo);
            cmd.Parameters.AddWithValue("@p", productCode);
            return System.Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Test]
        public void CompleteSale_MiscItem_NoStockMovement()
        {
            SeedProduct(SalesService.MiscProductCode, SalesService.MiscProductName);
            _service.AddMiscItem(2, 500000);

            var sale = _service.CompleteSale(1000000, 0, 0, "", "", "");

            CountMovements("1").Should().Be(0, "code \"1\" is non-stock: no stock_movements row");
            SaleItemCogs(sale.JournalNo, "1").Should().Be(0, "misc line COGS is still stored (0 until PR-K4 estimates it)");
        }

        // The line COGS set when the non-stock item was added (PR-K4 will estimate it) must
        // survive CompleteSale; it must not be overwritten by a stock-ledger average.
        [Test]
        public void CompleteSale_MiscItem_KeepsLineCogs()
        {
            SeedProduct(SalesService.MiscProductCode, SalesService.MiscProductName);
            var item = _service.AddMiscItem(2, 500000);
            item.Cogs = 750000; // estimated COGS (75% of value)

            var sale = _service.CompleteSale(1000000, 0, 0, "", "", "");

            SaleItemCogs(sale.JournalNo, "1").Should().Be(750000);
        }

        [TestCase("AL")]
        [TestCase("AT")]
        [TestCase("PR")]
        [TestCase("PL")]
        [TestCase("MY")]
        [TestCase("LL")]
        public void CompleteSale_CategoryKeyItem_NoStockMovement(string code)
        {
            SeedProduct(code, "KATEGORI " + code);
            _service.AddItem(code, 1);

            _service.CompleteSale(1000000, 0, 0, "", "", "");

            CountMovements(code).Should().Be(0, "category key codes are non-stock");
        }

        [Test]
        public void CategoryKeyCodes_ContainsExactlyTheSixCategoryKeys()
        {
            SalesService.CategoryKeyCodes.Should().BeEquivalentTo(new[] { "AL", "AT", "PR", "PL", "MY", "LL" });
        }

        [Test]
        public void CompleteSale_RegularItem_CreatesStockMovement()
        {
            _service.AddItem("P001", 2);

            var sale = _service.CompleteSale(10000000, 0, 0, "", "", "");

            var movements = new StockMovementRepository(_db).GetByJournal(sale.JournalNo);
            movements.Should().ContainSingle();
            movements[0].ProductCode.Should().Be("P001");
            movements[0].MovementType.Should().Be("SALE");
            movements[0].QtyOut.Should().Be(2);
            movements[0].LocationCode.Should().Be("T");
        }

        // Critic #4: pending_sales stores only product_code (no non-stock flag), so the
        // guard must derive non-stock-ness from the code after a crash recovery.
        [Test]
        public void CompleteSale_CrashRecovery_MiscItem_NoStockMovement()
        {
            SeedProduct(SalesService.MiscProductCode, SalesService.MiscProductName);
            _service.AddMiscItem(1, 500000);
            _service.AddItem("P001", 1);

            // Simulate crash + restart: a brand-new service recovers the persisted cart.
            var recovered = new SalesService(_db, _clock);
            recovered.SetCashier("ADM", 1);
            recovered.RecoverPendingSale().Should().Be(2);

            var sale = recovered.CompleteSale(10000000, 0, 0, "", "", "");

            CountMovements("1").Should().Be(0, "recovered code \"1\" line must still skip stock-out");
            var movements = new StockMovementRepository(_db).GetByJournal(sale.JournalNo);
            movements.Select(m => m.ProductCode).Should().Equal("P001");
        }

        [Test]
        public void VoidSale_MiscItem_NoStockMovement()
        {
            SeedProduct(SalesService.MiscProductCode, SalesService.MiscProductName);
            _service.AddMiscItem(1, 500000);
            _service.AddItem("P001", 1);
            var sale = _service.CompleteSale(10000000, 0, 0, "", "", "");

            _service.VoidSale(sale.JournalNo);

            CountMovements("1").Should().Be(0, "voiding a non-stock line must not create a RETURN_IN");
            CountMovements("P001").Should().Be(2, "SALE + RETURN_IN for the stock item");
        }

        // EC1 / owner decision "selling never blocks": on-hand 0, on-hand negative and an
        // inactive (status 'I') product all sell without exception.
        [Test]
        public void Sale_ZeroAndNegativeStock_Succeeds()
        {
            var inventory = new InventoryService(_db);
            inventory.RecordStockOut("P002", 5, 0, "SALE", "KLR-OLD", "2026-04-01", 1);
            inventory.GetStockOnHand("P001").Should().Be(0);
            inventory.GetStockOnHand("P002").Should().Be(-5);
            SeedProduct("P009", "BARANG NONAKTIF", status: "I");

            _service.AddItem("P001", 1).Should().NotBeNull("on-hand 0 must still sell");
            _service.AddItem("P002", 1).Should().NotBeNull("negative on-hand must still sell");
            _service.AddItem("P009", 1).Should().NotBeNull("inactive product must still sell by code");

            Sale sale = null;
            System.Action act = () => sale = _service.CompleteSale(100000000, 0, 0, "", "", "");

            act.Should().NotThrow();
            sale.Should().NotBeNull();
            inventory.GetStockOnHand("P001").Should().Be(-1);
            inventory.GetStockOnHand("P002").Should().Be(-6);
            inventory.GetStockOnHand("P009").Should().Be(-1);
        }
    }
}
