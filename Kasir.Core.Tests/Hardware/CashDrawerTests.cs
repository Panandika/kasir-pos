using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Hardware;

namespace Kasir.Tests.Hardware
{
    // F51: CashDrawer.Open kicks pin 0 and, if that fails, falls back to pin 1. This pure,
    // mockable fallback had no coverage.
    [TestFixture]
    public class CashDrawerTests
    {
        private sealed class FakeRawPrinter : IRawPrinter
        {
            private readonly bool _pin0Ok;
            private readonly bool _pin1Ok;
            public List<byte> PinsTried { get; } = new List<byte>();

            public FakeRawPrinter(bool pin0Ok, bool pin1Ok)
            {
                _pin0Ok = pin0Ok;
                _pin1Ok = pin1Ok;
            }

            public string LastError => "err";

            public bool Send(byte[] data)
            {
                byte pin = data[2]; // ESC p m ... — m is the drawer pin selector
                PinsTried.Add(pin);
                return pin == 0x00 ? _pin0Ok : _pin1Ok;
            }
        }

        [Test]
        public void Open_Pin0Succeeds_DoesNotTryPin1()
        {
            var raw = new FakeRawPrinter(pin0Ok: true, pin1Ok: true);
            new CashDrawer(raw).Open().Should().BeTrue();
            raw.PinsTried.Should().Equal(new byte[] { 0x00 }, "pin 1 is only tried when pin 0 fails");
        }

        [Test]
        public void Open_Pin0Fails_FallsBackToPin1()
        {
            var raw = new FakeRawPrinter(pin0Ok: false, pin1Ok: true);
            new CashDrawer(raw).Open().Should().BeTrue();
            raw.PinsTried.Should().Equal(new byte[] { 0x00, 0x01 });
        }

        [Test]
        public void Open_BothPinsFail_ReturnsFalse()
        {
            var raw = new FakeRawPrinter(pin0Ok: false, pin1Ok: false);
            new CashDrawer(raw).Open().Should().BeFalse();
            raw.PinsTried.Should().Equal(new byte[] { 0x00, 0x01 });
        }
    }
}
