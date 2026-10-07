using System;
using System.Collections.Generic;
using NUnit.Framework;
using Kasir.Data;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class DatabaseInitFailureMessageTests
    {
        private const string LogPath = @"C:\Kasir\logs\crash.log";

        [Test]
        public void Corrupt_TellsOperatorToContactAdminAndReRegisterFromCloud()
        {
            var ex = new DatabaseCorruptException(new List<string> { "Missing table: products" });

            string msg = DatabaseInitFailureMessage.Build(ex, LogPath);

            Assert.That(msg, Does.Contain("rusak"));
            Assert.That(msg, Does.Contain("admin"));
            Assert.That(msg, Does.Contain("Daftarkan dari cloud"));
            Assert.That(msg, Does.Contain("Missing table: products"));
            Assert.That(msg, Does.Contain(LogPath));
        }

        [Test]
        public void OtherFailure_ShowsReasonAndLogPath_NotCorruptAdvice()
        {
            var ex = new UnauthorizedAccessException("Access to the path is denied.");

            string msg = DatabaseInitFailureMessage.Build(ex, LogPath);

            Assert.That(msg, Does.Contain("Access to the path is denied."));
            Assert.That(msg, Does.Contain(LogPath));
            Assert.That(msg, Does.Contain("admin"));
            Assert.That(msg, Does.Not.Contain("rusak"));
        }

        [Test]
        public void WrappedCorruptException_IsStillRecognisedAsCorrupt()
        {
            var ex = new AggregateException(new DatabaseCorruptException(new List<string> { "bad" }));

            Assert.That(DatabaseInitFailureMessage.Build(ex, LogPath), Does.Contain("rusak"));
        }

        [Test]
        public void NullException_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => DatabaseInitFailureMessage.Build(null, LogPath));
        }
    }
}
