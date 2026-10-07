using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Hardware;

namespace Kasir.Tests.Hardware
{
    // F31: the footer status poller calls IsAvailable() every 30 s. It used to send
    // ESC @ (EscPosCommands.Init) down the real print path — a spool job on Windows
    // queues, bytes on serial/LPT ports. Probing must not send any print data.
    [TestFixture]
    public class ReceiptPrinterProbeTests
    {
        private sealed class RecordingRawPrinter : IRawPrinter
        {
            public List<byte[]> Sent { get; } = new List<byte[]>();
            public int ProbeCalls { get; private set; }
            public bool Reachable { get; set; } = true;
            public string LastError => null;

            public bool Send(byte[] data)
            {
                Sent.Add(data);
                return true;
            }

            public bool IsReachable()
            {
                ProbeCalls++;
                return Reachable;
            }
        }

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "kasir-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        [Test]
        public void IsAvailable_SendsNoPrintData()
        {
            var raw = new RecordingRawPrinter();

            new ReceiptPrinter(raw).IsAvailable();

            raw.Sent.Should().BeEmpty("an availability probe must not print anything");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void IsAvailable_ReturnsProbeResult(bool reachable)
        {
            var raw = new RecordingRawPrinter { Reachable = reachable };

            new ReceiptPrinter(raw).IsAvailable().Should().Be(reachable);
            raw.ProbeCalls.Should().Be(1);
        }

        [Test]
        public void IsReachable_MissingDeviceFile_FalseWithError()
        {
            var raw = new FileRawPrinter(Path.Combine(_tempDir, "no-such-device"));

            raw.IsReachable().Should().BeFalse();
            raw.LastError.Should().Contain("tidak ditemukan");
        }

        [Test]
        public void IsReachable_MissingSerialPort_FalseWithError()
        {
            var raw = new SerialRawPrinter("KASIR-NO-SUCH-PORT", 9600);

            raw.IsReachable().Should().BeFalse();
            raw.LastError.Should().Contain("tidak ditemukan");
        }

        [Test]
        public void IsReachable_NullPrinter_False()
        {
            new NullRawPrinter().IsReachable().Should().BeFalse();
        }

        [Test]
        public void IsAvailable_DeviceFile_DoesNotWriteToDevice()
        {
            string device = Path.Combine(_tempDir, "usb-lp0");
            File.WriteAllBytes(device, Array.Empty<byte>());

            bool available = new ReceiptPrinter(new FileRawPrinter(device)).IsAvailable();

            available.Should().BeTrue();
            new FileInfo(device).Length.Should().Be(0, "probing must not write ESC @ to the device");
        }
    }
}
