using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Kasir.Avalonia.Infrastructure;
using Kasir.Data;
using Kasir.Hardware;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Infrastructure;

// Footer status pollers run on System.Threading.Timer threads.
// F31: the printer poll must not send print data. F09: the cloud queue poll must not
// use the UI thread's shared connection (DbConnection.GetConnection()).
[TestFixture]
[NonParallelizable]
public class StatusPollerTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private sealed class RecordingRawPrinter : IRawPrinter
    {
        private int _sendCalls;
        private int _probeCalls;
        public int SendCalls => Volatile.Read(ref _sendCalls);
        public int ProbeCalls => Volatile.Read(ref _probeCalls);
        public string? LastError => null;

        public bool Send(byte[] data)
        {
            Interlocked.Increment(ref _sendCalls);
            return true;
        }

        public bool IsReachable()
        {
            Interlocked.Increment(ref _probeCalls);
            return true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        PrinterStatusModel.Current.Stop();
        ResetDbConnection();
    }

    private static void ResetDbConnection()
    {
        DbConnection.CloseConnection();
        SqliteConnection.ClearAllPools();
        typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))!
            .SetValue(null, false);
        typeof(DbConnection).GetField("_uiThreadId", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, 0);
        DbConnection.FirstRunHandler = null;
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);
    }

    [Test]
    public void PrinterPoll_SendsNoPrintData()
    {
        var raw = new RecordingRawPrinter();

        PrinterStatusModel.Current.Start(() => new ReceiptPrinter(raw));
        WaitUntil(() => raw.SendCalls > 0 || raw.ProbeCalls > 0);
        PrinterStatusModel.Current.Stop();

        Assert.That(raw.SendCalls, Is.EqualTo(0), "polling must not send bytes to the printer");
        Assert.That(raw.ProbeCalls, Is.GreaterThan(0), "the poll should have probed the printer");
    }

    [Test]
    public void CloudQueuePoll_OnTimerThread_ReadsDepthWithoutSharedConnection()
    {
        ResetDbConnection();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        DbConnection.GetConnection(); // the UI thread owns the shared connection

        long expected;
        using (var conn = DbConnection.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO sync_queue (register_id, table_name, record_key, operation)
                                VALUES ('01','products','A','I'), ('01','products','B','U'), ('01','products','C','D');
                                SELECT COUNT(*) FROM sync_queue WHERE cloud_synced=0;";
            expected = (long)cmd.ExecuteScalar()!;
        }
        Assert.That(expected, Is.GreaterThanOrEqualTo(3));

        var model = CloudSyncStatusModel.Current;
        var poll = typeof(CloudSyncStatusModel).GetMethod("PollQueueDepth", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { poll.Invoke(model, null); }
            catch (Exception ex) { error = ex; }
        });
        t.Start();
        t.Join();

        Assert.That(error, Is.Null);
        Assert.That(model.QueueDepth, Is.EqualTo((int)expected));
    }
}
