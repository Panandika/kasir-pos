using NUnit.Framework;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Kasir.Help;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class ContextCollectorTests
    {
        [Test]
        public void Build_IncludesAllExpectedFields()
        {
            var c = new ContextCollector(new PiiScrubber());
            string json = c.BuildAttachmentsJson("sinar-makmur", "01", "2.4.1", "INV-047", "");

            var obj = JObject.Parse(json);
            ((string)obj["store_id"]).Should().Be("sinar-makmur");
            ((string)obj["register_id"]).Should().Be("01");
            ((string)obj["app_version"]).Should().Be("2.4.1");
            ((string)obj["last_invoice"]).Should().Be("INV-047");
        }

        [Test]
        public void Build_ScrubsPiiFromLastError()
        {
            var c = new ContextCollector(new PiiScrubber());
            string json = c.BuildAttachmentsJson(
                "sinar-makmur", "01", "2.4.1", "INV-047",
                "Failed: paid Rp 100.000 with card 4111111111111111");

            json.Should().Contain("[redacted-rp]");
            json.Should().Contain("[redacted-pan]");
            json.Should().NotContain("4111111111111111");
            json.Should().NotContain("Rp 100.000");
        }

        [Test]
        public void Build_HandlesNullLastError()
        {
            var c = new ContextCollector(new PiiScrubber());
            string json = c.BuildAttachmentsJson("sm", "01", "2.4.1", null, null);

            var obj = JObject.Parse(json);
            ((string)obj["last_invoice"]).Should().Be("");
            ((string)obj["last_error"]).Should().Be("");
        }
    }
}
