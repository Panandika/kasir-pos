using System;
using System.Collections.Generic;
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
    // GL impact of stock documents:
    //   BPB (goods receipt)      Dr Inventory / Cr GRNI  (goods received, not yet billed)
    //   MSK billing a BPB        Dr GRNI (at BPB value) + Dr/Cr Inventory (difference) / Cr AP
    //   MSK combined / vs PO     Dr Inventory / Cr AP
    //   Stock out / opname       Dr/Cr stock-adjustment account vs Inventory, at movement cost
    [TestFixture]
    public class StockGlPostingTests
    {
        private const string Period = "202604";
        private SqliteConnection _db;
        private PostingService _posting;
        private PurchasingService _purchasing;
        private StockOpnameService _opname;
        private GlDetailRepository _glRepo;
        private ConfigRepository _configRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            var clock = new FakeClock(new DateTime(2026, 4, 4, 10, 0, 0));
            _posting = new PostingService(_db);
            _purchasing = new PurchasingService(_db, clock);
            _opname = new StockOpnameService(_db, clock);
            _glRepo = new GlDetailRepository(_db);

            _configRepo = new ConfigRepository(_db);
            _configRepo.Set("register_id", "01");
            _configRepo.Set("ACCOUNT_INVENTORY", "1300");
            _configRepo.Set("ACCOUNT_PAYABLES", "2100");
            _configRepo.Set("ACCOUNT_GRNI", "2150");
            _configRepo.Set("ACCOUNT_STOCK_ADJUSTMENT", "5900");
            _configRepo.Set("ACCOUNT_PRICE_VARIANCE", "5910");
            _configRepo.Set("ACCOUNT_PURCHASE_DISCOUNT", "4910");
            _configRepo.Set("ACCOUNT_VAT_IN", "1410");

            var accounts = new AccountRepository(_db);
            accounts.Insert(new Account { AccountCode = "1300", AccountName = "Persediaan", AccountGroup = 1, NormalBalance = "D", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "2100", AccountName = "Hutang Dagang", AccountGroup = 2, NormalBalance = "K", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "2150", AccountName = "Barang Diterima Belum Ditagih", AccountGroup = 2, NormalBalance = "K", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "5900", AccountName = "Selisih Persediaan", AccountGroup = 5, NormalBalance = "D", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "5910", AccountName = "Selisih Harga Pembelian", AccountGroup = 5, NormalBalance = "D", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "4910", AccountName = "Potongan Pembelian", AccountGroup = 4, NormalBalance = "K", IsDetail = 1 });
            accounts.Insert(new Account { AccountCode = "1410", AccountName = "PPN Masukan", AccountGroup = 1, NormalBalance = "D", IsDetail = 1 });

            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "PRODUCT 1", Price = 500000, BuyingPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            new SubsidiaryRepository(_db).Insert(new Subsidiary { SubCode = "V001", Name = "VENDOR", GroupCode = "1", Status = "A" });
            new FiscalPeriodRepository(_db).EnsurePeriod(Period, 2026, 4);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private string Receive(int qty, long price)
        {
            return _purchasing.CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = qty, UnitPrice = price } }, 1);
        }

        private string Invoice(int qty, long price, string orderRef = null)
        {
            return _purchasing.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = qty, UnitPrice = price, OrderRef = orderRef } }, 1);
        }

        private long Net(string account) // debit − credit over the period
        {
            return _glRepo.GetDebitTotalForAccount(Period, account) - _glRepo.GetCreditTotalForAccount(Period, account);
        }

        private long StockValue()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(val_in), 0) - COALESCE(SUM(val_out), 0) FROM stock_movements";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private string IsPosted(string table, string journalNo)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT is_posted FROM {table} WHERE journal_no = @j";
            cmd.Parameters.AddWithValue("@j", journalNo);
            return (string)cmd.ExecuteScalar();
        }

        // ---------- Item 4: goods received not invoiced ----------

        [Test]
        public void PostReceipts_DebitsInventory_CreditsGrni()
        {
            string gr = Receive(10, 300000);

            var result = _posting.PostReceipts(Period);

            result.PostedCount.Should().Be(1);
            result.ErrorCount.Should().Be(0);
            Net("1300").Should().Be(3000000);
            Net("2150").Should().Be(-3000000);
            IsPosted("purchases", gr).Should().Be("Y");
        }

        [Test]
        public void ReceiptThenInvoiceAtSamePrice_ClearsGrniToZero()
        {
            string gr = Receive(10, 300000);
            Invoice(10, 300000, gr);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("2150").Should().Be(0, "fully billed receipt leaves nothing in GRNI");
            Net("1300").Should().Be(3000000, "inventory counted once, on receipt");
            Net("2100").Should().Be(-3000000);
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void PartiallyBilledReceipt_LeavesUnbilledValueInGrni()
        {
            string gr = Receive(10, 300000);
            Invoice(6, 300000, gr);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period);

            Net("2150").Should().Be(-1200000, "4 units × Rp 3.000 still received-not-billed");
            Net("2100").Should().Be(-1800000);
        }

        [Test]
        public void InvoicePriceHigherThanReceipt_DifferenceGoesToVarianceAccount()
        {
            string gr = Receive(10, 300000);
            Invoice(10, 310000, gr);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period);

            Net("2150").Should().Be(0);
            Net("2100").Should().Be(-3100000);
            Net("1300").Should().Be(StockValue(), "inventory stays at the BPB (stock) value");
            Net("5910").Should().Be(100000, "10 × Rp 100 price increase is a variance expense");
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void InvoicePriceLowerThanReceipt_CreditsVarianceAccount()
        {
            string gr = Receive(10, 300000);
            Invoice(10, 290000, gr);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period);

            Net("2150").Should().Be(0);
            Net("2100").Should().Be(-2900000);
            Net("1300").Should().Be(StockValue());
            Net("5910").Should().Be(-100000, "price decrease is a variance credit");
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void CombinedInvoice_PostsInventoryAgainstAp_WithoutNeedingGrniAccount()
        {
            _configRepo.Set("ACCOUNT_GRNI", "");
            Invoice(5, 300000);

            var result = _posting.PostPurchases(Period);

            result.ErrorCount.Should().Be(0, "stores that never use BPB must not need a GRNI account");
            Net("1300").Should().Be(1500000);
            Net("2100").Should().Be(-1500000);
        }

        [Test]
        public void PostReceipts_WithoutGrniAccount_FailsClosed_AndLeavesReceiptUnposted()
        {
            _configRepo.Set("ACCOUNT_GRNI", "");
            string gr = Receive(1, 300000);

            var result = _posting.PostReceipts(Period);

            result.ErrorCount.Should().Be(1);
            result.Errors.Single().Should().Contain("ACCOUNT_GRNI");
            IsPosted("purchases", gr).Should().Be("N");
        }

        [Test]
        public void ClosePeriod_BlockedByUnpostedReceipt()
        {
            Receive(1, 300000);

            Action act = () => _posting.ClosePeriod(Period);

            act.Should().Throw<InvalidOperationException>().WithMessage("*receipt*");
        }

        [Test]
        public void ReceiptWithBonusLineAtZero_FullyBilled_ClearsGrniExactly()
        {
            string gr = _purchasing.CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem>
                {
                    new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 100000 },
                    new PurchaseItem { ProductCode = "P001", Quantity = 2, UnitPrice = 0 } // bonus
                }, 1);
            _purchasing.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 12, UnitPrice = 83333, OrderRef = gr } }, 1);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("2150").Should().Be(0);
        }

        [Test]
        public void ReceiptBilledInTwoInvoices_WeightedValue_ClearsGrniExactly()
        {
            string gr = _purchasing.CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem>
                {
                    new PurchaseItem { ProductCode = "P001", Quantity = 2, UnitPrice = 100000 },
                    new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 0 }
                }, 1);
            Invoice(1, 100000, gr);
            Invoice(2, 50000, gr);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("2150").Should().Be(0, "last billing clears the exact remainder; no rounding residue");
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void ZeroTotalInvoiceBillingReceipt_Posts_AndDoesNotBlockClose()
        {
            string gr = Receive(5, 100000);
            Invoice(5, 0, gr);

            _posting.PostReceipts(Period);
            var result = _posting.PostPurchases(Period);

            result.ErrorCount.Should().Be(0);
            Net("2150").Should().Be(0);
            Net("1300").Should().Be(StockValue(), "stock stays at BPB value");
            Net("5910").Should().Be(-500000, "goods turned out free: whole BPB value is a variance credit");
            Net("2100").Should().Be(0);
        }

        [Test]
        public void AllBonusReceiptBilledByZeroInvoice_PostsWithoutJournal_AndDoesNotBlockClose()
        {
            string gr = Receive(3, 0);
            string inv = Invoice(3, 0, gr);

            _posting.PostReceipts(Period).ErrorCount.Should().Be(0);
            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            IsPosted("purchases", inv).Should().Be("Y");
            _glRepo.GetByJournalNo(inv).Should().BeEmpty();
        }

        [Test]
        public void PostReceipts_IsIdempotent()
        {
            Receive(10, 300000);

            _posting.PostReceipts(Period);
            _posting.PostReceipts(Period).PostedCount.Should().Be(0);

            Net("1300").Should().Be(3000000);
        }

        [Test]
        public void MixedInvoice_BpbLineAndNewLine_GlMatchesStock()
        {
            string gr = Receive(10, 300000);
            _purchasing.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem>
                {
                    new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000, OrderRef = gr },
                    new PurchaseItem { ProductCode = "P001", Quantity = 5, UnitPrice = 300000 } // extra, received on the invoice
                }, 1);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period);

            long stockValue;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT SUM(val_in) - SUM(val_out) FROM stock_movements";
                stockValue = Convert.ToInt64(cmd.ExecuteScalar());
            }
            stockValue.Should().Be(4500000);
            Net("1300").Should().Be(stockValue);
            Net("2150").Should().Be(0);
            Net("2100").Should().Be(-4500000);
        }

        // ---------- Price variance, header discount, PPN Masukan (PKP) ----------

        [Test]
        public void CombinedInvoice_WithDiscountAndVat_SplitsToOwnAccounts_InventoryEqualsStock()
        {
            _purchasing.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", TotalDisc = 60000, VatAmount = 323400 },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } }, 1);

            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("1300").Should().Be(3000000);
            Net("1300").Should().Be(StockValue());
            Net("1410").Should().Be(323400, "PPN Masukan");
            Net("4910").Should().Be(-60000, "purchase discount credited");
            Net("2100").Should().Be(-3263400);
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void BpbInvoice_WithPriceChangeDiscountAndVat_AllSeparated()
        {
            string gr = Receive(10, 300000);
            _purchasing.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", TotalDisc = 62000, VatAmount = 334200 },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 310000, OrderRef = gr } }, 1);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("1300").Should().Be(StockValue());
            Net("2150").Should().Be(0);
            Net("5910").Should().Be(100000);
            Net("4910").Should().Be(-62000);
            Net("1410").Should().Be(334200);
            Net("2100").Should().Be(-3372200);
        }

        [Test]
        public void InvoiceWithoutVatOrDiscount_DoesNotNeedThoseAccounts()
        {
            _configRepo.Set("ACCOUNT_VAT_IN", "");
            _configRepo.Set("ACCOUNT_PURCHASE_DISCOUNT", "");
            _configRepo.Set("ACCOUNT_PRICE_VARIANCE", "");
            Invoice(5, 300000);

            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);
        }

        [Test]
        public void InvoiceWithVat_WithoutVatAccount_FailsClosed()
        {
            _configRepo.Set("ACCOUNT_VAT_IN", "");
            string inv = _purchasing.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", VatAmount = 33000 },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 300000 } }, 1);

            var result = _posting.PostPurchases(Period);

            result.ErrorCount.Should().Be(1);
            result.Errors.Single().Should().Contain("ACCOUNT_VAT_IN");
            IsPosted("purchases", inv).Should().Be("N");
        }

        [Test]
        public void LegacyInvoiceWithoutLines_PostsTotalToInventory_AsBefore()
        {
            // Migrated MSK rows carry only a header total, no purchase_items.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO purchases (doc_type, journal_no, doc_date, sub_code, total_value, period_code, legacy_source)
                                    VALUES ('PURCHASE', 'TIRTA1911', '2026-04-01', 'V001', 55699944, '202604', 'SM')";
                cmd.ExecuteNonQuery();
            }

            _posting.PostPurchases(Period).ErrorCount.Should().Be(0);

            Net("1300").Should().Be(55699944);
            Net("2100").Should().Be(-55699944);
        }

        // ---------- Item 3: stock adjustments ----------

        [Test]
        public void StockOut_PostsExpenseAgainstInventory_AtMovementCost()
        {
            Receive(10, 300000);
            string adj = _opname.CreateStockOut("DAMAGE", "TOKO",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 2 } }, 1);

            var result = _posting.PostStockAdjustments(Period);

            result.PostedCount.Should().Be(1);
            Net("5900").Should().Be(600000);
            _glRepo.GetCreditTotalForAccount(Period, "1300").Should().Be(600000);
            IsPosted("stock_adjustments", adj).Should().Be("Y");
        }

        [Test]
        public void OpnameShortageAndSurplus_PostNetOfBothDirections()
        {
            Receive(10, 300000);
            _opname.CreateOpnameAdjustment(new List<OpnameLine>
            {
                new OpnameLine { ProductCode = "P001", SystemQty = 10, PhysicalQty = 7 } // shortage 3
            }, 1);

            _posting.PostStockAdjustments(Period).ErrorCount.Should().Be(0);
            Net("5900").Should().Be(900000);

            _opname.CreateOpnameAdjustment(new List<OpnameLine>
            {
                new OpnameLine { ProductCode = "P001", SystemQty = 7, PhysicalQty = 8 } // surplus 1
            }, 1);
            _posting.PostStockAdjustments(Period).ErrorCount.Should().Be(0);

            Net("5900").Should().Be(600000, "surplus of 1 × Rp 3.000 credited back");
            _posting.CheckBalance(Period).IsBalanced.Should().BeTrue();
        }

        [Test]
        public void PostStockAdjustments_IsIdempotent()
        {
            Receive(10, 300000);
            _opname.CreateStockOut("LOSS", "TOKO",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 1 } }, 1);

            _posting.PostStockAdjustments(Period);
            _posting.PostStockAdjustments(Period).PostedCount.Should().Be(0);

            Net("5900").Should().Be(300000);
        }

        [Test]
        public void PostStockAdjustments_WithoutAccount_FailsClosed()
        {
            _configRepo.Set("ACCOUNT_STOCK_ADJUSTMENT", "");
            Receive(10, 300000);
            string adj = _opname.CreateStockOut("USAGE", "TOKO",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 1 } }, 1);

            var result = _posting.PostStockAdjustments(Period);

            result.ErrorCount.Should().Be(1);
            result.Errors.Single().Should().Contain("ACCOUNT_STOCK_ADJUSTMENT");
            IsPosted("stock_adjustments", adj).Should().Be("N");
        }

        [Test]
        public void ClosePeriod_BlockedByUnpostedStockAdjustment()
        {
            Receive(10, 300000);
            _posting.PostReceipts(Period);
            _opname.CreateStockOut("DAMAGE", "TOKO",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 1 } }, 1);

            Action act = () => _posting.ClosePeriod(Period);

            act.Should().Throw<InvalidOperationException>().WithMessage("*stock adjustment*");
        }

        [Test]
        public void AdjustmentWithoutStockMovements_IsMarkedPosted_WithNotice()
        {
            // Legacy (migrated) adjustments have no per-document stock movements.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO stock_adjustments (doc_type, journal_no, doc_date, total_value, period_code, legacy_source)
                                    VALUES ('USAGE', 'OT-LEGACY-1', '2026-04-01', 500000, '202604', 'SM')";
                cmd.ExecuteNonQuery();
            }

            var result = _posting.PostStockAdjustments(Period);

            result.ErrorCount.Should().Be(0);
            result.Notices.Should().ContainSingle(n => n.Contains("OT-LEGACY-1"));
            IsPosted("stock_adjustments", "OT-LEGACY-1").Should().Be("Y");
            Net("5900").Should().Be(0);
        }

        [Test]
        public void InventoryGlMatchesStockValue_AfterReceiptInvoiceAndDamage()
        {
            string gr = Receive(10, 300000);
            Invoice(10, 300000, gr);
            _opname.CreateStockOut("DAMAGE", "TOKO",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 2 } }, 1);

            _posting.PostReceipts(Period);
            _posting.PostPurchases(Period);
            _posting.PostStockAdjustments(Period);

            long stockValue;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT SUM(val_in) - SUM(val_out) FROM stock_movements";
                stockValue = Convert.ToInt64(cmd.ExecuteScalar());
            }
            Net("1300").Should().Be(stockValue);
        }
    }
}
