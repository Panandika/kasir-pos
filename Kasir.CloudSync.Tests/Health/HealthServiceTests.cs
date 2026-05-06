using System;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Health;
using Kasir.CloudSync.Tests.TestHelpers;

namespace Kasir.CloudSync.Tests.Health
{
    [TestFixture]
    public class HealthServiceTests
    {
        [Test]
        public void Healthy_With_No_Alerts_When_Outbox_Empty()
        {
            using var db = TestDb.Create();
            var svc = new HealthService(db, () => new DateTime(2026, 4, 25, 10, 0, 0, DateTimeKind.Utc));

            var snap = svc.Snapshot(supabaseDbSizeMb: 50);

            snap.Status.Should().Be("healthy");
            snap.OutboxDepth.Should().Be(0);
            snap.Alerts.Should().BeEmpty();
            snap.Tables.Should().ContainKey("products");
        }

        [Test]
        public void Outbox_Depth_Above_Critical_Adds_CRITICAL_Alert()
        {
            var snap = new HealthSnapshot
            {
                Tables = new System.Collections.Generic.Dictionary<string, TableHealth>(),
                Alerts = new System.Collections.Generic.List<Alert>(),
                OutboxDepth = HealthService.OutboxCriticalDepth + 1
            };

            HealthService.ApplyAlertRules(snap);

            snap.Status.Should().Be("critical");
            snap.Alerts.Should().Contain(a => a.Severity == "CRITICAL" && a.Code == "OUTBOX_FULL");
        }

        [Test]
        public void Db_Size_Above_Warning_Adds_WARNING_Alert()
        {
            var snap = new HealthSnapshot
            {
                Tables = new System.Collections.Generic.Dictionary<string, TableHealth>(),
                Alerts = new System.Collections.Generic.List<Alert>(),
                SupabaseDbSizeMb = HealthService.SupabaseDbWarningMb + 1
            };

            HealthService.ApplyAlertRules(snap);

            snap.Status.Should().Be("degraded");
            snap.Alerts.Should().Contain(a => a.Severity == "WARNING" && a.Code == "DB_GROWING");
        }

        [Test]
        public void Lag_Above_Critical_Adds_CRITICAL_Alert()
        {
            var snap = new HealthSnapshot
            {
                Tables = new System.Collections.Generic.Dictionary<string, TableHealth>
                {
                    { "products", new TableHealth { LagSeconds = HealthService.LagCriticalSeconds + 1 } }
                },
                Alerts = new System.Collections.Generic.List<Alert>()
            };

            HealthService.ApplyAlertRules(snap);

            snap.Status.Should().Be("critical");
            snap.Alerts.Should().Contain(a => a.Code == "LAG_CRITICAL");
        }

        [Test]
        public void RemoteHealthPublisher_Builds_UPSERT_With_OnConflict()
        {
            var sql = RemoteHealthPublisher.BuildUpsertSql();
            sql.Should().Contain("INSERT INTO _sync_health");
            sql.Should().Contain("ON CONFLICT (id) DO UPDATE SET");
            sql.Should().Contain("payload = EXCLUDED.payload");
        }
    }
}
