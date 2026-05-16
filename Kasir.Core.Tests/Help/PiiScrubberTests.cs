using NUnit.Framework;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Kasir.Help;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class PiiScrubberTests
    {
        [Test]
        public void Scrub_RedactsPanLikeDigits()
        {
            var s = new PiiScrubber();
            string r = s.Scrub("card 4111 1111 1111 1111 used");
            r.Should().Contain("[redacted-pan]");
            r.Should().NotContain("4111 1111 1111");
            s.ReplacementCount.Should().BeGreaterThan(0);
        }

        [Test]
        public void Scrub_RedactsRupiah()
        {
            var s = new PiiScrubber();
            string r = s.Scrub("paid Rp 123.500 cash");
            r.Should().Contain("[redacted-rp]");
        }

        [Test]
        public void Scrub_RedactsPinLikeStandalone4to6Digits()
        {
            var s = new PiiScrubber();
            string r = s.Scrub("entered pin 4321 then submitted");
            r.Should().Contain("[redacted-num]");
        }

        [Test]
        public void Scrub_LeavesNormalProseAlone()
        {
            var s = new PiiScrubber();
            string r = s.Scrub("printer macet setelah cetak ke tiga");
            r.Should().Be("printer macet setelah cetak ke tiga");
            s.ReplacementCount.Should().Be(0);
        }

        [Test]
        public void ScrubJson_RecursesIntoNestedFields()
        {
            var s = new PiiScrubber();
            var input = JObject.Parse(@"{
                ""ok"": ""no pii"",
                ""nested"": {
                    ""err"": ""oops Rp 50.000 lost"",
                    ""arr"": [""card 4111111111111111"", ""ok""]
                }
            }");

            var scrubbed = s.ScrubJson(input);
            scrubbed.ToString().Should().Contain("[redacted-rp]");
            scrubbed.ToString().Should().Contain("[redacted-pan]");
            scrubbed["ok"].ToString().Should().Be("no pii");
            s.ReplacementCount.Should().BeGreaterOrEqualTo(2);
        }

        [Test]
        public void ScrubJsonString_RoundTripsFromString()
        {
            var s = new PiiScrubber();
            string input = "{\"x\":\"Rp 1.000.000\",\"y\":[1,2,3]}";
            string r = s.ScrubJsonString(input);
            r.Should().Contain("[redacted-rp]");
        }
    }
}
