using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class ChartOfAccountsSeedTests
    {
        private SqliteConnection _db;
        private ConfigRepository _config;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _config = new ConfigRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private long Count(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Test]
        public void Seed_LoadsLegacyChart_AndPostingAccounts()
        {
            new Migration_010().Up(_db);

            Count("SELECT COUNT(*) FROM accounts").Should().Be(285);
            var accounts = new AccountRepository(_db);
            accounts.GetByCode("119.002").AccountName.Should().Be("PPN MASUKAN");
            accounts.GetByCode("211.004").AccountName.Should().Be("BARANG DITERIMA BELUM DITAGIH");
            accounts.GetByCode("411.002").NormalBalance.Should().Be("K", "legacy flagged sales accounts D; fixed");
        }

        [Test]
        public void Seed_ConfiguresEveryPostingAccount_ExceptCardAndVoucher()
        {
            new Migration_010().Up(_db);

            _config.Get("ACCOUNT_VAT_IN").Should().Be("119.002");
            _config.Get("ACCOUNT_GRNI").Should().Be("211.004");
            _config.Get("ACCOUNT_PURCHASE_DISCOUNT").Should().Be("710.012");
            _config.Get("ACCOUNT_STOCK_OPNAME").Should().Be("530.004");

            var missing = new AccountingService(_db).GetMissingAccountConfig();
            missing.Should().HaveCount(2);
            missing.Should().OnlyContain(m => m.StartsWith("ACCOUNT_CARD_CLEARING") || m.StartsWith("ACCOUNT_VOUCHER"));
        }

        [Test]
        public void TenderAccounts_AreSeeded_SoNothingIsMissing()
        {
            new Migration_010().Up(_db);
            new Migration_011().Up(_db);

            _config.Get("ACCOUNT_CARD_CLEARING").Should().Be("112.002");
            _config.Get("ACCOUNT_CARD_CLEARING_CREDIT").Should().Be("112.003");
            _config.Get("ACCOUNT_VOUCHER").Should().Be("610.018");
            new AccountingService(_db).GetMissingAccountConfig().Should().BeEmpty();
        }

        [Test]
        public void TenderAccounts_DoNotOverwriteConfiguredValues()
        {
            new Migration_010().Up(_db);
            _config.Set("ACCOUNT_CARD_CLEARING", "111.101");

            new Migration_011().Up(_db);

            _config.Get("ACCOUNT_CARD_CLEARING").Should().Be("111.101");
        }

        [Test]
        public void Seed_IsIdempotent_AndNeverOverwritesConfiguredValues()
        {
            _config.Set("ACCOUNT_INVENTORY", "135");
            _config.Set("ACCOUNT_VAT_IN", "");   // blank counts as unset

            new Migration_010().Up(_db);
            new Migration_010().Up(_db);

            Count("SELECT COUNT(*) FROM accounts").Should().Be(285);
            _config.Get("ACCOUNT_INVENTORY").Should().Be("135", "an existing setting is kept");
            _config.Get("ACCOUNT_VAT_IN").Should().Be("119.002", "a blank setting is filled");
        }

        [Test]
        public void Seed_DoesNotChangeExistingAccounts()
        {
            new AccountRepository(_db).Insert(new Kasir.Models.Account
            {
                AccountCode = "119.002", AccountName = "PPN IN (CUSTOM)", IsDetail = 1, AccountGroup = 1, NormalBalance = "D"
            });

            new Migration_010().Up(_db);

            new AccountRepository(_db).GetByCode("119.002").AccountName.Should().Be("PPN IN (CUSTOM)");
        }
    }
}
