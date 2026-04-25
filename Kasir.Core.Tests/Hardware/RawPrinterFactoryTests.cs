using FluentAssertions;
using Kasir.Hardware;
using NUnit.Framework;

namespace Kasir.Core.Tests.Hardware;

[TestFixture]
public class RawPrinterFactoryTests
{
    [Test]
    public void EmptyName_ReturnsNullRawPrinter()
    {
        RawPrinterFactory.Create("windows", "").Should().BeOfType<NullRawPrinter>();
        RawPrinterFactory.Create("", "").Should().BeOfType<NullRawPrinter>();
        RawPrinterFactory.Create("serial", null).Should().BeOfType<NullRawPrinter>();
    }

    [Test]
    public void Kind_Windows_ReturnsWindowsSpoolPrinter()
    {
        RawPrinterFactory.Create("windows", "EPSON TM-T82").Should().BeOfType<WindowsSpoolPrinter>();
    }

    [Test]
    public void Kind_Serial_ReturnsSerialRawPrinter()
    {
        RawPrinterFactory.Create("serial", "COM4", 19200).Should().BeOfType<SerialRawPrinter>();
    }

    [Test]
    public void Kind_DeviceFile_ReturnsFileRawPrinter()
    {
        RawPrinterFactory.Create("device_file", "/dev/usb/lp0").Should().BeOfType<FileRawPrinter>();
    }

    [Test]
    public void Kind_Empty_LegacyFallback_LptPrefix_ReturnsFile()
    {
        RawPrinterFactory.Create("", "LPT1:").Should().BeOfType<FileRawPrinter>();
        RawPrinterFactory.Create("", "/dev/usb/lp0").Should().BeOfType<FileRawPrinter>();
    }

    [Test]
    public void Kind_Empty_LegacyFallback_OtherName_ReturnsSerial()
    {
        // Matches the original UsbReceiptPrinter behavior — anything not LPT/usb path
        // dropped through to SerialPrinter at 115200 baud. Skip the EPSON-name check
        // on Windows: if that queue happens to be installed on the test machine, the
        // smart-migration path correctly reroutes to WindowsSpoolPrinter.
        RawPrinterFactory.Create("", "COM4").Should().BeOfType<SerialRawPrinter>();
        if (!System.OperatingSystem.IsWindows())
            RawPrinterFactory.Create("", "EPSON TM-T82").Should().BeOfType<SerialRawPrinter>();
    }

    [Test]
    public void Kind_CaseInsensitive()
    {
        RawPrinterFactory.Create("Windows", "x").Should().BeOfType<WindowsSpoolPrinter>();
        RawPrinterFactory.Create("SERIAL", "COM1", 9600).Should().BeOfType<SerialRawPrinter>();
        RawPrinterFactory.Create("Device_File", "/x").Should().BeOfType<FileRawPrinter>();
    }

    [Test]
    public void Unknown_Kind_FallsBackToInfer()
    {
        RawPrinterFactory.Create("garbage", "LPT1:").Should().BeOfType<FileRawPrinter>();
        RawPrinterFactory.Create("garbage", "FOO").Should().BeOfType<SerialRawPrinter>();
    }
}

[TestFixture]
public class NullRawPrinterTests
{
    [Test]
    public void Send_AlwaysReturnsFalse_LastErrorSet()
    {
        var p = new NullRawPrinter();
        p.Send(new byte[] { 1, 2 }).Should().BeFalse();
        p.LastError.Should().Contain("printer_name belum diset");
    }
}
