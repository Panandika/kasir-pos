using System.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Hardware;

namespace Kasir.Tests.Hardware
{
    // F27: App.BuildPrinter ran every 30 s (printer status poll) and opened a new
    // SqliteConnection each time that nothing disposed. Building the printer from config
    // must release the connection it opened.
    [TestFixture]
    public class ReceiptPrinterFromConfigTests
    {
        private static SqliteConnection OpenConfigDb()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"CREATE TABLE config (key TEXT PRIMARY KEY, value TEXT, description TEXT);
                                    INSERT INTO config (key, value) VALUES ('printer_kind', 'device_file'),
                                                                           ('printer_name', '/dev/usb/lp0');";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        [Test]
        public void FromConfig_DisposesTheConnectionItOpened()
        {
            SqliteConnection opened = null;

            var printer = ReceiptPrinter.FromConfig(() => opened = OpenConfigDb());

            printer.Should().NotBeNull();
            opened.Should().NotBeNull();
            opened.State.Should().Be(ConnectionState.Closed, "the poller must not leak a connection per poll");
        }
    }
}
