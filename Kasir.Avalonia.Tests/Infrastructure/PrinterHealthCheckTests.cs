using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Kasir.Avalonia.Infrastructure;
using Kasir.Data.Repositories;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Infrastructure;

// F29: SaleView's printer check ran `new ReceiptPrinter(_configRepo)` inside Task.Run, so
// the config reads hit the UI thread's shared SqliteConnection from a pool thread.
// SqliteConnection is not thread-safe, and DbConnection's off-thread guard only covers
// GetConnection(), not a repository built on the UI thread and used elsewhere.
[TestFixture]
public class PrinterHealthCheckTests
{
    private SqliteConnection _conn = null!;
    private ConcurrentBag<int> _queryThreads = null!;
    private string _deviceFile = null!;

    [SetUp]
    public void SetUp()
    {
        _queryThreads = new ConcurrentBag<int>();
        _deviceFile = Path.Combine(Path.GetTempPath(), "kasir-f29-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(_deviceFile, Array.Empty<byte>());

        // Stands in for the UI thread's shared connection. Every read of `config` calls
        // record_thread(), so the test sees which thread ran each statement.
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _conn.CreateFunction("record_thread", () =>
        {
            _queryThreads.Add(Environment.CurrentManagedThreadId);
            return 1;
        });
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE config_data (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, value TEXT, description TEXT);
            CREATE VIEW config AS SELECT * FROM config_data WHERE record_thread() = 1;";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        _conn.Dispose();
        try { File.Delete(_deviceFile); } catch (IOException) { }
    }

    private void SetConfig(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO config_data (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    [Test]
    public async Task DevicePrinter_ReadsConfigOnlyOnCallingThread()
    {
        SetConfig("printer_kind", "device_file");
        SetConfig("printer_name", _deviceFile);
        SetConfig("printer_baud", "9600");
        int callingThread = Environment.CurrentManagedThreadId;

        var result = await PrinterHealthCheck.RunAsync(new ConfigRepository(_conn),
            _ => throw new AssertionException("not a Windows queue"));

        Assert.That(result, Is.EqualTo(new PrinterHealth(true, true, null)));
        Assert.That(_queryThreads, Is.Not.Empty, "the check should read printer config");
        Assert.That(_queryThreads, Has.All.EqualTo(callingThread),
            "the shared connection was queried from a background thread");
    }

    [Test]
    public async Task DevicePrinter_Missing_ReportsWarning()
    {
        SetConfig("printer_kind", "device_file");
        SetConfig("printer_name", _deviceFile + "-missing");
        int callingThread = Environment.CurrentManagedThreadId;

        var result = await PrinterHealthCheck.RunAsync(new ConfigRepository(_conn), _ => null);

        Assert.That(result.Configured, Is.True);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Warning, Does.Contain("tidak ditemukan"));
        Assert.That(_queryThreads, Has.All.EqualTo(callingThread));
    }

    [Test]
    public async Task WindowsQueue_Offline_ReportsWarning()
    {
        SetConfig("printer_kind", "windows");
        SetConfig("printer_name", "EPSON TM-U220");
        int callingThread = Environment.CurrentManagedThreadId;

        var result = await PrinterHealthCheck.RunAsync(new ConfigRepository(_conn), _ => "offline");

        Assert.That(result, Is.EqualTo(new PrinterHealth(true, false, "Printer 'EPSON TM-U220' offline")));
        Assert.That(_queryThreads, Has.All.EqualTo(callingThread));
    }

    [Test]
    public async Task NoPrinterName_NotConfigured()
    {
        var result = await PrinterHealthCheck.RunAsync(new ConfigRepository(_conn),
            _ => throw new AssertionException("must not probe"));

        Assert.That(result, Is.EqualTo(new PrinterHealth(false, false, null)));
    }

    [TestCase("windows", "COM3", true)]
    [TestCase("", "EPSON TM-U220", true)]
    [TestCase("", "COM3", false)]
    [TestCase("", "LPT1", false)]
    [TestCase("", "/dev/usb/lp0", false)]
    [TestCase("serial", "EPSON", false)]
    public void IsWindowsQueue(string kind, string name, bool expected)
    {
        Assert.That(PrinterHealthCheck.IsWindowsQueue(kind, name), Is.EqualTo(expected));
    }
}
