using System;
using System.IO;
using FluentAssertions;
using Kasir.Hardware;
using NUnit.Framework;

namespace Kasir.Core.Tests.Hardware;

// Send must have written every byte by the time it returns true. ESCPOS_NET's FilePrinter
// queued writes on a background task and dropped them on Dispose, so Send reported success
// while nothing reached the printer.
[TestFixture]
public class FileRawPrinterTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "kasir-print-" + Guid.NewGuid().ToString("N") + ".bin");

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public void Send_WritesExactBytesThroughEscPosFilePrinter()
    {
        File.WriteAllBytes(_path, Array.Empty<byte>());
        var bytes = new byte[] { 0x1B, 0x40, (byte)'O', (byte)'K', 0x0A };

        var printer = new FileRawPrinter(_path);
        bool ok = printer.Send(bytes);

        ok.Should().BeTrue(printer.LastError);
        File.ReadAllBytes(_path).Should().Equal(bytes);
    }

    [Test]
    public void Send_MissingDevice_ReturnsFalseAndDoesNotCreateFile()
    {
        var printer = new FileRawPrinter(_path);

        printer.Send(new byte[] { 0x1B, 0x40 }).Should().BeFalse();
        printer.LastError.Should().NotBeNullOrEmpty();
        File.Exists(_path).Should().BeFalse();
    }
}
