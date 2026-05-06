using FluentAssertions;
using Kasir.Hardware;
using NUnit.Framework;

namespace Kasir.Core.Tests.Hardware;

[TestFixture]
public class WindowsSpoolPrinterTests
{
    [Test]
    public void EmptyName_ReturnsFalse_LastErrorSet()
    {
        var p = new WindowsSpoolPrinter("");
        p.Send(new byte[] { 1 }).Should().BeFalse();
        p.LastError.Should().Contain("Nama printer kosong");
    }

    [Test]
    public void EmptyData_ReturnsFalse_LastErrorSet()
    {
        var p = new WindowsSpoolPrinter("AnyPrinter");
        p.Send(System.Array.Empty<byte>()).Should().BeFalse();
        p.LastError.Should().Contain("Data kosong");
    }

    [Test]
    public void NonWindows_ReturnsFalse_WithFriendlyError()
    {
        if (System.OperatingSystem.IsWindows())
        {
            Assert.Ignore("Test only meaningful on non-Windows.");
            return;
        }
        var p = new WindowsSpoolPrinter("AnyPrinter");
        p.Send(new byte[] { 1 }).Should().BeFalse();
        p.LastError.Should().Contain("Windows spooler tidak tersedia");
    }
}
