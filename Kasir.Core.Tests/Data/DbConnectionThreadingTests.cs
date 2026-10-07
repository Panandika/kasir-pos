using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;

namespace Kasir.Tests.Data
{
    // F49: GetConnection() returns one shared SqliteConnection that must only be used on
    // the UI thread. The contract used to be a Debug.Assert, compiled out of Release, so a
    // background poller silently shared the UI connection (SqliteConnection is not
    // thread-safe; a poller query can land inside the UI's open sale transaction).
    [TestFixture]
    [NonParallelizable]
    public class DbConnectionThreadingTests
    {
        private static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        [SetUp]
        public void SetUp()
        {
            ResetDbConnection();
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
            DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
            DbConnection.InitializeDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            ResetDbConnection();
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        }

        private static void ResetDbConnection()
        {
            DbConnection.CloseConnection();
            SqliteConnection.ClearAllPools();
            typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))
                .SetValue(null, false);
            typeof(DbConnection).GetField("_uiThreadId", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, 0);
            DbConnection.FirstRunHandler = null;
        }

        private static Exception RunOnOtherThread(Action action)
        {
            Exception caught = null;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { caught = ex; }
            });
            t.Start();
            t.Join();
            return caught;
        }

        [Test]
        public void GetConnection_FromBackgroundThread_ThrowsInRelease()
        {
            DbConnection.GetConnection(); // binds the owning (UI) thread

            Exception ex = RunOnOtherThread(() => DbConnection.GetConnection());

            ex.Should().BeOfType<InvalidOperationException>(
                "off-thread use of the shared connection must fail loudly in every build");
        }

        [Test]
        public void CreateConnection_FromBackgroundThread_IsAllowed()
        {
            DbConnection.GetConnection();

            Exception ex = RunOnOtherThread(() =>
            {
                using (var conn = DbConnection.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    cmd.ExecuteScalar();
                }
            });

            ex.Should().BeNull();
        }

        [Test]
        public void GetConnection_AfterCloseConnection_ReopensOnOwnerThread()
        {
            var first = DbConnection.GetConnection();
            DbConnection.CloseConnection();

            var second = DbConnection.GetConnection();

            second.Should().NotBeSameAs(first);
            second.State.Should().Be(System.Data.ConnectionState.Open);
        }
    }
}
