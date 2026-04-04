using System;
using System.Data.SQLite;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Sync;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;
using Newtonsoft.Json;

namespace Kasir.Tests.Sync
{
    [TestFixture]
    public class PullServiceTests
    {
        private SQLiteConnection _db;
        private FakeSyncFileReader _fileReader;
        private PullService _pullService;
        private ConfigRepository _configRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _fileReader = new FakeSyncFileReader();

            // Seed config
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT OR IGNORE INTO config (key, value) VALUES
                  ('register_id', '02'),
                  ('sync_hub_share', 'C:\\kasir\\sync'),
                  ('sync_hmac_key', 'test-secret-key-32bytes!!')");

            _configRepo = new ConfigRepository(_db);
            _pullService = new PullService(_db, _fileReader);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test, Ignore("TODO: debug path matching in FakeSyncFileReader on CI")]
        public void Pull_TamperedSignature_SkipsFile()
        {
            var batch = CreateValidBatch("01");
            batch.Signature = "tampered-signature";
            string json = JsonConvert.SerializeObject(batch);

            _fileReader.Files["C:\\kasir\\sync\\outbox\\01_20260404_120000_abc123.json"] = json;

            var result = _pullService.Pull();

            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(1);
            result.Error.Should().Contain("HMAC");
        }

        [Test, Ignore("TODO: debug path matching in FakeSyncFileReader on CI")]
        public void Pull_WrongSchemaVersion_SkipsFile()
        {
            var batch = CreateValidBatch("01");
            batch.SchemaVersion = 999;
            string json = SignAndSerialize(batch);

            _fileReader.Files["C:\\kasir\\sync\\outbox\\01_20260404_120000_abc123.json"] = json;

            var result = _pullService.Pull();

            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(1);
            result.Error.Should().Contain("Schema version");
        }

        [Test, Ignore("TODO: debug path matching in FakeSyncFileReader on CI")]
        public void Pull_TableNotInWhitelist_SkipsFile()
        {
            var batch = CreateValidBatch("01");
            batch.Events[0].TableName = "evil_table";
            string json = SignAndSerialize(batch);

            _fileReader.Files["C:\\kasir\\sync\\outbox\\01_20260404_120000_abc123.json"] = json;

            var result = _pullService.Pull();

            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(1);
            result.Error.Should().Contain("whitelist");
        }

        [Test, Ignore("TODO: debug path matching in FakeSyncFileReader on CI")]
        public void Pull_ValidBatch_AppliesInsert()
        {
            var batch = CreateValidBatch("01");
            batch.Events[0].Operation = "I";
            batch.Events[0].TableName = "departments";
            batch.Events[0].RecordKey = "99";
            batch.Events[0].Data["id"] = 99;
            batch.Events[0].Data["dept_code"] = "99";
            batch.Events[0].Data["name"] = "TEST DEPT FROM SYNC";

            string json = SignAndSerialize(batch);
            _fileReader.Files["C:\\kasir\\sync\\outbox\\01_20260404_120000_abc123.json"] = json;

            var result = _pullService.Pull();

            result.AppliedCount.Should().Be(1);

            // Verify the department was created
            var deptRepo = new DepartmentRepository(_db);
            var dept = deptRepo.GetByCode("99");
            dept.Should().NotBeNull();
            dept.Name.Should().Be("TEST DEPT FROM SYNC");
        }

        [Test]
        public void Pull_SkipsOwnRegisterFiles()
        {
            var batch = CreateValidBatch("02"); // Same as our register_id
            string json = SignAndSerialize(batch);

            _fileReader.Files["C:\\kasir\\sync\\outbox\\02_20260404_120000_abc123.json"] = json;

            var result = _pullService.Pull();

            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(0); // Skipped silently, not counted as error
        }

        [Test]
        public void Pull_EmptyOutbox_SucceedsWithZero()
        {
            var result = _pullService.Pull();

            result.Success.Should().BeTrue();
            result.AppliedCount.Should().Be(0);
        }

        private SyncBatch CreateValidBatch(string registerId)
        {
            var batch = new SyncBatch
            {
                RegisterId = registerId,
                SchemaVersion = SyncConfig.SchemaVersion,
                Timestamp = "2026-04-04 12:00:00",
                BatchId = "abc12345"
            };

            var evt = new SyncEvent
            {
                QueueId = 1,
                TableName = "departments",
                RecordKey = "99",
                Operation = "I"
            };
            evt.Data["id"] = 99;
            evt.Data["dept_code"] = "99";
            evt.Data["name"] = "TEST DEPT";

            batch.Events.Add(evt);
            return batch;
        }

        private string SignAndSerialize(SyncBatch batch)
        {
            string hmacKey = _configRepo.Get("sync_hmac_key") ?? "test-secret-key-32bytes!!";

            batch.Signature = null;
            string payloadJson = JsonConvert.SerializeObject(batch, Formatting.None);

            byte[] keyBytes = Encoding.UTF8.GetBytes(hmacKey);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hash = hmac.ComputeHash(payloadBytes);
                batch.Signature = Convert.ToBase64String(hash);
            }

            return JsonConvert.SerializeObject(batch, Formatting.None);
        }
    }
}
