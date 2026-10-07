using System;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Kasir.Utils;

namespace Kasir.Tests.Utils
{
    [TestFixture]
    public class CrashLogTests
    {
        private static readonly DateTime Stamp = new DateTime(2026, 10, 7, 14, 3, 11);
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-crashlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception Thrown(Exception ex)
        {
            try { throw ex; }
            catch (Exception caught) { return caught; }
        }

        [Test]
        public void FormatEntry_ContainsTimestampVersionSourceTypeMessageAndStack()
        {
            var ex = Thrown(new InvalidOperationException("kaboom"));

            string entry = CrashLog.FormatEntry("ShellWindow.InitializeDatabase", ex, Stamp, "2.9.2");

            Assert.That(entry, Does.Contain("2026-10-07 14:03:11"));
            Assert.That(entry, Does.Contain("v2.9.2"));
            Assert.That(entry, Does.Contain("ShellWindow.InitializeDatabase"));
            Assert.That(entry, Does.Contain("Type: System.InvalidOperationException"));
            Assert.That(entry, Does.Contain("Message: kaboom"));
            Assert.That(entry, Does.Contain(nameof(Thrown)), "stack trace must be included");
        }

        [Test]
        public void FormatEntry_IncludesInnerExceptions()
        {
            var inner = Thrown(new IOException("disk gone"));
            var outer = Thrown(new InvalidOperationException("wrapper", inner));

            string entry = CrashLog.FormatEntry("src", outer, Stamp, "1.0.0");

            Assert.That(entry, Does.Contain("Type: System.InvalidOperationException"));
            Assert.That(entry, Does.Contain("Type: System.IO.IOException"));
            Assert.That(entry, Does.Contain("Message: disk gone"));
            Assert.That(entry.IndexOf("disk gone", StringComparison.Ordinal),
                Is.GreaterThan(entry.IndexOf("wrapper", StringComparison.Ordinal)));
        }

        [Test]
        public void FormatEntry_IncludesEveryAggregateInnerException()
        {
            var agg = new AggregateException(new ArgumentException("first"), new TimeoutException("second"));

            string entry = CrashLog.FormatEntry("src", agg, Stamp, "1.0.0");

            Assert.That(entry, Does.Contain("Message: first"));
            Assert.That(entry, Does.Contain("Type: System.TimeoutException"));
            Assert.That(entry, Does.Contain("Message: second"));
        }

        [Test]
        public void FormatEntry_NullException_DoesNotThrow()
        {
            string entry = null;
            Assert.DoesNotThrow(() => entry = CrashLog.FormatEntry("src", null, Stamp, "1.0.0"));
            Assert.That(entry, Does.Contain("src"));
        }

        private sealed class HostileException : Exception
        {
            public override string Message => throw new InvalidOperationException("Message getter");
            public override string StackTrace => throw new InvalidOperationException("StackTrace getter");
        }

        [Test]
        public void FormatEntry_ExceptionWhosePropertiesThrow_DoesNotThrow()
        {
            string entry = null;
            Assert.DoesNotThrow(() => entry = CrashLog.FormatEntry("src", new HostileException(), Stamp, "1.0.0"));
            Assert.That(entry, Does.Contain("HostileException"));
        }

        [Test]
        public void WriteTo_CreatesLogsDirectoryAndAppendsEntries()
        {
            string path = Path.Combine(_dir, "logs", "crash.log");

            Assert.That(CrashLog.WriteTo(path, "first-source", new Exception("one"), Stamp, 1024 * 1024), Is.True);
            Assert.That(CrashLog.WriteTo(path, "second-source", new Exception("two"), Stamp, 1024 * 1024), Is.True);

            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("first-source"));
            Assert.That(text, Does.Contain("second-source"));
        }

        [Test]
        public void WriteTo_OverSizeCap_RotatesToOldFile()
        {
            string path = Path.Combine(_dir, "crash.log");
            File.WriteAllText(path, new string('x', 2000));

            Assert.That(CrashLog.WriteTo(path, "after-rotate", new Exception("new"), Stamp, maxBytes: 1000), Is.True);

            Assert.That(File.ReadAllText(path + ".old"), Is.EqualTo(new string('x', 2000)));
            string current = File.ReadAllText(path);
            Assert.That(current, Does.Contain("after-rotate"));
            Assert.That(current, Does.Not.Contain("xxxx"));
        }

        [Test]
        public void WriteTo_UnwritableDirectory_ReturnsFalseAndDoesNotThrow()
        {
            // A regular file where the logs directory should be: CreateDirectory fails on
            // every OS, standing in for a read-only / locked install folder.
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "not a directory");
            string path = Path.Combine(blocker, "logs", "crash.log");

            bool written = true;
            Assert.DoesNotThrow(() => written = CrashLog.WriteTo(path, "src", new Exception("x"), Stamp, 1024));
            Assert.That(written, Is.False);
        }

        [Test]
        public void WriteTo_NullOrEmptyPath_ReturnsFalseAndDoesNotThrow()
        {
            Assert.That(CrashLog.WriteTo(null, "src", new Exception("x"), Stamp, 1024), Is.False);
            Assert.That(CrashLog.WriteTo("", "src", new Exception("x"), Stamp, 1024), Is.False);
        }

        [Test]
        public void LogPath_IsLogsCrashLogNextToTheExe()
        {
            Assert.That(CrashLog.LogPath,
                Is.EqualTo(Path.Combine(AppContext.BaseDirectory, "logs", "crash.log")));
        }
    }
}
